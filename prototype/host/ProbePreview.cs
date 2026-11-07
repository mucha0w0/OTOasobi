using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Otoasobi.Probe;

using Otoasobi;

// preview だけを話す。AnalysisClient は oto の保存型に依存するので、この試作には含めない。
sealed class ProbePreview : IDisposable
{
    readonly Process _process;
    readonly StreamWriter _input;
    readonly object _writeLock = new();
    readonly Dictionary<int, TaskCompletionSource<string>> _pending = [];
    int _nextId;
    bool _disposed;

    public ProbePreview()
    {
        var (repo, python, bundled) = Locate();
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
        psi.Environment["PYTHONPATH"] = repo;
        psi.Environment["TORCH_HOME"] = ResolveTorchHome();
        psi.Environment["TQDM_DISABLE"] = "1";
        if (bundled)
            psi.Environment["PYTHONNOUSERSITE"] = "1";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!_process.Start())
            throw new IOException("解析プロセスを起動できませんでした。");

        _input = _process.StandardInput;
        _input.NewLine = "\n";
        _process.BeginErrorReadLine();
        _process.Exited += (_, _) => FailPending("解析プロセスが終了しました。");
        _ = Task.Run(ReadLoop);
    }

    public async Task<PreviewPicture> LoadAsync(string path, double t0, double t1, int columns, int rows)
    {
        var id = Interlocked.Increment(ref _nextId);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
            _pending[id] = done;
        var request = JsonSerializer.Serialize(new
        {
            id,
            cmd = "preview",
            path,
            t0,
            t1,
            columns,
            rows,
        });
        lock (_writeLock)
        {
            _input.WriteLine(request);
            _input.Flush();
        }

        var line = await done.Task.ConfigureAwait(true);
        using var doc = JsonDocument.Parse(line);
        return Read(doc.RootElement);
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

    static PreviewPicture Read(JsonElement root)
    {
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        var png = root.TryGetProperty("spec_png", out var pngValue) ? pngValue.GetString() ?? "" : "";
        var sampleRate = 0;
        if (root.TryGetProperty("sr", out var srValue) && srValue.TryGetInt32(out var parsed))
            sampleRate = parsed;
        return new PreviewPicture(
            ok,
            error,
            ReadDoubles(root, "wave_lo"),
            ReadDoubles(root, "wave_hi"),
            png,
            Number(root, "spec_t0"),
            Number(root, "spec_t1"),
            Number(root, "wave_t0"),
            Number(root, "wave_t1"),
            sampleRate,
            Number(root, "duration"),
            ReadDoubles(root, "loudness"));
    }

    static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : 0;

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
                    if (!root.TryGetProperty("id", out var idValue) || !idValue.TryGetInt32(out var id))
                        continue;
                    if (root.TryGetProperty("event", out var eventValue)
                        && eventValue.ValueKind == JsonValueKind.String)
                        continue;
                    TaskCompletionSource<string>? done;
                    lock (_pending)
                    {
                        if (!_pending.Remove(id, out done))
                            continue;
                    }

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
        List<TaskCompletionSource<string>> pending;
        lock (_pending)
        {
            pending = _pending.Values.ToList();
            _pending.Clear();
        }

        foreach (var done in pending)
            done.TrySetException(new IOException(message));
    }

    static string ResolveTorchHome()
    {
        if (File.Exists(AppPaths.ModelFile))
            return AppPaths.ModelsRoot;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cached = Path.Combine(profile, ".cache", "torch", "hub", "checkpoints", "model.pt");
        if (File.Exists(cached))
            return Path.Combine(profile, ".cache", "torch");

        return AppPaths.ModelsRoot;
    }

    static (string Repo, string Python, bool Bundled) Locate()
    {
        var root = FindScriptRoot();
        if (root == null)
            throw new FileNotFoundException("解析用の Python が見つかりません。");

        if (File.Exists(AppPaths.EnginePython))
            return (root, AppPaths.EnginePython, true);

        var slim = Path.Combine(root, "python", "python.exe");
        if (File.Exists(slim))
            return (root, slim, true);

        var venv = Path.Combine(root, ".venv", "Scripts", "python.exe");
        if (File.Exists(venv))
            return (root, venv, false);

        throw new FileNotFoundException("解析用の Python が見つかりません。");
    }

    static string? FindScriptRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "otoasobi", "worker.py")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}

sealed record PreviewPicture(
    bool Ok,
    string? Error,
    double[] Lo,
    double[] Hi,
    string Png,
    double SpecT0,
    double SpecT1,
    double WaveT0,
    double WaveT1,
    int SampleRate,
    double Duration,
    double[] Loudness);
