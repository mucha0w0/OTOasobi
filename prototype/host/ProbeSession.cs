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
    readonly DispatcherQueueTimer _timer;
    ProbePreview? _python;
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
    DateTime _recordStarted;

    public ProbeSession(MainWindow window, WebView2 view, Action<string> showMissing)
    {
        _window = window;
        _view = view;
        _showMissing = showMissing;
        _queue = DispatcherQueue.GetForCurrentThread();
        _player = new PreviewPlayer(_queue);
        _player.Failed += (_, message) => Post(new { type = "failed", message });
        _player.Ended += (_, _) => Post(new { type = "guide", playing = false });
        _timer = _queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => PostCapture();
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
        if (string.IsNullOrWhiteSpace(url))
            url = "http://localhost:5173";
        _view.Source = new Uri(url);
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
        _python?.Dispose();
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
                    _ = GuideAsync(Text(root, "note") ?? "A4", (int)Math.Round(Number(root, "semitones")), Number(root, "speed"));
                    break;
                case "guideStop":
                    _player.Stop();
                    Post(new { type = "guide", playing = false });
                    break;
                case "record":
                    BeginRecord();
                    break;
                case "recordStop":
                    StopRecord();
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
            }
        }
    }

    async Task HelloAsync()
    {
        if (_disposed)
            return;
        _ready = true;
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
        _folder = folder;
        try
        {
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
        }
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
            Post(new { type = "failed", message = "このファイルは存在しません。" });
            return;
        }

        _wavPath = Path.Combine(_folder, match);
        _previewGen++;
        Post(new { type = "opened", name = match });
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

    async Task GuideAsync(string note, int semitones, double speed)
    {
        if (_disposed)
            return;
        if (!RecTone.TryHertz(note, out var hz))
        {
            Post(new { type = "failed", message = "音名を読めませんでした。" });
            return;
        }

        var tone = RecTone.Tone(hz, RecTone.TargetRate, 2);
        var shaped = RecTone.ShapeGuide(tone, 1, RecTone.TargetRate, speed, semitones);
        var wav = PreviewWav.Encode(shaped, 1, RecTone.TargetRate);
        _player.Volume = 0.5;
        var played = await _player.PlayAsync(wav, loop: true);
        Post(new { type = "guide", playing = played });
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

    void BeginRecord()
    {
        if (_disposed)
            return;
        if (!_capture.Running)
        {
            Post(new { type = "failed", message = "マイクが開いていません。" });
            return;
        }

        _capture.BeginTake();
        _recording = true;
        _recordStarted = DateTime.UtcNow;
        PostCapture();
    }

    void StopRecord()
    {
        if (_disposed || !_recording)
            return;
        var saved = WriteTake();
        PostCapture();
        if (saved != null)
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
            var directory = Path.Combine(AppPaths.DataRoot, "probe-takes");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "take-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".wav");
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
