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
    readonly ConcurrentDictionary<int, Action<string, double, string>> _fileProgress = new();
    readonly object _writeLock = new();
    int _nextId;
    bool _disposed;

    public AnalysisClient()
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
        // ダウンロード表示が改行せず、標準エラーが詰まると解析が戻ってこない。
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

    public async Task<FolderData> AnalyzeFolderAsync(
        string path,
        Action<int, int>? onProgress = null,
        bool force = false,
        Action<string, double, string>? onFile = null)
    {
        using var doc = await SendAsync(
            id => JsonSerializer.Serialize(new { id, cmd = "analyze", path, force }),
            onProgress,
            onFile).ConfigureAwait(true);
        return ReadFolder(doc.RootElement);
    }

    public async Task<FolderData> AnalyzeFilesAsync(
        string folder,
        IReadOnlyList<string> names,
        Action<int, int>? onProgress = null,
        Action<string, double, string>? onFile = null)
    {
        using var doc = await SendAsync(
            id => JsonSerializer.Serialize(new { id, cmd = "analyze", path = folder, only = names }),
            onProgress,
            onFile).ConfigureAwait(true);
        return ReadFolder(doc.RootElement);
    }

    public async Task<FolderData> AnalyzeFileAsync(
        string folder,
        string name,
        Action<int, int>? onProgress = null,
        Action<string, double, string>? onFile = null)
    {
        using var doc = await SendAsync(
            id => JsonSerializer.Serialize(new { id, cmd = "analyze", path = folder, only = name }),
            onProgress,
            onFile).ConfigureAwait(true);
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

    public async Task<AnalyzeResult> WriteAsync(
        string folder,
        IReadOnlyList<WavFile> files,
        IReadOnlyList<OtoWritePart> parts,
        string scope)
    {
        using var doc = await SendAsync(id => JsonSerializer.Serialize(new
        {
            id,
            cmd = "write",
            folder,
            files = FilePayload(files),
            scope,
            parts = parts.Select(part => new
            {
                config = ConfigPayload(part.Config),
                adjustments = AdjustPayload(part.Adjustments),
            }).ToArray(),
        }), null).ConfigureAwait(true);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        string? txt = root.TryGetProperty("txt", out var txtValue) ? txtValue.GetString() : null;
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        string? notice = root.TryGetProperty("notice", out var noticeValue) ? noticeValue.GetString() : null;
        return new AnalyzeResult(ok, txt, error, notice);
    }

    public async Task<FrqResult> WriteFrqAsync(
        string folder,
        string mode,
        object? only,
        object? clear,
        Action<int, int>? onProgress = null,
        Action<string, double, string>? onFile = null)
    {
        using var doc = await SendAsync(
            id => JsonSerializer.Serialize(new { id, cmd = "frq", folder, mode, only, clear }),
            onProgress,
            onFile).ConfigureAwait(true);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        var written = root.TryGetProperty("written", out var writtenValue) && writtenValue.TryGetInt32(out var parsed)
            ? parsed
            : 0;
        string? error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
        string? notice = root.TryGetProperty("notice", out var noticeValue) ? noticeValue.GetString() : null;
        return new FrqResult(ok, written, error, notice);
    }

    async Task<JsonDocument> SendAsync(
        Func<int, string> build,
        Action<int, int>? onProgress,
        Action<string, double, string>? onFile = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new IOException("解析プロセスが終了しました。");

        var id = Interlocked.Increment(ref _nextId);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = done;
        if (onProgress != null)
            _progress[id] = onProgress;
        if (onFile != null)
            _fileProgress[id] = onFile;
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
            _fileProgress.TryRemove(id, out _);
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

    static object ConfigPayload(OtoExport config) => new
    {
        offset_ms = config.OffsetMs,
        consonant_ms = config.ConsonantMs,
        blank = config.Blank,
        overlap = config.Mode == "renzoku" ? (object)config.OverlapAt : config.Overlap,
        mode = config.Mode,
        rest = config.Mode == "renzoku"
            ? new
            {
                offset_ms = config.RestOffsetMs,
                consonant_ms = config.RestConsonantMs,
                blank = config.RestBlank,
                overlap = config.RestOverlap,
            }
            : null,
        lead = config.Mode == "cvvc"
            ? new
            {
                offset_ms = config.LeadOffsetMs,
                consonant_ms = config.LeadConsonantMs,
                blank = config.LeadBlank,
                overlap = config.LeadOverlap,
            }
            : null,
        vc_offset_ms = config.VcOffsetMs,
        vc_consonant = config.VcConsonant,
        vc_blank = config.VcBlank,
        vc_overlap = config.VcOverlapAt,
    };

    static object[] AdjustPayload(IReadOnlyList<OtoAdjustedEntry>? entries)
    {
        if (entries == null || entries.Count == 0)
            return [];
        return entries.Where(entry => entry.Any || !string.IsNullOrEmpty(entry.Name)).Select(entry => new
        {
            file = entry.File,
            index = entry.Index,
            alias = entry.Alias,
            name = string.IsNullOrEmpty(entry.Name) ? null : entry.Name,
            offset = entry.Offset,
            overlap = entry.Overlap,
            preutter = entry.Preutter,
            consonant = entry.Consonant,
            blank = entry.Blank,
        }).ToArray();
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
        var fileDuration = root.TryGetProperty("duration", out var durationValue) ? durationValue.GetDouble() : 0;
        return new PreviewData(
            ok, error, lo, hi, loudness, png, t0, t1, waveT0, waveT1, samples, sampleRate, fileDuration);
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
                        && eventValue.ValueKind == JsonValueKind.String)
                    {
                        var eventName = eventValue.GetString();
                        if (eventName == "progress")
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

                        if (eventName == "file")
                        {
                            var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
                            var fileDone = root.TryGetProperty("done", out var fileDoneValue) && fileDoneValue.TryGetDouble(out var parsedFile)
                                ? parsedFile
                                : 0;
                            var label = root.TryGetProperty("label", out var labelValue) ? labelValue.GetString() ?? "" : "";
                            if (_fileProgress.TryGetValue(id, out var fileProgress))
                            {
                                try
                                {
                                    fileProgress(name, fileDone, label);
                                }
                                catch (Exception)
                                {
                                }
                            }

                            continue;
                        }
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

    // アプリ用の model.pt が無いときは、既にある torch のキャッシュを使う。
    // 空の保存先のままだと、音素対応の途中で 1.2GB を取り直して止まる。
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

    // 高精度エンジン、同梱の音響用 Python、開発用 .venv の順。スクリプトはアプリ側を使う。
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

sealed record AnalyzeResult(bool Ok, string? Txt, string? Error, string? Notice);

sealed record OtoWritePart(OtoExport Config, IReadOnlyList<OtoAdjustedEntry> Adjustments);

sealed record FrqResult(bool Ok, int Written, string? Error, string? Notice);
