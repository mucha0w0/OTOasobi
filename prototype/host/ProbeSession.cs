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
    readonly PluginRuntime _pluginsRuntime = new();
    PluginCatalog _plugins = PluginCatalog.Empty;
    bool _applying;
    readonly DispatcherQueueTimer _otoSave;
    readonly DispatcherQueueTimer _otoIniSync;
    readonly DispatcherQueueTimer _otoRows;
    readonly object _postGate = new();
    readonly Queue<Outbound> _outbound = new();
    int _draining;
    int _analyzeGen;
    int _prepareGen;
    volatile bool _preparing;
    string? _folder;
    string? _wavPath;
    string? _inputId;
    string? _outputId;
    bool _monitor;
    bool _watch;
    volatile bool _frqOpen;
    volatile bool _otoTable;
    bool _recording;
    bool _ready;
    bool _disposed;
    int _previewGen;
    int _otoGen;
    int _otoRowGen;
    int _otoIniSyncGen;
    int _otoIniSyncBusy;
    bool _otoIniSyncPending;
    int _fileToken;
    readonly object _previewLock = new();
    PreviewJob? _previewQueued;
    PreviewJob? _previewActive;
    int _previewPump;
    volatile int _fileGen;
    readonly object _frqGate = new();
    List<string> _listedNames = [];
    readonly ProbeWaveCache _waves = new();
    WaveLoad? _waveLoad;

    sealed class WaveLoad(string path, Task<ProbeWave.Audio?> task)
    {
        public string Path { get; } = path;
        public Task<ProbeWave.Audio?> Task { get; } = task;
    }
    int _countGen;
    bool _counting;
    int _recordScheduleGen;
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
        _otoIniSync = _queue.CreateTimer();
        _otoIniSync.Interval = TimeSpan.FromMilliseconds(400);
        _otoIniSync.IsRepeating = false;
        _otoIniSync.Tick += (_, _) =>
        {
            _otoIniSyncPending = false;
            _ = SyncOtoIniAsync();
        };
        _otoRows = _queue.CreateTimer();
        _otoRows.Interval = TimeSpan.FromMilliseconds(80);
        _otoRows.IsRepeating = false;
        _otoRows.Tick += (_, _) => PostOtoRows();
    }

    sealed class Outbound(object payload, int? previewGen, int? otoGen, int? otoRowGen)
    {
        public object Payload { get; } = payload;
        public int? PreviewGen { get; } = previewGen;
        public int? OtoGen { get; } = otoGen;
        public int? OtoRowGen { get; } = otoRowGen;
    }

    sealed class PreviewJob
    {
        public int Generation { get; init; }
        public string Path { get; init; } = "";
        public double T0 { get; init; }
        public double T1 { get; init; }
        public int Columns { get; init; }
        public int Rows { get; init; }
        public TaskCompletionSource<bool> Superseded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        // Ctrl+ホイールや Ctrl+± では画面全体の倍率を変えない。倍率は見た目のスライダー。
        _view.CoreWebView2.Settings.IsZoomControlEnabled = false;
        AppAppearance.Load();
        await _view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            "document.documentElement.style.zoom='" + AppAppearance.Scale.ToString(CultureInfo.InvariantCulture) + "';" +
            "document.documentElement.dataset.panelsLocked='" + (AppAppearance.LockPanels ? "1" : "") + "';");
        _view.CoreWebView2.WebMessageReceived += OnMessage;
        MapPlugins();
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
        _pluginsRuntime.Dispose();
        _capture.Dispose();
        _player.Dispose();
        _take.Dispose();
        _otoSave.Stop();
        _otoIniSync.Stop();
        _otoRows.Stop();
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
                case "pickProjectPlace":
                    _ = PickProjectPlaceAsync();
                    break;
                case "createProject":
                    _ = CreateProjectAsync(Text(root, "parent"), Text(root, "name"));
                    break;
                case "selectFile":
                    if (root.TryGetProperty("file", out var fileToken) && fileToken.TryGetInt32(out var parsedToken) && parsedToken > 0)
                        _fileToken = parsedToken;
                    SelectFile(Text(root, "name"));
                    break;
                case "preview":
                    EnqueuePreview(
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
                    _ = GuideAsync(
                        Text(root, "note") ?? "C4",
                        (int)Math.Round(Number(root, "semitones")),
                        Number(root, "speed"),
                        Volume(root),
                        loop: false,
                        BpmOf(root),
                        MorasOf(root),
                        CountBeatsOf(root),
                        CountBeatsAfterOf(root),
                        MoraBeatsOf(root));
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
                case "watchPanels":
                    WatchPanels(
                        root.TryGetProperty("frq", out var frqOpen) && frqOpen.ValueKind == JsonValueKind.True,
                        root.TryGetProperty("oto", out var otoOpen) && otoOpen.ValueKind == JsonValueKind.True);
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
                case "plugin":
                    StartPlugin(root);
                    break;
            }
        }
    }

    void MapPlugins()
    {
        if (_view.CoreWebView2 == null)
            return;
        _plugins = PluginCatalog.Load(Path.Combine(AppPaths.DataRoot, "plugins"));
        foreach (var item in _plugins.Items)
        {
            _view.CoreWebView2.SetVirtualHostNameToFolderMapping(
                item.Host,
                item.Directory,
                CoreWebView2HostResourceAccessKind.Deny);
        }
    }

    void PostPlugins()
    {
        Post(new
        {
            type = "plugins",
            items = _plugins.Items.Select(item => new
            {
                id = item.Id,
                title = item.Title,
                origin = item.Origin,
                entry = item.Entry,
            }).ToArray(),
        });
        if (_plugins.Errors.Count > 0)
            Post(new { type = "status", message = string.Join("、", _plugins.Errors) });
    }

    void StartPlugin(JsonElement root)
    {
        var id = Text(root, "id") ?? "";
        var request = Text(root, "request") ?? "";
        var op = Text(root, "op") ?? "";
        var path = Text(root, "path") ?? "";
        var text = Text(root, "text") ?? "";
        var encoding = Text(root, "encoding") ?? "";
        JsonElement? body = root.TryGetProperty("body", out var bodyValue) ? bodyValue.Clone() : null;
        var project = _folder;
        var catalog = _plugins;
        _ = Task.Run(async () =>
        {
            var reply = await _pluginsRuntime.Execute(catalog, project, new PluginCall(id, request, op, path, text, encoding, body));
            Post(new
            {
                type = "plugin",
                id,
                request,
                ok = reply.Ok,
                result = reply.Result,
                error = reply.Error,
            });
        });
    }

    async Task HelloAsync()
    {
        if (_disposed)
            return;
        _ready = true;
        PostPlugins();
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
        PostEffects(wave: true);
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
        RetirePreview();
        PostFiles();
        PostOpened(file.Name);
        BeginFile(file.Path);
        if (!string.IsNullOrEmpty(_folder))
        {
            _oto.Open(_folder);
            _rec.OpenFolder(_folder);
            OpenVoice(_folder);
            PostOto();
            PostRec();
            PostFrqCount();
            EnsureWorkSessionFile();
            BeginPrepareFolder();
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

    async Task PickProjectPlaceAsync()
    {
        if (_disposed)
            return;
        string? folder;
        try
        {
            folder = await WavPicker.PickAsync(WindowNative.GetWindowHandle(_window), "作成する場所");
        }
        catch (Exception ex)
        {
            Post(new { type = "failed", message = ex.Message });
            return;
        }

        if (string.IsNullOrEmpty(folder) || _disposed)
            return;
        Post(new { type = "projectPlace", path = folder });
    }

    async Task CreateProjectAsync(string? parent, string? rawName)
    {
        if (_disposed)
            return;
        var error = ProjectFolder.CheckName(rawName, out var name);
        if (error != null)
        {
            Post(new { type = "failed", message = error });
            return;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            Post(new { type = "failed", message = "作成する場所を選んでください。" });
            return;
        }

        string? path;
        try
        {
            path = ProjectFolder.Combine(parent, name);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            Post(new { type = "failed", message = "この場所には作成できません。" });
            return;
        }

        if (path == null)
        {
            Post(new { type = "failed", message = "この場所には作成できません。" });
            return;
        }

        if (Directory.Exists(path) || File.Exists(path))
        {
            Post(new { type = "failed", message = "同じ名前のフォルダが既にあります。" });
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            ProjectFolder.Write(path, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception)
            {
            }

            Post(new { type = "failed", message = ex.Message });
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
        if (!string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase))
            _fileToken++;
        _folder = folder;
        List<string> names;
        try
        {
            names = WavNames(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "failed", message = ex.Message });
            names = [];
        }

        _listedNames = names;
        if (!string.IsNullOrEmpty(_wavPath))
        {
            var current = Path.GetFileName(_wavPath);
            var still = names.Any(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase));
            if (!still)
            {
                _wavPath = null;
                RetirePreview();
                _fileGen++;
            }
        }

        Post(new { type = "files", folder = _folder, names, file = _fileToken });
        _oto.Open(folder);
        _rec.OpenFolder(folder);
        OpenVoice(folder);
        PostOto();
        PostRec();
        PostFrqCount();
        EnsureWorkSessionFile();
        BeginPrepareFolder();
        RecentFolders.Remember(folder);
        PostRecent();
    }

    void EnsureWorkSessionFile()
    {
        if (string.IsNullOrEmpty(_folder) || _oto.SessionLocked)
            return;
        try
        {
            if (!File.Exists(OtoSaveStore.SessionPath(_folder)))
                _oto.FlushSession();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    void BeginPrepareFolder()
    {
        if (string.IsNullOrEmpty(_folder) || _disposed)
            return;
        var gen = ++_prepareGen;
        _ = PrepareFolderAsync(gen);
    }

    async Task PrepareFolderAsync(int gen)
    {
        var folder = _folder!;
        _preparing = true;
        PostStatus("準備しています");
        try
        {
            await Task.Run(() => OriginalLibrary.Prepare(folder, (label, index, count) =>
            {
                _queue.TryEnqueue(() =>
                {
                    if (!_disposed && _folder == folder && gen == _prepareGen)
                        PostStatus(label + "　" + index + " / " + count);
                });
            })).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (gen == _prepareGen && !_disposed && _folder == folder)
                Post(new { type = "failed", message = ex.Message });
        }
        finally
        {
            if (gen == _prepareGen)
                _preparing = false;
            if (gen == _prepareGen && !_disposed && _folder == folder)
            {
                _waves.Bump();
                _waveLoad = null;
                PostStatus("");
            }
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

        var match = _listedNames.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            if (name!.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                _wavPath = null;
                RetirePreview();
                _fileGen++;
                _waveLoad = null;
                lock (_frqGate)
                {
                    _frq.Clear();
                    if (_frqOpen)
                        Post(_frq.View(null));
                }
                PostOpened(name!, missing: true);
                PostOtoView();
                return;
            }

            Post(new { type = "failed", message = "このファイルは存在しません。" });
            return;
        }

        _wavPath = Path.Combine(_folder, match);
        RetirePreview();
        PostOpened(match);
        PostOtoView();
        BeginFile(_wavPath);
    }

    void BeginFile(string path)
    {
        var fileGen = ++_fileGen;
        var epoch = _waves.Epoch;
        var cached = _waves.TryGet(path);
        var task = cached != null
            ? Task.FromResult<ProbeWave.Audio?>(cached)
            : Task.Run(() => LoadWave(path, epoch));
        _waveLoad = new WaveLoad(path, task);
        _ = Task.Run(() =>
        {
            object? view = null;
            lock (_frqGate)
            {
                if (!_frqOpen || fileGen != _fileGen)
                    return;
                view = _frq.View(path);
            }

            if (!_disposed && fileGen == _fileGen && _frqOpen && view != null)
                Post(view);
        });
    }

    void PostFiles()
    {
        if (string.IsNullOrEmpty(_folder))
            return;
        try
        {
            var names = WavNames(_folder);
            _listedNames = names;
            Post(new { type = "files", folder = _folder, names, file = _fileToken });
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

    List<string> ListedNames(JsonElement root)
    {
        var names = new List<string>();
        if (!root.TryGetProperty("names", out var value) || value.ValueKind != JsonValueKind.Array)
            return names;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;
            var name = item.GetString();
            if (!IsPlainFileName(name))
                continue;
            var match = _listedNames.FirstOrDefault(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase));
            if (match != null && !names.Any(entry => string.Equals(entry, match, StringComparison.OrdinalIgnoreCase)))
                names.Add(match);
        }

        return names;
    }

    static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            return false;
        return name.IndexOfAny(['\\', '/', ':']) < 0;
    }

    void PostOpened(string name, bool missing = false) =>
        Post(new { type = "opened", name, missing, file = _fileToken });

    void RetirePreview()
    {
        Interlocked.Increment(ref _previewGen);
        lock (_previewLock)
        {
            _previewQueued?.Superseded.TrySetResult(true);
            _previewQueued = null;
            _previewActive?.Superseded.TrySetResult(true);
        }
    }

    void EnqueuePreview(double t0, double t1, int columns, int rows)
    {
        if (_disposed)
            return;
        if (string.IsNullOrEmpty(_wavPath))
        {
            Post(new { type = "failed", message = "WAV を開いてください。" });
            return;
        }

        var job = new PreviewJob
        {
            Generation = Interlocked.Increment(ref _previewGen),
            Path = _wavPath,
            T0 = t0,
            T1 = t1,
            Columns = Math.Clamp(columns, 32, 1024),
            Rows = Math.Clamp(rows, 32, 256),
        };
        var start = false;
        lock (_previewLock)
        {
            _previewQueued?.Superseded.TrySetResult(true);
            _previewActive?.Superseded.TrySetResult(true);
            _previewQueued = job;
            if (_previewPump == 0)
            {
                _previewPump = 1;
                start = true;
            }
        }

        if (start)
            _ = PumpPreview();
    }

    async Task PumpPreview()
    {
        var stopped = false;
        try
        {
            while (!_disposed)
            {
                PreviewJob job;
                lock (_previewLock)
                {
                    if (_previewQueued == null)
                    {
                        _previewPump = 0;
                        stopped = true;
                        return;
                    }

                    job = _previewQueued;
                    _previewQueued = null;
                    _previewActive = job;
                }

                await RunPreview(job).ConfigureAwait(false);
                lock (_previewLock)
                {
                    if (_previewActive == job)
                        _previewActive = null;
                }
            }
        }
        finally
        {
            if (!stopped)
            {
                lock (_previewLock)
                {
                    if (_disposed || _previewQueued == null)
                        _previewPump = 0;
                    else
                        _ = PumpPreview();
                }
            }
        }
    }

    async Task RunPreview(PreviewJob job)
    {
        try
        {
            var waveTask = WaveAsync(job.Path);
            var finished = await Task.WhenAny(waveTask, job.Superseded.Task).ConfigureAwait(false);
            if (finished != waveTask || !PreviewCurrent(job))
            {
                Forget(waveTask);
                return;
            }

            var audio = await waveTask.ConfigureAwait(false);
            if (!PreviewCurrent(job))
                return;
            if (audio == null)
            {
                Post(new { type = "failed", message = "波形を読めませんでした。" });
                return;
            }

            var (lo, hi, waveT0, waveT1) = ProbeWave.Peaks(audio, job.T0, job.T1, job.Columns);
            PostPreview(new PreviewPicture(
                true,
                null,
                lo,
                hi,
                "",
                "",
                0,
                0,
                false,
                waveT0,
                waveT1,
                waveT0,
                waveT1,
                audio.SampleRate,
                audio.Duration,
                []), job.Generation);
            if (!PreviewCurrent(job))
                return;
            var python = PreviewEngine();
            var load = python.LoadAsync(job.Path, job.T0, job.T1, job.Columns, job.Rows);
            finished = await Task.WhenAny(load, job.Superseded.Task).ConfigureAwait(false);
            if (finished != load || !PreviewCurrent(job))
            {
                Forget(load);
                return;
            }

            var picture = await load.ConfigureAwait(false);
            if (!PreviewCurrent(job) || picture.Skipped)
                return;
            if (!picture.Ok)
            {
                Post(new { type = "failed", message = picture.Error ?? "プレビューを作れませんでした。" });
                return;
            }

            PostPreview(picture, job.Generation);
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or JsonException)
        {
            if (_python != null && !_python.Alive)
            {
                _python.Dispose();
                _python = null;
            }

            if (PreviewCurrent(job))
                Post(new { type = "failed", message = ex.Message });
        }
    }

    bool PreviewCurrent(PreviewJob job) =>
        !_disposed
        && job.Generation == Volatile.Read(ref _previewGen)
        && string.Equals(job.Path, _wavPath, StringComparison.OrdinalIgnoreCase);

    static void Forget(Task task) =>
        _ = task.ContinueWith(
            done => { _ = done.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    ProbePreview PreviewEngine()
    {
        if (_python != null && !_python.Alive)
        {
            _python.Dispose();
            _python = null;
        }

        return _python ??= new ProbePreview();
    }

    AnalysisClient Analysis()
    {
        if (_analysis != null && !_analysis.Alive)
        {
            _analysis.Dispose();
            _analysis = null;
        }

        return _analysis ??= new AnalysisClient();
    }

    void PostPreview(PreviewPicture picture, int generation)
    {
        if (generation != Volatile.Read(ref _previewGen))
            return;
        Post(new
        {
            type = "preview",
            name = string.IsNullOrEmpty(_wavPath) ? "" : Path.GetFileName(_wavPath),
            duration = picture.Duration,
            lo = picture.Lo,
            hi = picture.Hi,
            png = "",
            spec = picture.Spec,
            specColumns = picture.SpecColumns,
            specRows = picture.SpecRows,
            specT0 = picture.SpecT0,
            specT1 = picture.SpecT1,
            waveT0 = picture.WaveT0,
            waveT1 = picture.WaveT1,
            sampleRate = picture.SampleRate,
            loudness = picture.Loudness,
            file = _fileToken,
        }, previewGen: generation);
    }

    ProbeWave.Audio? LoadWave(string path, int epoch)
    {
        try
        {
            var audio = ProbeWave.Load(path);
            if (audio != null)
                _waves.Store(path, audio, epoch);
            return audio;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    async Task<ProbeWave.Audio?> WaveAsync(string path)
    {
        var cached = _waves.TryGet(path);
        if (cached != null)
            return cached;
        var pending = _waveLoad;
        if (pending != null && string.Equals(pending.Path, path, StringComparison.OrdinalIgnoreCase))
            return await pending.Task.ConfigureAwait(true);
        return await Task.Run(() => LoadWave(path, _waves.Epoch)).ConfigureAwait(true);
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
        else
            TryApplyGuideScript(path);
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
        else
            TryApplyGuideScript(file.Path);
        PostGuide();
    }

    void TryApplyGuideScript(string wavPath)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            return;
        double duration;
        try
        {
            var audio = WavAudio.Read(wavPath);
            duration = audio.Samples.Length / (double)Math.Max(1, audio.Channels) / audio.SampleRate;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return;
        }

        var offsets = RecGuideScript.Parse(RecFolderStore.GuideTextPath(wavPath), duration);
        if (offsets.StartAfter == null && offsets.EndBefore == null)
            return;
        var settings = RecAppSettings.Load();
        if (offsets.StartAfter is double start)
            settings.RecStartAfter = start;
        if (offsets.EndBefore is double end)
            settings.RecEndBefore = end;
        settings.Save();
        PostRecSettings();
    }

    void PostGuide() => Post(_rec.GuideSnapshot());

    async Task GuideAsync(string note, int semitones, double speed, double volume, bool loop, int bpm, int moras, int countBeats, int countBeatsAfter, double moraBeats)
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

        // 録音中のループには先頭カウントを入れない。発声中のクリックはループでも効く。
        var tone = RecTone.ManualGuide(hz, bpm, moras, RecTone.TargetRate, countEnabled: true, preCount: !loop, countBeats, countBeatsAfter, moraBeats);
        var shaped = RecTone.ShapeGuide(tone, 1, RecTone.TargetRate, speed, semitones);
        var wav = PreviewWav.Encode(shaped, 1, RecTone.TargetRate);
        _player.Volume = volume;
        var played = await _player.PlayAsync(wav, loop);
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
        var bpm = (int)Math.Clamp(Math.Round(Number(root, "bpm") == 0 ? 120 : Number(root, "bpm")), 40, 240);
        var countBeats = CountBeatsOf(root);
        if (manualGuide)
        {
            var token = ++_countGen;
            _counting = true;
            Post(new { type = "recordWait", waiting = true });
            PostStatus("カウントしています");
            var clicks = PreviewWav.Encode(RecTone.CountIn(bpm, RecTone.TargetRate, countBeats), 1, RecTone.TargetRate);
            _player.Volume = Volume(root);
            var played = await _player.PlayAsync(clicks, loop: false);
            var seconds = countBeats * 60.0 / bpm;
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

        var startAfter = RecStartAfterOf(root);
        var endBefore = RecEndBeforeOf(root);
        var schedule = ++_recordScheduleGen;
        var plan = BuildRecordGuidePlan(root, manualGuide, bpm, MorasOf(root), countBeats, CountBeatsAfterOf(root), MoraBeatsOf(root));
        var guideDuration = plan?.DurationSeconds ?? 0;
        var wantPlay = plan?.Wav != null;

        DateTime bgmAt = DateTime.UtcNow;
        if (wantPlay && plan?.Wav != null)
        {
            _player.Volume = Volume(root);
            var played = await _player.PlayAsync(plan.Wav, loop: false);
            bgmAt = DateTime.UtcNow;
            if (!played && !_disposed)
            {
                Post(new { type = "failed", message = "ガイドを再生できませんでした。" });
                return;
            }
        }
        else if (guideDuration > 0)
            bgmAt = DateTime.UtcNow;

        if (_disposed || _recording || schedule != _recordScheduleGen)
            return;

        await WaitUntilSecondsFromAsync(bgmAt, startAfter, schedule);
        if (_disposed || _recording || schedule != _recordScheduleGen || !_capture.Running)
            return;

        _capture.BeginTake();
        _recording = true;
        _recordStarted = DateTime.UtcNow;
        PostCapture();

        if (guideDuration > 0)
        {
            var stopAt = guideDuration - endBefore - startAfter;
            if (stopAt > 0.05)
                _ = ScheduleRecordStopAsync(stopAt, schedule);
        }
    }

    sealed record RecordGuidePlan(byte[]? Wav, double DurationSeconds);

    RecordGuidePlan? BuildRecordGuidePlan(JsonElement root, bool manual, int bpm, int moras, int countBeats, int countBeatsAfter, double moraBeats)
    {
        var semitones = (int)Math.Round(Number(root, "semitones"));
        var speed = Number(root, "speed") == 0 ? 1 : Number(root, "speed");
        if (manual)
        {
            var note = Text(root, "note") ?? "C4";
            if (!RecTone.TryHertz(note, out var hz))
                return null;
            var tone = RecTone.ManualGuide(hz, bpm, moras, RecTone.TargetRate, countEnabled: true, preCount: false, countBeats, countBeatsAfter, moraBeats);
            var shaped = RecTone.ShapeGuide(tone, 1, RecTone.TargetRate, speed, semitones);
            var wav = PreviewWav.Encode(shaped, 1, RecTone.TargetRate);
            return new RecordGuidePlan(wav, shaped.Length / (double)RecTone.TargetRate);
        }

        var path = _rec.GuidePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new RecordGuidePlan(null, 0);
        try
        {
            var audio = WavAudio.Read(path);
            var shaped = RecTone.ShapeGuide(audio.Samples, audio.Channels, audio.SampleRate, speed, semitones);
            var wav = PreviewWav.Encode(shaped, audio.Channels, audio.SampleRate);
            var seconds = shaped.Length / (double)Math.Max(1, audio.Channels) / audio.SampleRate;
            return new RecordGuidePlan(wav, seconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Post(new { type = "failed", message = ex.Message });
            return new RecordGuidePlan(null, 0);
        }
    }

    async Task WaitUntilSecondsFromAsync(DateTime started, double seconds, int schedule)
    {
        if (seconds <= 0)
            return;
        while (schedule == _recordScheduleGen && !_disposed)
        {
            var left = seconds - (DateTime.UtcNow - started).TotalSeconds;
            if (left <= 0)
                return;
            await Task.Delay(Math.Clamp((int)(left * 1000), 10, 80));
        }
    }

    async Task ScheduleRecordStopAsync(double seconds, int schedule)
    {
        var delay = TimeSpan.FromSeconds(seconds);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay);
        if (schedule != _recordScheduleGen || _disposed || !_recording)
            return;
        _queue.TryEnqueue(StopRecord);
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

        _recordScheduleGen++;
        _player.Stop();
        Post(new { type = "guide", playing = false });
        if (_disposed || !_recording)
            return;
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
                RememberOto(true);
                break;
            case "slider":
                if (_oto.SetSlider(Text(root, "group"), Text(root, "key"), Number(root, "value")))
                    RememberOto(false);
                break;
            case "preset":
                _oto.SelectPreset(Text(root, "target"), Text(root, "name"));
                RememberOto(false);
                break;
            case "savePreset":
                var presetError = _oto.SavePreset(Text(root, "which"), Text(root, "name"));
                if (presetError != null)
                    Post(new { type = "failed", message = presetError });
                RememberOto(true);
                break;
            case "alias":
                _oto.SetAlias(Text(root, "which"), root.TryGetProperty("on", out var aliasOn) && aliasOn.ValueKind == JsonValueKind.True);
                RememberOto(true);
                break;
            case "scope":
                _oto.SetScope(Text(root, "scope"));
                RememberOto(true, syncIni: false);
                break;
            case "analyze":
                BeginAnalyze(root.TryGetProperty("force", out var forceValue) && forceValue.ValueKind == JsonValueKind.True);
                break;
            case "analyzeFile":
                BeginAnalyzeFiles(ListedNames(root));
                break;
            case "write":
                _ = WriteOtoAsync();
                break;
            case "tune":
                if (_oto.TunePoint(Text(root, "file"), (int)Math.Round(Number(root, "index")), (int)Math.Round(Number(root, "point")), Number(root, "time")))
                    RememberOto(true, syncIni: false);
                break;
            case "spans":
                if (_oto.MoveSpans(Text(root, "file"), ReadSpanTimes(Property(root, "spans"))))
                {
                    _ = SaveSpanCacheAsync();
                    ScheduleOtoRows();
                }
                break;
            case "fields":
                if (_oto.TuneFields(Text(root, "file"), (int)Math.Round(Number(root, "index")), Text(root, "alias"), Number(root, "offset"), Number(root, "overlap"), Number(root, "preutter"), Number(root, "consonant"), Number(root, "blank"), Text(root, "mode")))
                    RememberOto(true);
                break;
            case "aliasName":
                if (_oto.TuneAlias(Text(root, "file"), (int)Math.Round(Number(root, "index")), Text(root, "alias"), Text(root, "name"), Text(root, "mode")))
                    RememberOto(true);
                break;
            case "comment":
                if (_oto.TuneComment(Text(root, "file"), (int)Math.Round(Number(root, "index")), Text(root, "alias"), Text(root, "comment"), Text(root, "mode")))
                    RememberOto(true);
                break;
        }
    }

    async Task SaveSpanCacheAsync()
    {
        if (_disposed || string.IsNullOrEmpty(_folder) || _oto.Files.Count == 0)
            return;
        try
        {
            await Analysis().SaveCacheAsync(_folder, _oto.Files);
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

    void RememberOto(bool rowsNow, bool syncIni = false)
    {
        _otoSave.Stop();
        _otoSave.Start();
        if (syncIni && _oto.HasIni)
            ScheduleOtoIniSync();
        if (rowsNow)
            PostOto();
        else
        {
            PostOtoView();
            ScheduleOtoRows();
        }
    }

    void ScheduleOtoIniSync()
    {
        if (_disposed || !_oto.HasIni)
            return;
        _otoIniSyncPending = true;
        _otoIniSync.Stop();
        _otoIniSync.Start();
    }

    async Task SyncOtoIniAsync()
    {
        if (_disposed || string.IsNullOrEmpty(_folder) || _oto.Files.Count == 0 || _oto.Busy || _oto.SessionLocked)
            return;
        var modes = _oto.AliasModes();
        if (modes.Count == 0)
            return;
        if (Interlocked.CompareExchange(ref _otoIniSyncBusy, 1, 0) != 0)
        {
            ScheduleOtoIniSync();
            return;
        }

        var gen = Interlocked.Increment(ref _otoIniSyncGen);
        try
        {
            var parts = modes.Select(mode => new OtoWritePart(_oto.ExportFor(mode), _oto.Adjustments(mode))).ToList();
            var result = await Analysis().SyncOtoAsync(_folder, _oto.Files, parts).ConfigureAwait(true);
            if (_disposed || gen != _otoIniSyncGen)
                return;
            if (!result.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(result.Error) ? "oto.iniを書き込めませんでした。" : result.Error });
        }
        catch (Exception ex)
        {
            if (!_disposed && gen == _otoIniSyncGen)
                Post(new { type = "failed", message = ex.Message });
        }
        finally
        {
            Interlocked.Exchange(ref _otoIniSyncBusy, 0);
            if (_otoIniSyncPending)
                ScheduleOtoIniSync();
        }
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
        if (_preparing)
        {
            Post(new { type = "failed", message = "準備が終わるまで待ってください。" });
            return;
        }

        var gen = ++_analyzeGen;
        try
        {
            _analysis?.Cancel();
        }
        catch (IOException)
        {
        }

        _ = AnalyzeAsync(gen, force);
    }

    void BeginAnalyzeFiles(IReadOnlyList<string> names)
    {
        var list = names.Count > 0 ? names.ToList() : new List<string>();
        if (list.Count == 0 && !string.IsNullOrEmpty(_wavPath))
        {
            var current = Path.GetFileName(_wavPath);
            if (_listedNames.Any(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase)))
                list.Add(current);
        }

        if (_disposed || string.IsNullOrEmpty(_folder) || list.Count == 0)
        {
            Post(new { type = "failed", message = "ファイルを選んでください。" });
            return;
        }

        if (_preparing)
        {
            Post(new { type = "failed", message = "準備が終わるまで待ってください。" });
            return;
        }

        if (_oto.IsBusy)
        {
            Post(new { type = "failed", message = "解析が終わるまで待ってください。" });
            return;
        }

        var gen = ++_analyzeGen;
        try
        {
            _analysis?.Cancel();
        }
        catch (IOException)
        {
        }

        _ = AnalyzeFilesAsync(gen, list);
    }

    async Task AnalyzeAsync(int gen, bool force)
    {
        _oto.SetBusy(true);
        PostOto();
        PostStatus("解析しています");
        try
        {
            var data = await Analysis().AnalyzeFolderAsync(
                _folder!,
                (done, total) => PostStatus($"解析しています　{done} / {total} ファイル"),
                force,
                (name, _, label) => PostStatus(string.IsNullOrEmpty(label) ? name : $"{name}　{label}"));
            if (gen != _analyzeGen || _disposed || data.Cancelled)
                return;
            if (!data.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(data.Error) ? "解析できませんでした。" : data.Error });
            else
            {
                _oto.SetFiles(data.Files);
                await SaveSpanCacheAsync();
            }
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

    async Task AnalyzeFilesAsync(int gen, IReadOnlyList<string> names)
    {
        _oto.SetBusy(true);
        PostOto();
        PostStatus(names.Count == 1 ? $"{names[0]} を解析しています" : "解析しています");
        try
        {
            var data = await Analysis().AnalyzeFilesAsync(
                _folder!,
                names,
                (done, total) => PostStatus($"解析しています　{done} / {total} ファイル"),
                (name, _, label) => PostStatus(string.IsNullOrEmpty(label) ? name : $"{name}　{label}"));
            if (gen != _analyzeGen || _disposed || data.Cancelled)
                return;
            if (!data.Ok)
                Post(new { type = "failed", message = string.IsNullOrEmpty(data.Error) ? "解析できませんでした。" : data.Error });
            else if (data.Files.Count == 0)
                Post(new { type = "failed", message = "解析できませんでした。" });
            else
            {
                foreach (var file in data.Files)
                    _oto.MergeFile(file);
                await SaveSpanCacheAsync();
                if (!string.IsNullOrWhiteSpace(data.Notice))
                    Post(new { type = "notice", message = data.Notice });
            }
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

        _otoIniSync.Stop();
        _otoIniSyncPending = false;
        Interlocked.Increment(ref _otoIniSyncGen);
        _oto.SetBusy(true);
        PostOto();
        try
        {
            var analysis = Analysis();
            var parts = modes.Select(mode => new OtoWritePart(_oto.ExportFor(mode), Array.Empty<OtoAdjustedEntry>())).ToList();
            var result = await analysis.WriteAsync(_folder, _oto.Files, parts, _oto.ScopeForWrite());
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
                _ = WriteFrqAsync(Text(root, "mode"), root.TryGetProperty("clear", out var clear) && clear.ValueKind == JsonValueKind.True, ListedNames(root));
                break;
            case "stroke":
                lock (_frqGate)
                    PostFrqEdit(_frq.Stroke(Property(root, "points")));
                break;
            case "octave":
                lock (_frqGate)
                    PostFrqEdit(_frq.Shift(Property(root, "frames"), (int)Math.Round(Number(root, "octaves"))));
                break;
            case "clear":
                lock (_frqGate)
                    PostFrqEdit(_frq.ClearPitch(Property(root, "frames")));
                break;
        }
    }

    async Task WriteFrqAsync(string? mode, bool clear, IReadOnlyList<string> names)
    {
        if (_disposed || string.IsNullOrEmpty(_folder))
            return;
        if (_oto.IsBusy)
        {
            Post(new { type = "failed", message = "解析が終わるまで待ってください。" });
            return;
        }

        mode = mode is "missing" or "selected" ? mode : "all";
        object? only = null;
        if (mode == "selected")
        {
            var list = names.Count > 0 ? names.ToList() : new List<string>();
            if (list.Count == 0 && !string.IsNullOrEmpty(_wavPath))
            {
                var current = Path.GetFileName(_wavPath);
                if (!string.IsNullOrEmpty(current))
                    list.Add(current);
            }

            if (list.Count == 0)
            {
                Post(new { type = "failed", message = "ファイルを選んでください。" });
                return;
            }

            only = list.ToArray();
        }

        _oto.SetBusy(true);
        PostOto();
        PostStatus(".frqを計算しています");
        try
        {
            var result = await Analysis().WriteFrqAsync(
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

            if (!string.IsNullOrEmpty(_wavPath) && _frqOpen)
            {
                object view;
                lock (_frqGate)
                    view = _frq.View(_wavPath);
                Post(view);
            }
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
        var bpm = Number(root, "bpm");
        if (bpm > 0)
            settings.Bpm = (int)Math.Clamp(Math.Round(bpm), 40, 240);
        if (root.TryGetProperty("moras", out _))
            settings.Moras = MorasOf(root);
        if (root.TryGetProperty("moraBeats", out _))
            settings.MoraBeats = MoraBeatsOf(root);
        if (root.TryGetProperty("countBeats", out _))
            settings.CountBeats = CountBeatsOf(root);
        if (root.TryGetProperty("countBeatsAfter", out _))
            settings.CountBeatsAfter = CountBeatsAfterOf(root);
        else if (root.TryGetProperty("countWhileMoras", out var moraCount) && moraCount.ValueKind == JsonValueKind.True && settings.CountBeatsAfter == 0)
            settings.CountBeatsAfter = RecTone.DefaultCountBeats;
        settings.AutoAdvance = root.TryGetProperty("advance", out var advance) && advance.ValueKind == JsonValueKind.True;
        if (root.TryGetProperty("recStartAfter", out _))
            settings.RecStartAfter = RecStartAfterOf(root);
        if (root.TryGetProperty("recEndBefore", out _))
            settings.RecEndBefore = RecEndBeforeOf(root);
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
            countBeats = settings.CountBeats,
            countBeatsAfter = settings.CountBeatsAfter,
            bpm = settings.Bpm,
            moras = settings.Moras,
            moraBeats = settings.MoraBeats,
            advance = settings.AutoAdvance,
            recStartAfter = settings.RecStartAfter,
            recEndBefore = settings.RecEndBefore,
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
        PostEffects(wave: true);
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
                PostEffects(wave: false);
                break;
            case "noise":
                _effects.SetNoise(Text(root, "file"), OptionalNumber(root, "start"), OptionalNumber(root, "end"));
                PostEffects(wave: true);
                break;
            case "apply":
                _ = ApplyEffectsAsync();
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

        if (_applying || _preparing || _oto.IsBusy)
        {
            Post(new { type = "failed", message = _preparing ? "準備が終わるまで待ってください。" : "解析が終わるまで待ってください。" });
            return;
        }

        var folder = _folder;
        _waves.Bump();
        _waveLoad = null;
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
            _waves.Bump();
            _waveLoad = null;
            if (_folder == folder)
            {
                PostStatus("");
                Post(new { type = "failed", message = "適用できませんでした。" + ex.Message });
            }

            return;
        }

        _applying = false;
        _waves.Bump();
        _waveLoad = null;
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

    void WatchPanels(bool frq, bool oto)
    {
        var openFrq = frq && !_frqOpen;
        var openOto = oto && !_otoTable;
        _frqOpen = frq;
        _otoTable = oto;
        if (!frq)
        {
            lock (_frqGate)
                _frq.Clear();
        }
        else if (openFrq && !string.IsNullOrEmpty(_wavPath))
        {
            var path = _wavPath;
            var fileGen = _fileGen;
            _ = Task.Run(() =>
            {
                object? view = null;
                lock (_frqGate)
                {
                    if (!_frqOpen || fileGen != _fileGen)
                        return;
                    view = _frq.View(path);
                }

                if (!_disposed && fileGen == _fileGen && _frqOpen && view != null)
                    Post(view);
            });
        }

        if (!oto)
            _otoRows.Stop();
        else if (openOto)
            PostOtoRows();
    }

    string? CurrentWavName() => string.IsNullOrEmpty(_wavPath) ? null : Path.GetFileName(_wavPath);

    void PostOto()
    {
        _otoRows.Stop();
        PostOtoView();
        if (_otoTable)
            PostOtoRows();
    }

    void PostOtoView()
    {
        var generation = Interlocked.Increment(ref _otoGen);
        Post(_oto.Snapshot(CurrentWavName(), false, generation), otoGen: generation);
    }

    void PostOtoRows()
    {
        if (_disposed || !_otoTable)
            return;
        var rowGeneration = Interlocked.Increment(ref _otoRowGen);
        var viewGeneration = Volatile.Read(ref _otoGen);
        var name = CurrentWavName();
        _ = Task.Run(() =>
        {
            if (_disposed || rowGeneration != Volatile.Read(ref _otoRowGen))
                return;
            var payload = _oto.Snapshot(name, true, viewGeneration, rowGeneration, includeViewFields: false);
            Post(payload, otoRowGen: rowGeneration);
        });
    }

    void ScheduleOtoRows()
    {
        if (!_otoTable)
            return;
        _otoRows.Stop();
        _otoRows.Start();
    }

    static JsonElement Property(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value : default;

    static double Volume(JsonElement root)
    {
        if (!root.TryGetProperty("volume", out var value) || !value.TryGetDouble(out var parsed))
            return 0.7;
        return Math.Clamp(parsed, 0, 1);
    }

    static int BpmOf(JsonElement root)
    {
        var bpm = Number(root, "bpm");
        return (int)Math.Clamp(Math.Round(bpm <= 0 ? 120 : bpm), 40, 240);
    }

    static int MorasOf(JsonElement root)
    {
        var moras = Number(root, "moras");
        return (int)Math.Clamp(Math.Round(moras <= 0 ? RecTone.DefaultMoras : moras), RecTone.MinMoras, RecTone.MaxMoras);
    }

    static double MoraBeatsOf(JsonElement root)
    {
        if (root.TryGetProperty("moraBeats", out var value) && value.TryGetDouble(out var beats))
            return Math.Clamp(beats, RecTone.MinMoraBeats, RecTone.MaxMoraBeats);
        return RecAppSettings.Load().MoraBeats;
    }

    static int CountBeatsOf(JsonElement root)
    {
        var beats = Number(root, "countBeats");
        return (int)Math.Clamp(Math.Round(beats <= 0 ? RecTone.DefaultCountBeats : beats), RecTone.MinCountBeats, RecTone.MaxCountBeats);
    }

    static int CountBeatsAfterOf(JsonElement root)
    {
        if (root.TryGetProperty("countBeatsAfter", out var after) && after.TryGetDouble(out var parsedAfter))
            return (int)Math.Clamp(Math.Round(parsedAfter), 0, RecTone.MaxCountBeats);
        if (root.TryGetProperty("countWhileMoras", out var legacy) && legacy.ValueKind == JsonValueKind.True)
            return RecTone.DefaultCountBeats;
        return 0;
    }

    static double RecStartAfterOf(JsonElement root)
    {
        if (root.TryGetProperty("recStartAfter", out var value) && value.TryGetDouble(out var seconds))
            return Math.Clamp(seconds, 0, 600);
        return RecAppSettings.Load().RecStartAfter;
    }

    static double RecEndBeforeOf(JsonElement root)
    {
        if (root.TryGetProperty("recEndBefore", out var value) && value.TryGetDouble(out var seconds))
            return Math.Clamp(seconds, 0, 600);
        return RecAppSettings.Load().RecEndBefore;
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
            scale = AppAppearance.Scale,
            lockPanels = AppAppearance.LockPanels,
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
        if (root.TryGetProperty("scale", out var scale) && scale.TryGetDouble(out var value))
            AppAppearance.SaveScale(value);
        if (root.TryGetProperty("lockPanels", out var locked) && locked.ValueKind is JsonValueKind.True or JsonValueKind.False)
            AppAppearance.SaveLockPanels(locked.ValueKind == JsonValueKind.True);
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

    void Post(object payload, int? previewGen = null, int? otoGen = null, int? otoRowGen = null)
    {
        if (_disposed)
            return;
        var start = false;
        lock (_postGate)
        {
            _outbound.Enqueue(new Outbound(payload, previewGen, otoGen, otoRowGen));
            if (_draining == 0)
            {
                _draining = 1;
                start = true;
            }
        }

        if (start)
            _ = Task.Run(DrainPosts);
    }

    void DrainPosts()
    {
        while (true)
        {
            Outbound item;
            lock (_postGate)
            {
                if (_outbound.Count == 0 || _disposed)
                {
                    _draining = 0;
                    return;
                }

                item = _outbound.Dequeue();
            }

            if (Stale(item))
                continue;
            string json;
            try
            {
                json = JsonSerializer.Serialize(item.Payload, Json);
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException)
            {
                continue;
            }

            if (_disposed || Stale(item))
                continue;
            _queue.TryEnqueue(() => Deliver(json));
        }
    }

    bool Stale(Outbound item)
    {
        if (item.PreviewGen is int preview && preview != Volatile.Read(ref _previewGen))
            return true;
        if (item.OtoGen is int oto && oto != Volatile.Read(ref _otoGen))
            return true;
        if (item.OtoRowGen is int row && row != Volatile.Read(ref _otoRowGen))
            return true;
        return false;
    }

    void Deliver(string json)
    {
        if (_disposed || !_ready || _view.CoreWebView2 == null)
            return;
        _view.CoreWebView2.PostWebMessageAsJson(json);
    }

    void PostEffects(bool wave)
    {
        if (wave)
            Post(_effects.NoiseWave());
        Post(_effects.Snapshot());
    }

    static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : 0;

    static double? OptionalNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : null;

    static int Count(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var parsed))
            return fallback;
        return parsed;
    }

    static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
