using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Otoasobi;

sealed class AnalysisClient : IDisposable
{
    readonly Process _process;
    readonly StreamWriter _input;
    readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    readonly ConcurrentDictionary<int, Action<int, int>> _progress = new();
    readonly object _writeLock = new();
    int _nextId;
    bool _disposed;

    public AnalysisClient()
    {
        var (repo, python) = Locate();
        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = "-X utf8 -m otoasobi.worker",
            WorkingDirectory = repo,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!_process.Start())
            throw new IOException("解析プロセスを起動できませんでした。");

        _input = _process.StandardInput;
        _input.NewLine = "\n";
        _process.BeginErrorReadLine();
        _process.Exited += (_, _) => FailPending("解析プロセスが終了しました。");
        _ = Task.Run(ReadLoop);
    }

    public async Task<AnalyzeResult> AnalyzeAsync(string path, Action<int, int>? onProgress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new IOException("解析プロセスが終了しました。");

        var id = Interlocked.Increment(ref _nextId);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = done;
        if (onProgress != null)
            _progress[id] = onProgress;
        var request = JsonSerializer.Serialize(new { id, cmd = "analyze", path });
        lock (_writeLock)
        {
            _input.WriteLine(request);
            _input.Flush();
        }

        string line;
        try
        {
            line = await done.Task.ConfigureAwait(true);
        }
        finally
        {
            _progress.TryRemove(id, out _);
        }
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        string? txt = root.TryGetProperty("txt", out var txtValue) ? txtValue.GetString() : null;
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        return new AnalyzeResult(ok, txt, error);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        FailPending("解析プロセスが終了しました。");
        try
        {
            _input.Dispose();
        }
        catch (IOException)
        {
        }

        try
        {
            if (!_process.HasExited && !_process.WaitForExit(1500))
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
    }

    void ReadLoop()
    {
        try
        {
            while (_process.StandardOutput.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.Number)
                        continue;
                    var id = idValue.GetInt32();
                    if (root.TryGetProperty("event", out var eventValue)
                        && eventValue.ValueKind == JsonValueKind.String
                        && eventValue.GetString() == "progress")
                    {
                        var doneCount = root.TryGetProperty("done", out var doneValue) && doneValue.TryGetInt32(out var parsedDone)
                            ? parsedDone
                            : 0;
                        var total = root.TryGetProperty("total", out var totalValue) && totalValue.TryGetInt32(out var parsedTotal)
                            ? parsedTotal
                            : 0;
                        if (_progress.TryGetValue(id, out var progress))
                        {
                            try
                            {
                                progress(doneCount, total);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        continue;
                    }

                    if (_pending.TryRemove(id, out var done))
                        done.TrySetResult(line);
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            FailPending("解析プロセスが終了しました。");
        }
    }

    void FailPending(string message)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var done))
                done.TrySetException(new IOException(message));
        }
    }

    static (string Repo, string Python) Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var python = Path.Combine(dir.FullName, ".venv", "Scripts", "python.exe");
            var worker = Path.Combine(dir.FullName, "otoasobi", "worker.py");
            if (File.Exists(python) && File.Exists(worker))
                return (dir.FullName, python);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("解析用の Python (.venv) が見つかりません。");
    }
}

sealed record AnalyzeResult(bool Ok, string? Txt, string? Error);
