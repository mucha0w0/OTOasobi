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
    readonly Dictionary<int, Pending> _pending = [];
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

    public async Task<PreviewPicture> LoadAsync(
        string path,
        double t0,
        double t1,
        int columns,
        int rows,
        Action<PreviewPicture>? onWave = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
            _pending[id] = new Pending(done, onWave);
        var request = JsonSerializer.Serialize(new
        {
            id,
            cmd = "preview",
            spec = "index",
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
        var spec = root.TryGetProperty("spec_index", out var specValue) ? specValue.GetString() ?? "" : "";
        var skipped = root.TryGetProperty("skipped", out var skippedValue)
            && skippedValue.ValueKind == JsonValueKind.True;
        var sampleRate = 0;
        if (root.TryGetProperty("sr", out var srValue) && srValue.TryGetInt32(out var parsed))
            sampleRate = parsed;
        return new PreviewPicture(
            ok,
            error,
            ReadDoubles(root, "wave_lo"),
            ReadDoubles(root, "wave_hi"),
            png,
            spec,
            Count(root, "spec_columns"),
            Count(root, "spec_rows"),
            skipped,
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

    static int Count(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;

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
                    {
                        if (eventValue.GetString() == "wave")
                        {
                            Pending? waiting;
                            lock (_pending)
                                _pending.TryGetValue(id, out waiting);
                            try
                            {
                                waiting?.OnWave?.Invoke(Read(root));
                            }
                            catch (Exception)
                            {
                            }
                        }

                        continue;
                    }

                    Pending? done;
                    lock (_pending)
                    {
                        if (!_pending.Remove(id, out done))
                            continue;
                    }

                    done.Done.TrySetResult(line);
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
        List<Pending> pending;
        lock (_pending)
        {
            pending = _pending.Values.ToList();
            _pending.Clear();
        }

        foreach (var done in pending)
            done.Done.TrySetException(new IOException(message));
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

sealed class Pending(TaskCompletionSource<string> done, Action<PreviewPicture>? onWave)
{
    public TaskCompletionSource<string> Done { get; } = done;
    public Action<PreviewPicture>? OnWave { get; } = onWave;
}

sealed record PreviewPicture(
    bool Ok,
    string? Error,
    double[] Lo,
    double[] Hi,
    string Png,
    string Spec,
    int SpecColumns,
    int SpecRows,
    bool Skipped,
    double SpecT0,
    double SpecT1,
    double WaveT0,
    double WaveT1,
    int SampleRate,
    double Duration,
    double[] Loudness);
