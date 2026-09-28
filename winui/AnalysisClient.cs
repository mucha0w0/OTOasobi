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

    public async Task<FolderData> AnalyzeFolderAsync(string path, Action<int, int>? onProgress = null, bool force = false)
    {
        using var doc = await SendAsync(
            id => JsonSerializer.Serialize(new { id, cmd = "analyze", path, force }),
            onProgress).ConfigureAwait(true);
        return ReadFolder(doc.RootElement);
    }

    public async Task<PreviewData> PreviewAsync(string path, double t0, double t1, int columns, int rows)
    {
        using var doc = await SendAsync(
            id => JsonSerializer.Serialize(new { id, cmd = "preview", path, t0, t1, columns, rows }),
            null).ConfigureAwait(true);
        return ReadPreview(doc.RootElement);
    }

    public async Task SaveCacheAsync(string folder, IReadOnlyList<WavFile> files)
    {
        using var doc = await SendAsync(id => JsonSerializer.Serialize(new
        {
            id,
            cmd = "save",
            folder,
            files = FilePayload(files),
        }), null).ConfigureAwait(true);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        if (ok)
            return;
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        throw new IOException(string.IsNullOrEmpty(error) ? "保存データを書けませんでした。" : error);
    }

    public async Task<AnalyzeResult> WriteAsync(string folder, IReadOnlyList<WavFile> files)
    {
        using var doc = await SendAsync(id => JsonSerializer.Serialize(new
        {
            id,
            cmd = "write",
            folder,
            files = FilePayload(files),
        }), null).ConfigureAwait(true);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        string? txt = root.TryGetProperty("txt", out var txtValue) ? txtValue.GetString() : null;
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        string? notice = root.TryGetProperty("notice", out var noticeValue) ? noticeValue.GetString() : null;
        return new AnalyzeResult(ok, txt, error, notice);
    }

    async Task<JsonDocument> SendAsync(Func<int, string> build, Action<int, int>? onProgress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new IOException("解析プロセスが終了しました。");

        var id = Interlocked.Increment(ref _nextId);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = done;
        if (onProgress != null)
            _progress[id] = onProgress;
        var request = build(id);
        lock (_writeLock)
        {
            _input.WriteLine(request);
            _input.Flush();
        }

        try
        {
            var line = await done.Task.ConfigureAwait(true);
            return JsonDocument.Parse(line);
        }
        finally
        {
            _progress.TryRemove(id, out _);
        }
    }

    static FolderData ReadFolder(JsonElement root)
    {
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        string? notice = root.TryGetProperty("notice", out var noticeValue) ? noticeValue.GetString() : null;
        var files = new List<WavFile>();
        if (root.TryGetProperty("files", out var filesValue) && filesValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filesValue.EnumerateArray())
            {
                var file = new WavFile
                {
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Duration = item.TryGetProperty("duration", out var duration) ? duration.GetDouble() : 0,
                    Engine = item.TryGetProperty("engine", out var engine) ? engine.GetString() ?? "" : "",
                };
                if (item.TryGetProperty("spans", out var spans) && spans.ValueKind == JsonValueKind.Array)
                {
                    foreach (var span in spans.EnumerateArray())
                    {
                        file.Spans.Add(new PhoneSpan
                        {
                            Start = span.TryGetProperty("start", out var start) ? start.GetDouble() : 0,
                            End = span.TryGetProperty("end", out var end) ? end.GetDouble() : 0,
                            Label = span.TryGetProperty("label", out var label) ? label.GetString() ?? "" : "",
                            Token = span.TryGetProperty("token", out var token) ? token.GetString() ?? "" : "",
                        });
                    }
                }

                files.Add(file);
            }
        }

        var cached = root.TryGetProperty("cached", out var cachedValue) && cachedValue.ValueKind == JsonValueKind.True;
        return new FolderData(ok, error, notice, files, cached);
    }

    static object[] FilePayload(IReadOnlyList<WavFile> files)
    {
        return files.Select(file => new
        {
            name = file.Name,
            duration = file.Duration,
            engine = file.Engine,
            spans = file.Spans.Select(span => new
            {
                start = span.Start,
                end = span.End,
                label = span.Label,
                token = span.Token,
            }).ToArray(),
        }).ToArray();
    }

    static PreviewData ReadPreview(JsonElement root)
    {
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        var lo = ReadDoubles(root, "wave_lo");
        var hi = ReadDoubles(root, "wave_hi");
        var loudness = ReadDoubles(root, "loudness");
        var png = Array.Empty<byte>();
        if (root.TryGetProperty("spec_png", out var pngValue) && pngValue.ValueKind == JsonValueKind.String)
        {
            var text = pngValue.GetString();
            if (!string.IsNullOrEmpty(text))
                png = Convert.FromBase64String(text);
        }

        var t0 = root.TryGetProperty("spec_t0", out var t0Value) ? t0Value.GetDouble() : 0;
        var t1 = root.TryGetProperty("spec_t1", out var t1Value) ? t1Value.GetDouble() : 0;
        var waveT0 = root.TryGetProperty("wave_t0", out var waveT0Value) ? waveT0Value.GetDouble() : 0;
        var waveT1 = root.TryGetProperty("wave_t1", out var waveT1Value) ? waveT1Value.GetDouble() : 0;
        var samples = root.TryGetProperty("wave_samples", out var samplesValue)
            && samplesValue.ValueKind == JsonValueKind.True;
        var sampleRate = 0;
        if (root.TryGetProperty("sr", out var srValue) && srValue.TryGetInt32(out var parsedRate))
            sampleRate = parsedRate;
        return new PreviewData(ok, error, lo, hi, loudness, png, t0, t1, waveT0, waveT1, samples, sampleRate);
    }

    static double[] ReadDoubles(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        var values = new double[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray())
            values[index++] = item.GetDouble();
        return values;
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

sealed record AnalyzeResult(bool Ok, string? Txt, string? Error, string? Notice);
