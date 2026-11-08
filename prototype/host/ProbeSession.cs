using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Otoasobi;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Otoasobi.Probe;

sealed class ProbeSession : IDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly MainWindow _window;
    readonly WebView2 _view;
    readonly Action<string> _showMissing;
    readonly DispatcherQueue _queue;
    readonly RecCapture _capture = new();
    readonly PreviewPlayer _player;
    readonly PreviewPlayer _take;
    readonly DispatcherQueueTimer _timer;
    ProbePreview? _python;
    AnalysisClient? _analysis;
    readonly ProbeOto _oto = new();
    readonly ProbeFrq _frq = new();
    readonly ProbeRec _rec = new();
    readonly ProbeBank _bank = new();
    readonly ProbeEffects _effects = new();
    readonly ProbeUpdate _update = new();
    bool _applying;
    readonly DispatcherQueueTimer _otoSave;
    int _analyzeGen;
    string? _folder;
    string? _wavPath;
    string? _inputId;
    string? _outputId;
    bool _monitor;
    bool _watch;
    bool _recording;
    bool _ready;
    bool _disposed;
    int _previewGen;
    int _countGen;
    bool _counting;
    string? _recordStem;
    DateTime _recordStarted;
    bool _takePlaying;
    double _takeDuration;

    public ProbeSession(MainWindow window, WebView2 view, Action<string> showMissing)
    {
        _window = window;
        _view = view;
        _showMissing = showMissing;
        _queue = DispatcherQueue.GetForCurrentThread();
        _player = new PreviewPlayer(_queue);
        _player.Failed += (_, message) => Post(new { type = "failed", message });
        _player.Ended += (_, _) => Post(new { type = "guide", playing = false });
        _take = new PreviewPlayer(_queue);
        _take.Failed += (_, message) => Post(new { type = "failed", message });
        _take.Ended += (_, _) =>
        {
            _takePlaying = false;
            Post(new { type = "take", playing = false, position = 0, duration = _takeDuration });
        };
        _timer = _queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => PostCapture();
        _otoSave = _queue.CreateTimer();
        _otoSave.Interval = TimeSpan.FromMilliseconds(400);
        _otoSave.IsRepeating = false;
        _otoSave.Tick += (_, _) => _oto.FlushSession();
    }

    public async Task AttachAsync()
    {
        try
        {
            var folder = Path.Combine(AppPaths.DataRoot, "probe-webview");
            Directory.CreateDirectory(folder);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, null);
            await _view.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            var missing = ex.GetType().Name.Contains("WebView2RuntimeNotFound", StringComparison.Ordinal)
                || ex is DllNotFoundException or FileNotFoundException;
            _showMissing(missing
                ? "WebView2 ランタイムが見つかりません。Windows に WebView2 ランタイムを入れてから、もう一度開いてください。"
                : "画面を開けませんでした。" + ex.Message);
            return;
        }

        if (_disposed || _view.CoreWebView2 == null)
            return;

        _view.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _view.CoreWebView2.WebMessageReceived += OnMessage;
        var url = Environment.GetEnvironmentVariable("OTOASOBI_PROBE_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            _view.Source = new Uri(url);
            return;
        }

        var ui = Path.Combine(AppContext.BaseDirectory, "ui");
        _view.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "otoasobi.assets",
            ui,
            CoreWebView2HostResourceAccessKind.Deny);
        _view.Source = new Uri("https://otoasobi.assets/index.html");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        if (_recording)
            WriteTake();
        _capture.Dispose();
        _player.Dispose();
        _take.Dispose();
        _otoSave.Stop();
        _oto.FlushSession();
        _python?.Dispose();
        _analysis?.Dispose();
    }

    void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(args.WebMessageAsJson);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            switch (type)
            {
                case "hello":
                    _ = HelloAsync();
                    break;
                case "openWav":
                    _ = OpenWavAsync();
                    break;
                case "openFolder":
                    _ = OpenFolderAsync();
                    break;
                case "selectFile":
                    SelectFile(Text(root, "name"));
                    break;
                case "preview":
                    _ = PreviewAsync(
                        Number(root, "t0"),
                        Number(root, "t1"),
                        Count(root, "columns", 800),
                        Count(root, "rows", 320));
                    break;
                case "setDevices":
                    _ = SetDevicesAsync(Text(root, "inputId"), Text(root, "outputId"));
                    break;
                case "monitor":
                    _ = MonitorAsync(root.TryGetProperty("on", out var onValue) && onValue.ValueKind == JsonValueKind.True);
                    break;
                case "guide":
                    _ = GuideAsync(Text(root, "note") ?? "C4", (int)Math.Round(Number(root, "semitones")), Number(root, "speed"), Volume(root));
                    break;
                case "guideStop":
                    _player.Stop();
                    Post(new { type = "guide", playing = false });
                    break;
                case "guideFile":
                    HandleGuideFile(root);
                    break;
                case "take":
                    _ = HandleTakeAsync(root);
                    break;
                case "record":
                    _ = BeginRecordAsync(root);
                    break;
                case "recordStop":
                    StopRecord();
                    break;
                case "recSettings":
                    SaveRecSettings(root);
                    break;
                case "recList":
                    HandleRec(root);
                    break;
                case "frq":
                    HandleFrq(root);
                    break;
                case "bank":
                    HandleBank(root);
                    break;
                case "bankPick":
                    _ = PickBankAsync(Text(root, "which"));
                    break;
                case "effects":
                    HandleEffects(root);
                    break;
                case "watchMic":
                    _ = WatchMicAsync(root.TryGetProperty("on", out var watchValue) && watchValue.ValueKind == JsonValueKind.True);
                    break;
                case "close":
                    _window.Close();
                    break;
                case "caption":
                    _window.PaintCaption(Text(root, "theme") == "dark");
                    break;
                case "layout":
                    SaveLayout(root);
                    break;
                case "openRecent":
                    _ = OpenRecentAsync(Text(root, "path"));
                    break;
                case "template":
                    ApplyTemplate(Text(root, "op"), Text(root, "name"));
                    break;
                case "appearance":
                    SaveAppearance(root);
                    break;
                case "update":
                    _ = HandleUpdateAsync(Text(root, "op"));
                    break;
                case "oto":
                    HandleOto(root);
                    break;
                case "saveSettings":
                    SaveSettings(Text(root, "name"), root.TryGetProperty("replace", out var replaceValue) && replaceValue.ValueKind == JsonValueKind.True);
                    break;
                case "loadSettings":
                    LoadSettings(Text(root, "name"));
                    break;
            }
        }
    }

    async Task HelloAsync()
    {
        if (_disposed)
            return;
        _ready = true;
        PostLayout();
        try
        {
            var inputs = await RecCapture.DevicesAsync();
            var outputs = await RecCapture.PlaybackDevicesAsync();
            Post(new
            {
                type = "devices",
                inputs = inputs.Select(device => new { id = device.Id, label = device.Label }).ToArray(),
                outputs = outputs.Select(device => new { id = device.Id, label = device.Label }).ToArray(),
            });
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
        }

        PostCapture();
        PostRecSettings();
        Post(_effects.Snapshot());
        PostRecent();
        PostTemplates();
        PostAppearance();
    }

    async Task OpenWavAsync()
    {
        if (_disposed)
            return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        picker.FileTypeFilter.Add(".wav");
        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (file == null || _disposed)
            return;
        _folder = Path.GetDirectoryName(file.Path);
        _wavPath = file.Path;
        _previewGen++;
        PostFiles();
        Post(new { type = "opened", name = file.Name });
        if (!string.IsNullOrEmpty(_folder))
        {
            _oto.Open(_folder);
            _rec.OpenFolder(_folder);
            OpenVoice(_folder);
            PostOto();
            PostRec();
            PostFrqCount();
            Post(_frq.View(_wavPath));
            BeginAnalyze(false);
            RecentFolders.Remember(_folder);
            PostRecent();
        }
    }

    async Task OpenFolderAsync()
    {
        if (_disposed)
            return;
        string? folder;
        try
        {
            folder = await WavPicker.PickAsync(WindowNative.GetWindowHandle(_window));
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (string.IsNullOrEmpty(folder) || _disposed)
            return;
        try
        {
            AdoptFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
        }
    }

    async Task OpenRecentAsync(string? path)
    {
        if (_disposed)
            return;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            if (!string.IsNullOrWhiteSpace(path))
                RecentFolders.Forget(path);
            PostRecent();
            Post(new { type = "failed", message = "フォルダを開けませんでした。" });
            return;
        }

        try
        {
            AdoptFolder(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
        }

        await Task.CompletedTask;
    }

    void AdoptFolder(string folder)
    {
        _folder = folder;
        if (!string.IsNullOrEmpty(_wavPath))
            {
                var current = Path.GetFileName(_wavPath);
                var still = WavNames(folder).Any(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase));
                if (!still)
                {
                    _wavPath = null;
                    _previewGen++;
                }
            }

            Post(new { type = "files", folder = _folder, names = WavNames(_folder) });
            _oto.Open(folder);
            _rec.OpenFolder(folder);
            OpenVoice(folder);
            PostOto();
            PostRec();
            PostFrqCount();
            BeginAnalyze(false);
            RecentFolders.Remember(folder);
            PostRecent();
    }

    void SelectFile(string? name)
    {
        if (_disposed)
            return;
        if (string.IsNullOrEmpty(_folder))
        {
            Post(new { type = "failed", message = "フォルダを開いてください。" });
            return;
        }

        if (!IsPlainFileName(name))
        {
            Post(new { type = "failed", message = "ファイル名だけを指定してください。" });
            return;
        }

        List<string> names;
        try
        {
            names = WavNames(_folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        var match = names.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            if (name!.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                _wavPath = null;
                _previewGen++;
                _frq.Clear();
                Post(new { type = "opened", name, missing = true });
                PostOto();
                Post(_frq.View(null));
                return;
            }

            Post(new { type = "failed", message = "このファイルは存在しません。" });
            return;
        }

        _wavPath = Path.Combine(_folder, match);
        _previewGen++;
        Post(new { type = "opened", name = match });
        PostOto();
        Post(_frq.View(_wavPath));
    }

    void PostFiles()
    {
        if (string.IsNullOrEmpty(_folder))
            return;
        try
        {
            Post(new { type = "files", folder = _folder, names = WavNames(_folder) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
        }
    }

    static List<string> WavNames(string folder)
    {
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileName(path));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            return false;
        return name.IndexOfAny(['\\', '/', ':']) < 0;
    }

    async Task PreviewAsync(double t0, double t1, int columns, int rows)
    {
        if (_disposed)
            return;
        if (string.IsNullOrEmpty(_wavPath))
        {
            Post(new { type = "failed", message = "WAV を開いてください。" });
            return;
        }

        var generation = ++_previewGen;
        try
        {
            _python ??= new ProbePreview();
            var picture = await _python.LoadAsync(_wavPath, t0, t1, columns, rows);
            if (_disposed || generation != _previewGen)
                return;
            if (!picture.Ok)
            {
                Post(new { type = "failed", message = picture.Error ?? "プレビューを作れませんでした。" });
                return;
            }

            Post(new
            {
                type = "preview",
                duration = picture.Duration,
                lo = picture.Lo,
                hi = picture.Hi,
                png = picture.Png,
                specT0 = picture.SpecT0,
                specT1 = picture.SpecT1,
                waveT0 = picture.WaveT0,
                waveT1 = picture.WaveT1,
                sampleRate = picture.SampleRate,
                loudness = picture.Loudness,
            });
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or JsonException)
        {
            if (!_disposed && generation == _previewGen)
                Post(new { type = "failed", message = ex.Message });
        }
    }

    async Task SetDevicesAsync(string? inputId, string? outputId)
    {
        if (_disposed)
            return;
        if (_recording)
            StopRecord();
        _inputId = string.IsNullOrWhiteSpace(inputId) ? null : inputId;
        _outputId = string.IsNullOrWhiteSpace(outputId) ? null : outputId;
        _player.SetOutput(_outputId);
        try
        {
            await _player.ApplyOutputAsync();
        }
        catch (Exception)
        {
            Post(new { type = "failed", message = "出力デバイスを開けませんでした。" });
        }

        if (_watch)
            await RestartCaptureAsync();
        PostCapture();
    }

    async Task MonitorAsync(bool on)
    {
        if (_disposed)
            return;
        _monitor = on;
        if (!_watch)
            return;
        if (!_capture.Running)
            await RestartCaptureAsync();
        try
        {
            await _capture.ApplyListenAsync(on, _outputId);
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
        }

        PostCapture();
    }

    void HandleGuideFile(JsonElement root)
    {
        if (root.TryGetProperty("pick", out var pick) && pick.ValueKind == JsonValueKind.True)
        {
            _ = PickGuideAsync();
            return;
        }

        if (root.TryGetProperty("manual", out var manual))
        {
            _rec.SetManual(manual.ValueKind == JsonValueKind.True);
            PostGuide();
            return;
        }

        var path = Text(root, "path");
        if (string.IsNullOrEmpty(path))
        {
            _rec.ClearGuide();
            PostGuide();
            return;
        }

        var error = _rec.UseGuide(path);
        if (error != null && error.StartsWith("missing-text:", StringComparison.Ordinal))
            Post(new { type = "notice", message = "同じ名前の txt がありません。" + error["missing-text:".Length..] });
        else if (error != null)
            Post(new { type = "failed", message = error });
        PostGuide();
    }

    async Task PickGuideAsync()
    {
        if (_disposed)
            return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        picker.FileTypeFilter.Add(".wav");
        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (file == null || _disposed)
            return;
        var error = _rec.UseGuide(file.Path);
        if (error != null && error.StartsWith("missing-text:", StringComparison.Ordinal))
            Post(new { type = "notice", message = "同じ名前の txt がありません。" + error["missing-text:".Length..] });
        else if (error != null)
            Post(new { type = "failed", message = error });
        PostGuide();
    }

    void PostGuide() => Post(_rec.GuideSnapshot());

    async Task GuideAsync(string note, int semitones, double speed, double volume)
    {
        if (_disposed)
            return;
        if (!_rec.Manual)
        {
            await PlayGuideFileAsync(semitones, speed, volume);
            return;
        }

        if (!RecTone.TryHertz(note, out var hz))
        {
            Post(new { type = "failed", message = "音名を読めませんでした。" });
            return;
        }

        var tone = RecTone.Tone(hz, RecTone.TargetRate, 2);
        var shaped = RecTone.ShapeGuide(tone, 1, RecTone.TargetRate, speed, semitones);
        var wav = PreviewWav.Encode(shaped, 1, RecTone.TargetRate);
        _player.Volume = volume;
        var played = await _player.PlayAsync(wav, loop: true);
        Post(new { type = "guide", playing = played });
    }

    async Task PlayGuideFileAsync(int semitones, double speed, double volume)
    {
        var path = _rec.GuidePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Post(new { type = "failed", message = "ガイドのファイルを選んでください。WAV を選ぶと、同じ名前の txt が対になります。" });
            Post(new { type = "guide", playing = false });
            return;
        }

        try
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024)
            {
                Post(new { type = "failed", message = "ガイドが大きすぎます。" });
                return;
            }

            byte[] wav;
            if (Math.Abs(speed - 1) < 0.001 && semitones == 0)
                wav = await File.ReadAllBytesAsync(path);
            else
            {
                var audio = await Task.Run(() => WavAudio.Read(path));
                var shaped = await Task.Run(() => RecTone.ShapeGuide(audio.Samples, audio.Channels, audio.SampleRate, speed == 0 ? 1 : speed, semitones));
                wav = PreviewWav.Encode(shaped, audio.Channels, audio.SampleRate);
            }

            _player.Volume = volume;
            var played = await _player.PlayAsync(wav, loop: false);
            Post(new { type = "guide", playing = played });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Post(new { type = "failed", message = ex.Message });
            Post(new { type = "guide", playing = false });
        }
    }

    async Task HandleTakeAsync(JsonElement root)
    {
        var op = Text(root, "op");
        if (op == "stop")
        {
            _takePlaying = false;
            _take.Stop();
            Post(new { type = "take", playing = false, position = 0, duration = _takeDuration });
            return;
        }

        if (op == "wave")
        {
            PostTakeWave(Text(root, "name"));
            return;
        }

        if (string.IsNullOrEmpty(_folder))
        {
            Post(new { type = "failed", message = "フォルダを開いてください。" });
            return;
        }

        var name = Path.GetFileName(Text(root, "name") ?? "");
        var path = Path.Combine(_folder, name);
        if (name.Length == 0 || !File.Exists(path))
        {
            Post(new { type = "failed", message = "このファイルは存在しません。" });
            return;
        }

        try
        {
            var audio = await Task.Run(() => WavAudio.Read(path));
            var frames = audio.Channels <= 0 ? 0 : audio.Samples.Length / audio.Channels;
            _takeDuration = audio.SampleRate <= 0 ? 0 : frames / (double)audio.SampleRate;
            var bytes = await File.ReadAllBytesAsync(path);
            _take.Volume = 1;
            _takePlaying = true;
            var played = await _take.PlayAsync(bytes, loop: false);
            _takePlaying = played;
            Post(new { type = "take", playing = played, position = 0, duration = _takeDuration });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _takePlaying = false;
            Post(new { type = "failed", message = ex.Message });
        }
    }

    void PostTakeWave(string? name)
    {
        if (string.IsNullOrEmpty(_folder) || string.IsNullOrEmpty(name))
        {
            Post(new { type = "takeWave", name = name ?? "", lo = Array.Empty<double>(), hi = Array.Empty<double>(), duration = 0 });
            return;
        }

        var file = Path.GetFileName(name);
        var path = Path.Combine(_folder, file);
        if (!File.Exists(path))
        {
            Post(new { type = "takeWave", name = file, lo = Array.Empty<double>(), hi = Array.Empty<double>(), duration = 0 });
            return;
        }

        try
        {
            var audio = WavAudio.Read(path);
            var frames = audio.Channels <= 0 ? 0 : audio.Samples.Length / audio.Channels;
            var mono = new float[frames];
            if (audio.Channels <= 1)
                mono = audio.Samples;
            else
            {
                for (var i = 0; i < frames; i++)
                {
                    double sum = 0;
                    for (var channel = 0; channel < audio.Channels; channel++)
                        sum += audio.Samples[i * audio.Channels + channel];
                    mono[i] = (float)(sum / audio.Channels);
                }
            }

            var (lo, hi) = ProbePeaks.Columns(mono, 240);
            var duration = audio.SampleRate <= 0 ? 0 : frames / (double)audio.SampleRate;
            Post(new { type = "takeWave", name = file, lo, hi, duration });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Post(new { type = "failed", message = ex.Message });
        }
    }

    async Task WatchMicAsync(bool on)
    {
        if (_disposed)
            return;
        _watch = on;
        if (!on)
        {
            if (_recording)
                StopRecord();
            _timer.Stop();
            _capture.Stop();
            PostCapture();
            return;
        }

        if (!_timer.IsRunning)
            _timer.Start();
        await RestartCaptureAsync();
        PostCapture();
    }

    async Task RestartCaptureAsync()
    {
        try
        {
            await _capture.StartAsync(_inputId, _outputId);
            if (_monitor)
                await _capture.ApplyListenAsync(true, _outputId);
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
        }
    }

    async Task BeginRecordAsync(JsonElement root)
    {
        if (_disposed || _recording || _counting)
            return;
        if (!_capture.Running)
        {
            Post(new { type = "failed", message = "マイクが開いていません。" });
            return;
        }

        var name = Text(root, "name");
        _recordStem = null;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var error = RecNames.StemError(name) ?? RecNames.WavError(name + ".wav");
            if (error != null)
            {
                Post(new { type = "failed", message = error });
                return;
            }

            if (string.IsNullOrEmpty(_folder))
            {
                Post(new { type = "failed", message = "フォルダを開いてください。" });
                return;
            }

            _recordStem = name.Trim();
        }

        var manualGuide = _rec.Manual;
        var countIn = manualGuide && root.TryGetProperty("countIn", out var countValue) && countValue.ValueKind == JsonValueKind.True;
        var bpm = (int)Math.Clamp(Math.Round(Number(root, "bpm") == 0 ? 120 : Number(root, "bpm")), 40, 240);
        if (countIn)
        {
            var token = ++_countGen;
            _counting = true;
            Post(new { type = "recordWait", waiting = true });
            PostStatus("カウントしています");
            var clicks = PreviewWav.Encode(RecTone.CountIn(bpm, RecTone.TargetRate), 1, RecTone.TargetRate);
            _player.Volume = Volume(root);
            var played = await _player.PlayAsync(clicks, loop: false);
            var seconds = 4 * 60.0 / bpm;
            var started = DateTime.UtcNow;
            while (played && token == _countGen && !_disposed && (DateTime.UtcNow - started).TotalSeconds < seconds)
                await Task.Delay(40);
            var cancelled = token != _countGen || _disposed || !played;
            if (token == _countGen)
                _counting = false;
            Post(new { type = "recordWait", waiting = false });
            PostStatus("");
            if (cancelled || !_capture.Running)
                return;
        }

        if (_disposed || _recording)
            return;
        _capture.BeginTake();
        _recording = true;
        _recordStarted = DateTime.UtcNow;
        PostCapture();
        if (manualGuide && root.TryGetProperty("guideWhile", out var guideValue) && guideValue.ValueKind == JsonValueKind.True)
        {
            var note = Text(root, "note") ?? "C4";
            _ = GuideAsync(note, (int)Math.Round(Number(root, "semitones")), Number(root, "speed") == 0 ? 1 : Number(root, "speed"), Volume(root));
        }
    }

    void StopRecord()
    {
        if (_counting)
        {
            _countGen++;
            _counting = false;
            _player.Stop();
            Post(new { type = "recordWait", waiting = false });
            PostStatus("");
            return;
        }

        if (_disposed || !_recording)
            return;
        _player.Stop();
        Post(new { type = "guide", playing = false });
        var saved = WriteTake();
        PostCapture();
        if (saved == null)
            return;
        PostFiles();
        PostRec();
        Post(saved);
    }

    object? WriteTake()
    {
        if (!_recording)
            return null;
        var elapsed = Math.Max(0, (DateTime.UtcNow - _recordStarted).TotalSeconds);
        _recording = false;
        var (samples, channels, rate) = _capture.EndTake();
        var mono = RecTone.ToMono44100(samples, channels, rate);
        if (mono.Length == 0)
        {
            Post(new { type = "failed", message = "音が入りませんでした。" });
            return null;
        }

        try
        {
            string path;
            if (string.IsNullOrEmpty(_recordStem) || string.IsNullOrEmpty(_folder))
            {
                var directory = Path.Combine(AppPaths.DataRoot, "probe-takes");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "take-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".wav");
            }
            else
            {
                path = Path.Combine(_folder, RecNames.WavName(_recordStem));
            }
            WavAudio.Write(path, new WavAudio
            {
                SampleRate = RecTone.TargetRate,
                Channels = 1,
                BitsPerSample = 16,
                Samples = mono,
            });
            var read = WavAudio.Read(path);
            var seconds = read.Channels > 0 && read.SampleRate > 0
                ? read.Samples.Length / (double)read.Channels / read.SampleRate
                : 0;
            return new
            {
                type = "saved",
                path,
                inputRate = rate,
                headerRate = read.SampleRate,
                bits = read.BitsPerSample,
                channels = read.Channels,
                seconds,
                elapsed,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Post(new { type = "failed", message = ex.Message });
            return null;
        }
    }

    void HandleOto(JsonElement root)
    {
        var op = Text(root, "op");
        switch (op)
        {
            case "mode":
                _oto.SetMode(Text(root, "mode"));
                RememberOto();
                break;
            case "slider":
                if (_oto.SetSlider(Text(root, "group"), Text(root, "key"), Number(root, "value")))
                    RememberOto();
                break;
            case "preset":
                _oto.SelectPreset(Text(root, "target"), Text(root, "name"));
                RememberOto();
                break;
            case "savePreset":
                var presetError = _oto.SavePreset(Text(root, "which"), Text(root, "name"));
                if (presetError != null)
                    Post(new { type = "failed", message = presetError });
                RememberOto();
                break;
            case "alias":
                _oto.SetAlias(Text(root, "which"), root.TryGetProperty("on", out var aliasOn) && aliasOn.ValueKind == JsonValueKind.True);
                RememberOto();
                break;
            case "scope":
                _oto.SetScope(Text(root, "scope"));
                RememberOto();
                break;
            case "analyze":
                BeginAnalyze(root.TryGetProperty("force", out var forceValue) && forceValue.ValueKind == JsonValueKind.True);
                break;
            case "analyzeFile":
                BeginAnalyzeFile();
                break;
            case "write":
                _ = WriteOtoAsync();
                break;
            case "tune":
                if (_oto.TunePoint(Text(root, "file"), (int)Math.Round(Number(root, "index")), (int)Math.Round(Number(root, "point")), Number(root, "time")))
                    RememberOto();
                break;
            case "spans":
                if (_oto.MoveSpans(Text(root, "file"), ReadSpanTimes(Property(root, "spans"))))
                {
                    PostOto();
                    _ = SaveSpanCacheAsync();
                }
                break;
            case "fields":
                if (_oto.TuneFields(Text(root, "file"), (int)Math.Round(Number(root, "index")), Text(root, "alias"), Number(root, "offset"), Number(root, "overlap"), Number(root, "preutter"), Number(root, "consonant"), Number(root, "blank"), Text(root, "mode")))
                    RememberOto();
                break;
            case "aliasName":
                if (_oto.TuneAlias(Text(root, "file"), (int)Math.Round(Number(root, "index")), Text(root, "alias"), Text(root, "name"), Text(root, "mode")))
                    RememberOto();
                break;
            case "comment":
                if (_oto.TuneComment(Text(root, "file"), (int)Math.Round(Number(root, "index")), Text(root, "alias"), Text(root, "comment"), Text(root, "mode")))
                    RememberOto();
                break;
        }
    }

    async Task SaveSpanCacheAsync()
    {
        if (_disposed || string.IsNullOrEmpty(_folder) || _oto.Files.Count == 0)
            return;
        try
        {
            _analysis ??= new AnalysisClient();
            await _analysis.SaveCacheAsync(_folder, _oto.Files);
        }
        catch (Exception ex)
        {
            if (!_disposed)
                Post(new { type = "failed", message = ex.Message });
        }
    }

    static List<(double Start, double End)> ReadSpanTimes(JsonElement value)
    {
        var times = new List<(double Start, double End)>();
        if (value.ValueKind != JsonValueKind.Array)
            return times;
        foreach (var item in value.EnumerateArray())
        {
            if (!item.TryGetProperty("start", out var startValue) || !item.TryGetProperty("end", out var endValue))
                return [];
            if (!startValue.TryGetDouble(out var start) || !endValue.TryGetDouble(out var end))
                return [];
            times.Add((start, end));
        }

        return times;
    }

    void RememberOto()
    {
        PostOto();
        _otoSave.Stop();
        _otoSave.Start();
    }

    void SaveSettings(string? name, bool replace)
    {
        var error = _oto.SaveNamed(name, replace);
        if (error == "exists")
        {
            Post(new { type = "otoExists", name });
            return;
        }

        if (error != null)
            Post(new { type = "failed", message = error });
        else
            Post(new { type = "notice", message = "設定を保存しました。" });
        PostOto();
    }

    void LoadSettings(string? name)
    {
        var error = _oto.LoadNamed(name);
        if (error != null)
            Post(new { type = "failed", message = error });
        else
            Post(new { type = "notice", message = "設定を読み込みました。" });
        PostOto();
    }

    void BeginAnalyze(bool force)
    {
        if (string.IsNullOrEmpty(_folder) || _disposed)
            return;
        var gen = ++_analyzeGen;
        _ = AnalyzeAsync(gen, force);
    }

    void BeginAnalyzeFile()
    {
        if (_disposed || string.IsNullOrEmpty(_folder) || string.IsNullOrEmpty(_wavPath))
        {
            Post(new { type = "failed", message = "ファイルを選んでください。" });
            return;
        }

        if (_oto.IsBusy)
        {
            Post(new { type = "failed", message = "解析が終わるまで待ってください。" });
            return;
        }

        if (_oto.IsBusy)
            return;
        var gen = ++_analyzeGen;
        _ = AnalyzeFileAsync(gen, Path.GetFileName(_wavPath));
    }

    async Task AnalyzeAsync(int gen, bool force)
    {
        _oto.SetBusy(true);
        PostOto();
        PostStatus("解析しています");
        try
        {
            _analysis ??= new AnalysisClient();
            var data = await _analysis.AnalyzeFolderAsync(
                _folder!,
                (done, total) => PostStatus($"解析しています　{done} / {total} ファイル"),
                force,
                (name, _, label) => PostStatus(string.IsNullOrEmpty(label) ? name : $"{name}　{label}"));
            if (gen != _analyzeGen || _disposed)
                return;
            if (!data.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(data.Error) ? "解析できませんでした。" : data.Error });
            else
                _oto.SetFiles(data.Files);
        }
        catch (Exception ex)
        {
            if (gen == _analyzeGen && !_disposed)
                Post(new { type = "failed", message = ex.Message });
        }
        finally
        {
            if (gen == _analyzeGen && !_disposed)
            {
                _oto.SetBusy(false);
                PostStatus("");
                PostOto();
            }
        }
    }

    async Task AnalyzeFileAsync(int gen, string name)
    {
        _oto.SetBusy(true);
        PostOto();
        PostStatus($"{name} を解析しています");
        try
        {
            _analysis ??= new AnalysisClient();
            var data = await _analysis.AnalyzeFileAsync(
                _folder!,
                name,
                (done, total) => PostStatus($"{name}　{done} / {total}"),
                (_, _, label) => PostStatus(string.IsNullOrEmpty(label) ? name : $"{name}　{label}"));
            if (gen != _analyzeGen || _disposed)
                return;
            if (!data.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(data.Error) ? "解析できませんでした。" : data.Error });
            else if (data.Files.Count == 0)
                Post(new { type = "failed", message = "解析できませんでした。" });
            else
                _oto.MergeFile(data.Files[0]);
        }
        catch (Exception ex)
        {
            if (gen == _analyzeGen && !_disposed)
                Post(new { type = "failed", message = ex.Message });
        }
        finally
        {
            if (gen == _analyzeGen && !_disposed)
            {
                _oto.SetBusy(false);
                PostStatus("");
                PostOto();
            }
        }
    }

    async Task WriteOtoAsync()
    {
        if (_disposed || string.IsNullOrEmpty(_folder) || _oto.Files.Count == 0)
        {
            Post(new { type = "failed", message = "解析が終わってから生成できます。" });
            return;
        }

        var modes = _oto.AliasModes();
        if (modes.Count == 0)
        {
            Post(new { type = "failed", message = "単独音、連続音、CVVC のどれかを選んでください。" });
            return;
        }

        _oto.SetBusy(true);
        PostOto();
        try
        {
            _analysis ??= new AnalysisClient();
            var parts = modes.Select(mode => new OtoWritePart(_oto.ExportFor(mode), _oto.Adjustments(mode))).ToList();
            var result = await _analysis.WriteAsync(_folder, _oto.Files, parts, _oto.ScopeForWrite());
            if (_disposed)
                return;
            if (!result.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(result.Error) ? "oto.iniを生成できませんでした。" : result.Error });
            else
                Post(new { type = "notice", message = string.IsNullOrWhiteSpace(result.Notice) ? "oto.iniを生成しました。" : "oto.iniを生成しました。 " + result.Notice });
        }
        catch (Exception ex)
        {
            if (!_disposed)
                Post(new { type = "failed", message = ex.Message });
        }
        finally
        {
            if (!_disposed)
            {
                _oto.SetBusy(false);
                PostOto();
            }
        }
    }

    void HandleFrq(JsonElement root)
    {
        switch (Text(root, "op"))
        {
            case "write":
                _ = WriteFrqAsync(Text(root, "mode"), root.TryGetProperty("clear", out var clear) && clear.ValueKind == JsonValueKind.True);
                break;
            case "stroke":
                PostFrqEdit(_frq.Stroke(Property(root, "points")));
                break;
            case "octave":
                PostFrqEdit(_frq.Shift(Property(root, "frames"), (int)Math.Round(Number(root, "octaves"))));
                break;
            case "clear":
                PostFrqEdit(_frq.ClearPitch(Property(root, "frames")));
                break;
        }
    }

    async Task WriteFrqAsync(string? mode, bool clear)
    {
        if (_disposed || string.IsNullOrEmpty(_folder))
            return;
        if (_oto.IsBusy)
        {
            Post(new { type = "failed", message = "解析が終わるまで待ってください。" });
            return;
        }

        mode = mode is "missing" or "selected" ? mode : "all";
        string? only = null;
        if (mode == "selected")
        {
            if (string.IsNullOrEmpty(_wavPath))
            {
                Post(new { type = "failed", message = "ファイルを選んでください。" });
                return;
            }

            only = Path.GetFileName(_wavPath);
        }

        _oto.SetBusy(true);
        PostOto();
        PostStatus(".frqを計算しています");
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteFrqAsync(
                _folder,
                mode,
                only,
                clear ? _oto.PreutterClears() : null,
                (done, total) => PostStatus($".frqを計算しています　{done} / {total} ファイル"),
                (name, _, label) => PostStatus(string.IsNullOrEmpty(label) ? name : $"{name}　{label}"));
            if (_disposed)
                return;
            if (!result.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(result.Error) ? ".frqを生成できませんでした。" : result.Error });
            else
            {
                var notice = $"{result.Written} 件の .frq を書きました。";
                if (!string.IsNullOrWhiteSpace(result.Notice))
                    notice += " " + result.Notice;
                Post(new { type = "notice", message = notice });
            }

            if (!string.IsNullOrEmpty(_wavPath))
                Post(_frq.View(_wavPath));
        }
        catch (Exception ex)
        {
            if (!_disposed)
                Post(new { type = "failed", message = ex.Message });
        }
        finally
        {
            if (!_disposed)
            {
                _oto.SetBusy(false);
                PostStatus("");
                PostOto();
                PostFrqCount();
            }
        }
    }

    void PostFrqEdit(object? view)
    {
        if (view == null)
            Post(new { type = "failed", message = "このファイルの .frq はありません。" });
        else
            Post(view);
    }

    void HandleRec(JsonElement root)
    {
        if (string.IsNullOrEmpty(_folder))
        {
            Post(new { type = "failed", message = "フォルダを開いてください。" });
            return;
        }

        switch (Text(root, "op"))
        {
            case "none":
                _rec.ClearChoice();
                PostRec();
                break;
            case "open":
                var error = _rec.Load(Text(root, "path"), remember: true);
                if (error != null)
                    Post(new { type = "failed", message = error });
                PostRec();
                break;
            case "pick":
                _ = PickRecAsync();
                break;
        }
    }

    async Task PickRecAsync()
    {
        if (_disposed)
            return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        picker.FileTypeFilter.Add(".txt");
        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (file == null || _disposed)
            return;
        var error = _rec.Load(file.Path, remember: true);
        if (error != null)
            Post(new { type = "failed", message = error });
        PostRec();
    }

    void SaveRecSettings(JsonElement root)
    {
        var settings = RecAppSettings.Load();
        var note = Text(root, "note");
        if (!string.IsNullOrEmpty(note) && RecTone.Notes.Contains(note))
            settings.Note = note;
        settings.GuidePitch = (int)Math.Clamp(Math.Round(Number(root, "semitones")), -12, 12);
        var speed = Number(root, "speed");
        settings.GuideSpeed = speed == 0 ? settings.GuideSpeed : Math.Clamp(speed, 0.5, 2);
        if (root.TryGetProperty("volume", out _))
            settings.GuideVolume = Volume(root);
        settings.CountIn = root.TryGetProperty("countIn", out var count) && count.ValueKind == JsonValueKind.True;
        var bpm = Number(root, "bpm");
        if (bpm > 0)
            settings.Bpm = (int)Math.Clamp(Math.Round(bpm), 40, 240);
        settings.GuideWhileRecording = root.TryGetProperty("guideWhile", out var guide) && guide.ValueKind == JsonValueKind.True;
        settings.AutoAdvance = root.TryGetProperty("advance", out var advance) && advance.ValueKind == JsonValueKind.True;
        settings.Save();
    }

    void PostRecSettings()
    {
        var settings = RecAppSettings.Load();
        Post(new
        {
            type = "recSettings",
            note = settings.Note,
            semitones = settings.GuidePitch,
            speed = settings.GuideSpeed,
            volume = settings.GuideVolume,
            countIn = settings.CountIn,
            bpm = settings.Bpm,
            guideWhile = settings.GuideWhileRecording,
            advance = settings.AutoAdvance,
        });
    }

    void PostRec()
    {
        if (string.IsNullOrEmpty(_folder))
            return;
        IReadOnlyList<string> names;
        try
        {
            names = WavNames(_folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            names = [];
        }

        Post(_rec.Snapshot(names));
        PostGuide();
    }

    void PostFrqCount()
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
        {
            Post(new { type = "frq", wavs = 0, frqs = 0, busy = false });
            return;
        }

        var counted = ProbeFrq.Count(_folder);
        Post(new { type = "frq", wavs = counted.Wavs, frqs = counted.Frqs, busy = false });
    }

    void PostStatus(string message) => Post(new { type = "status", message });

    void OpenVoice(string folder)
    {
        _bank.Open(folder);
        _effects.SetFolder(folder);
        _ = PostBankAsync();
        Post(_effects.Snapshot());
    }

    void HandleBank(JsonElement root)
    {
        var error = _bank.Save(root, (int)Math.Round(Number(root, "generation")));
        if (error != null)
            Post(new { type = "failed", message = error });
    }

    async Task PickBankAsync(string? which)
    {
        if (_disposed)
            return;
        if (string.IsNullOrEmpty(_folder))
        {
            Post(new { type = "failed", message = "フォルダを開いてください。" });
            return;
        }

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico" })
            picker.FileTypeFilter.Add(ext);
        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (file == null || _disposed || string.IsNullOrEmpty(_folder))
            return;
        var generation = _bank.Generation;
        string? error;
        try
        {
            error = which == "portrait"
                ? await _bank.SavePortraitAsync(file.Path)
                : await _bank.SaveIconAsync(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            error = ex.Message;
        }

        if (error != null)
            Post(new { type = "failed", message = error });
        else
            Post(new { type = "notice", message = which == "portrait" ? "立ち絵を設定しました。" : "100×100 の icon.bmp にしました。" });
        if (generation == _bank.Generation)
            await PostBankAsync();
    }

    async Task PostBankAsync()
    {
        var generation = _bank.Generation;
        object snapshot;
        try
        {
            snapshot = await _bank.SnapshotAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (!_disposed && generation == _bank.Generation)
            Post(snapshot);
    }

    void HandleEffects(JsonElement root)
    {
        switch (Text(root, "op"))
        {
            case "set":
                _effects.Set(root);
                break;
            case "preset":
                _effects.ApplyPreset(Text(root, "name"));
                Post(_effects.Snapshot());
                break;
            case "noise":
                var error = _effects.SetNoise(Text(root, "file"), Number(root, "start"), Number(root, "end"));
                if (error != null)
                    Post(new { type = "failed", message = error });
                Post(_effects.Snapshot());
                break;
            case "apply":
                _ = ApplyEffectsAsync();
                break;
            case "move":
                _effects.Move((int)Math.Round(Number(root, "from")), (int)Math.Round(Number(root, "to")));
                Post(_effects.Snapshot());
                break;
        }
    }

    async Task ApplyEffectsAsync()
    {
        if (string.IsNullOrEmpty(_folder))
        {
            Post(new { type = "failed", message = "フォルダを開いてください。" });
            return;
        }

        if (_effects.NeedsNoiseRange)
        {
            Post(new { type = "failed", message = "ノイズサプレッサーがオンです。原音から無音の範囲を選んでから、適用してください。" });
            return;
        }

        if (_applying || _oto.IsBusy)
        {
            Post(new { type = "failed", message = "解析が終わるまで待ってください。" });
            return;
        }

        var folder = _folder;
        _applying = true;
        PostStatus("エフェクトを適用しています");
        try
        {
            await Task.Run(() => OriginalLibrary.Apply(folder, _effects.Chain, (name, index, count) =>
            {
                _queue.TryEnqueue(() =>
                {
                    if (!_disposed && _folder == folder)
                        PostStatus(name + "　" + index + " / " + count);
                });
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _applying = false;
            if (_folder == folder)
            {
                PostStatus("");
                Post(new { type = "failed", message = "適用できませんでした。" + ex.Message });
            }

            return;
        }

        _applying = false;
        if (_disposed || _folder != folder)
            return;
        var cache = Path.Combine(folder, OtoSaveStore.CacheFileName);
        try
        {
            if (File.Exists(cache))
                File.Delete(cache);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        BeginAnalyze(true);
    }

    void PostOto()
    {
        var name = string.IsNullOrEmpty(_wavPath) ? null : Path.GetFileName(_wavPath);
        Post(_oto.Snapshot(name));
    }

    static JsonElement Property(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value : default;

    static double Volume(JsonElement root)
    {
        if (!root.TryGetProperty("volume", out var value) || !value.TryGetDouble(out var parsed))
            return 0.7;
        return Math.Clamp(parsed, 0, 1);
    }

    bool _keepRatios;

    void SaveLayout(JsonElement message)
    {
        if (!message.TryGetProperty("root", out var body))
            return;
        LayoutNode? root = null;
        if (body.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            try
            {
                root = JsonSerializer.Deserialize<LayoutNode>(body.GetRawText(), JsonFiles.Options);
            }
            catch (JsonException)
            {
                return;
            }
        }

        if (root != null && !EditorLayout.IsValid(root))
            return;
        _keepRatios = message.TryGetProperty("keepRatios", out var keep) && keep.ValueKind == JsonValueKind.True;
        EditorLayoutStore.Save(root, _keepRatios);
    }

    void PostLayout()
    {
        if (!EditorLayoutStore.TryLoad(out var root, out var keep))
            return;
        _keepRatios = keep;
        Post(new { type = "layout", root, keepRatios = keep });
    }

    void PostRecent()
    {
        var folders = new List<object>();
        foreach (var path in RecentFolders.Load())
        {
            if (!Directory.Exists(path))
            {
                RecentFolders.Forget(path);
                continue;
            }

            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            folders.Add(new { path, name = name.Length == 0 ? path : name });
        }

        Post(new { type = "recent", folders });
    }

    void PostTemplates()
    {
        var names = LayoutTemplateStore.Load().Select(template => template.Name).ToArray();
        Post(new { type = "templates", names });
    }

    void ApplyTemplate(string? op, string? name)
    {
        if (op == "save")
        {
            var error = SaveTemplate(name);
            if (error != null)
                Post(new { type = "failed", message = error });
            PostTemplates();
            return;
        }

        LayoutNode? root = null;
        var keep = false;
        if (name == LayoutTemplate.BuiltInName)
            root = EditorLayout.CreateDefault();
        else if (name == LayoutTemplate.RecordingName)
            root = EditorLayout.CreateRecording();
        else
        {
            var found = LayoutTemplateStore.Load().FirstOrDefault(template => template.Name == name);
            if (found == null)
            {
                Post(new { type = "failed", message = "配置テンプレートがありません。" });
                return;
            }

            root = EditorLayout.Clone(found.Root);
            keep = found.KeepRatios;
        }

        _keepRatios = keep;
        EditorLayoutStore.Save(root, keep);
        Post(new { type = "layout", root, keepRatios = keep });
    }

    string? SaveTemplate(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return "名前を入力してください。";
        if (trimmed.Length > 40)
            return "名前は40文字までです。";
        if (trimmed is LayoutTemplate.BuiltInName or LayoutTemplate.RecordingName)
            return "その名前は使えません。";
        if (!EditorLayoutStore.TryLoad(out var root, out var keep))
        {
            root = null;
            keep = _keepRatios;
        }

        var list = LayoutTemplateStore.Load().Where(template => template.Name != trimmed).ToList();
        list.Add(new LayoutTemplate { Name = trimmed, Root = root, KeepRatios = keep });
        try
        {
            LayoutTemplateStore.Save(list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return null;
    }

    void PostAppearance()
    {
        AppAppearance.Load();
        Post(new
        {
            type = "appearance",
            theme = AppAppearance.Theme switch
            {
                AppThemeChoice.Light => "light",
                AppThemeChoice.Dark => "dark",
                _ => "system",
            },
            accent = AppAppearance.Accent?.ToHex(),
        });
    }

    void SaveAppearance(JsonElement root)
    {
        var theme = Text(root, "theme") switch
        {
            "light" => AppThemeChoice.Light,
            "dark" => AppThemeChoice.Dark,
            _ => AppThemeChoice.System,
        };
        AppAppearance.SaveTheme(theme);
        var accent = Text(root, "accent");
        AppAppearance.SaveAccent(RgbColor.TryParse(accent, out var color) ? color : null);
    }

    async Task HandleUpdateAsync(string? op)
    {
        if (op == "apply")
            await _update.ApplyAsync(PostUpdate, StopEngines);
        else
            await _update.CheckAsync(PostUpdate);
    }

    void PostUpdate(string phase, string title, string body) =>
        Post(new { type = "update", phase, title, body });

    void StopEngines()
    {
        _python?.Dispose();
        _python = null;
        _analysis?.Dispose();
        _analysis = null;
    }

    void PostCapture()
    {
        if (_disposed || !_ready)
            return;
        var (samples, rate) = _capture.CopyLive();
        var (lo, hi) = ProbePeaks.Columns(samples, 200);
        Post(new
        {
            type = "capture",
            running = _capture.Running,
            recording = _recording,
            rate = _capture.Running ? rate : 0,
            peak = _capture.Peak,
            lo,
            hi,
            listenError = _capture.ListenError,
        });
        if (_takePlaying)
            Post(new { type = "take", playing = true, position = _take.PositionSeconds, duration = _takeDuration });
    }

    void Post(object payload)
    {
        if (_disposed || !_ready || _view.CoreWebView2 == null)
            return;
        var json = JsonSerializer.Serialize(payload, Json);
        if (_queue.HasThreadAccess)
            _view.CoreWebView2.PostWebMessageAsJson(json);
        else
            _queue.TryEnqueue(() =>
            {
                if (!_disposed && _view.CoreWebView2 != null)
                    _view.CoreWebView2.PostWebMessageAsJson(json);
            });
    }

    static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : 0;

    static int Count(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var parsed))
            return fallback;
        return parsed;
    }

    static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
