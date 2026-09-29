using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Otoasobi;

public sealed partial class MainPage : Page
{
    static AnalysisClient? _analysis;

    readonly List<WavFile> _files = [];
    string? _folder;
    string? _notice;
    bool _busy;
    bool _wrote;
    bool _suppressSelection;
    int _previewGen;
    int _saveGen;
    IDisposable? _previewStream;
    DispatcherQueueTimer? _viewportTimer;
    string _appliedKey = "";
    bool _configReady;

    // oto.py の PAD_MS、OVERLAP_AT、ブランク 0 秒（上辺の右端からのずれ）。
    const double DefaultMarginMs = 200;
    const double DefaultBlankSeconds = 0;
    const double DefaultOverlapPercent = 50;

    public MainPage()
    {
        InitializeComponent();
        Editor.Edited += (_, _) => OnEdited();
        Editor.ViewportChanged += (_, _) => ScheduleViewport();
        OffsetSlider.ValueChanged += (_, _) => OnConfigChanged();
        ConsonantSlider.ValueChanged += (_, _) => OnConfigChanged();
        BlankSlider.ValueChanged += (_, _) => OnConfigChanged();
        OverlapSlider.ValueChanged += (_, _) => OnConfigChanged();
        _configReady = true;
        Editor.SetOtoSettings(CurrentExport());
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPageWheel), true);
    }

    void OnPageWheel(object sender, PointerRoutedEventArgs e)
    {
        if (EditorView.Visibility != Visibility.Visible || Editor.ActualWidth < 2)
            return;
        var point = e.GetCurrentPoint(Editor);
        if (point.Position.X < 0 || point.Position.Y < 0
            || point.Position.X > Editor.ActualWidth
            || point.Position.Y > Editor.ActualHeight)
            return;
        var delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
            delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (Editor.ZoomAt(point.Position.X, delta, e))
            e.Handled = true;
    }

    public static void ShutdownAnalysis()
    {
        _analysis?.Dispose();
        _analysis = null;
    }

    public void OpenFolder(IReadOnlyList<string> paths)
    {
        if (_busy)
            return;
        var folder = FolderFrom(paths);
        if (folder == null)
        {
            ShowError("フォルダを選んでください。");
            return;
        }

        _ = RunAsync(folder, force: false);
    }

    async Task ConfirmReanalyzeAsync()
    {
        if (_busy || _folder == null)
            return;
        var dialog = new ContentDialog
        {
            Title = "再解析しますか？",
            Content = "保存データを使わず、フォルダの WAV を解析し直します。いまの区間は置き換わります。",
            PrimaryButtonText = "再解析する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _busy || _folder == null)
            return;
        await RunAsync(_folder, force: true);
    }

    public void ChooseFolder()
    {
        _ = PickFolderAsync();
    }

    void IdleView_Click(object sender, RoutedEventArgs e)
    {
        _ = PickFolderAsync();
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Close();
    }

    void NoticeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        NoticeHost.Visibility = Visibility.Collapsed;
    }

    async Task PickFolderAsync()
    {
        if (_busy || App.MainWindow == null)
            return;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        var path = await WavPicker.PickAsync(hwnd);
        if (path != null)
            OpenFolder([path]);
    }

    void WriteOto_Click(object sender, RoutedEventArgs e)
    {
        _ = WriteAsync();
    }

    void Reanalyze_Click(object sender, RoutedEventArgs e)
    {
        _ = ConfirmReanalyzeAsync();
    }

    void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;
        if (FileList.SelectedItem is not WavFile file)
            return;
        _ = LoadPreviewAsync(file);
    }

    async Task RunAsync(string folderPath, bool force)
    {
        _busy = true;
        _folder = folderPath;
        _files.Clear();
        _notice = null;
        _wrote = false;
        var total = CountTargets(folderPath);
        ShowBusy(0, total);
        var queue = DispatcherQueue;
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.AnalyzeFolderAsync(folderPath, (done, count) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateStatus(done, count);
                });
            }, force, (name, done, label) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateFileStatus(name, done, label);
                });
            });
            if (!result.Ok || result.Files.Count == 0)
            {
                ShowError(string.IsNullOrEmpty(result.Error) ? "解析に失敗しました。" : result.Error);
                return;
            }

            _notice = result.Notice;
            _files.AddRange(result.Files);
            ShowEditor(folderPath, result.Cached);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateCommands();
        }
    }

    void ScheduleViewport()
    {
        if (_viewportTimer == null)
        {
            _viewportTimer = DispatcherQueue.CreateTimer();
            _viewportTimer.Interval = TimeSpan.FromMilliseconds(40);
            _viewportTimer.IsRepeating = false;
            _viewportTimer.Tick += (_, _) => _ = LoadViewportAsync();
        }

        _viewportTimer.Stop();
        _viewportTimer.Start();
    }

    async Task LoadPreviewAsync(WavFile file)
    {
        if (_folder == null)
            return;
        _viewportTimer?.Stop();
        var gen = ++_previewGen;
        _appliedKey = "";
        ShowFile(file);
        Editor.Clear("波形を読み込んでいます…");
        try
        {
            _analysis ??= new AnalysisClient();
            var (columns, rows) = Editor.RasterSize();
            var preview = await _analysis.PreviewAsync(
                Path.Combine(_folder, file.Name), 0, file.Duration, columns, rows);
            if (gen != _previewGen)
                return;
            if (!preview.Ok)
            {
                Editor.Clear(string.IsNullOrEmpty(preview.Error) ? "波形を表示できません。" : preview.Error);
                return;
            }

            var image = await DecodePngAsync(preview.Png);
            if (gen != _previewGen)
                return;
            Editor.SetFile(
                file,
                image,
                preview.SpecT0,
                preview.SpecT1,
                preview.Lo,
                preview.Hi,
                preview.Loudness,
                preview.WaveT0,
                preview.WaveT1,
                preview.Samples,
                preview.SampleRate);
            var current = Editor.ViewportQuery();
            if (current.Columns == columns && current.Rows == rows
                && Math.Abs(current.T0) < 1e-6
                && Math.Abs(current.T1 - file.Duration) < 1e-4)
            {
                _appliedKey = ViewportKey(file.Name, current.T0, current.T1, current.Columns, current.Rows);
            }
        }
        catch (Exception ex)
        {
            if (gen == _previewGen)
                Editor.Clear(ex.Message);
        }
    }

    async Task LoadViewportAsync()
    {
        if (_folder == null || FileList.SelectedItem is not WavFile file || !Editor.HasContent)
            return;
        var query = Editor.ViewportQuery();
        var key = ViewportKey(file.Name, query.T0, query.T1, query.Columns, query.Rows);
        if (key == _appliedKey)
            return;
        var gen = ++_previewGen;
        try
        {
            _analysis ??= new AnalysisClient();
            var preview = await _analysis.PreviewAsync(
                Path.Combine(_folder, file.Name), query.T0, query.T1, query.Columns, query.Rows);
            if (gen != _previewGen || FileList.SelectedItem != file || !Editor.HasContent || !preview.Ok)
                return;
            var image = await DecodePngAsync(preview.Png);
            if (gen != _previewGen || FileList.SelectedItem != file || !Editor.HasContent)
                return;
            var latest = Editor.ViewportQuery();
            if (ViewportKey(file.Name, latest.T0, latest.T1, latest.Columns, latest.Rows) != key)
                return;
            Editor.ShowDetail(
                image,
                preview.SpecT0,
                preview.SpecT1,
                preview.Lo,
                preview.Hi,
                preview.WaveT0,
                preview.WaveT1,
                preview.Samples,
                preview.SampleRate);
            _appliedKey = key;
        }
        catch (Exception)
        {
            if (gen == _previewGen)
                _appliedKey = "";
        }
    }

    static string ViewportKey(string name, double t0, double t1, int columns, int rows)
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join(
            "|",
            name,
            t0.ToString("R", culture),
            t1.ToString("R", culture),
            columns.ToString(culture),
            rows.ToString(culture));
    }

    async Task WriteAsync()
    {
        if (_busy || _folder == null || _files.Count == 0)
            return;
        _busy = true;
        UpdateCommands();
        Editor.IsEnabled = false;
        ConfigBody.IsEnabled = false;
        ShowStatus("oto.iniを計算しています");
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteAsync(_folder, _files, CurrentExport());
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowNotice(
                    "oto.iniを作成できませんでした",
                    string.IsNullOrEmpty(result.Error) ? null : result.Error,
                    InfoBarSeverity.Error);
                return;
            }

            _wrote = true;
            if (!string.IsNullOrWhiteSpace(result.Notice))
            {
                _notice = string.IsNullOrWhiteSpace(_notice)
                    ? result.Notice
                    : _notice + "\n" + result.Notice;
            }

            var severity = string.IsNullOrWhiteSpace(_notice)
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            ShowNotice("oto.iniを作成しました", WithSkip(null), severity);
        }
        catch (Exception ex)
        {
            ShowNotice("oto.iniを作成できませんでした", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            Editor.IsEnabled = true;
            ConfigBody.IsEnabled = true;
            UpdateCommands();
            HideStatus();
        }
    }

    OtoExport CurrentExport() => new(
        OffsetSlider.Value,
        ConsonantSlider.Value,
        BlankSlider.Value,
        OverlapSlider.Value / 100.0);

    void OnConfigChanged()
    {
        OffsetValue.Text = FormatMs(OffsetSlider.Value);
        ConsonantValue.Text = FormatMs(ConsonantSlider.Value);
        BlankValue.Text = FormatSignedSeconds(BlankSlider.Value);
        OverlapValue.Text = FormatPercent(OverlapSlider.Value);
        Editor.SetOtoSettings(CurrentExport());
        if (!_configReady || !_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを作成」を押すと、oto.ini に反映されます。"),
            InfoBarSeverity.Warning);
    }

    void ResetConfig_Click(object sender, RoutedEventArgs e)
    {
        OffsetSlider.Value = DefaultMarginMs;
        ConsonantSlider.Value = DefaultMarginMs;
        BlankSlider.Value = DefaultBlankSeconds;
        OverlapSlider.Value = DefaultOverlapPercent;
    }

    static string FormatMs(double value) =>
        $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)} ms";

    static string FormatPercent(double value) =>
        $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)}%";

    static string FormatSignedSeconds(double value)
    {
        var rounded = Math.Round(value, 3);
        var text = Math.Abs(rounded).ToString("0.000", CultureInfo.InvariantCulture);
        if (rounded > 0)
            return "+" + text + " s";
        if (rounded < 0)
            return "-" + text + " s";
        return "0.000 s";
    }

    void OnEdited()
    {
        _ = SaveCacheAsync();
        if (!_wrote)
            return;
        _wrote = false;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを作成」を押すと、oto.ini に反映されます。"),
            InfoBarSeverity.Warning);
    }

    async Task SaveCacheAsync()
    {
        var folder = _folder;
        if (folder == null || _files.Count == 0)
            return;
        var gen = ++_saveGen;
        try
        {
            _analysis ??= new AnalysisClient();
            await _analysis.SaveCacheAsync(folder, _files);
        }
        catch (Exception ex)
        {
            if (gen != _saveGen || folder != _folder)
                return;
            ShowNotice("保存データを更新できませんでした", ex.Message, InfoBarSeverity.Error);
        }
    }

    async Task<BitmapImage?> DecodePngAsync(byte[] png)
    {
        if (png.Length == 0)
            return null;
        _previewStream?.Dispose();
        var stream = new InMemoryRandomAccessStream();
        _previewStream = stream;
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    void ShowBusy(int done, int total)
    {
        ShowOnly("busy");
        HideStatus();
        ClearNotice();
        BusyTitle.Text = "読み込んでいます";
        SetProgress(done, total);
        FileStatusText.Text = "解析するファイルを準備しています";
        FileProgress.IsIndeterminate = true;
        FileStatusBar.Visibility = Visibility.Visible;
    }

    void UpdateStatus(int done, int total)
    {
        BusyTitle.Text = "解析しています";
        SetProgress(done, total);
    }

    void SetProgress(int done, int total)
    {
        BusyCount.Text = $"{done} / {total} ファイル";
        BusyProgress.IsIndeterminate = false;
        BusyProgress.Maximum = Math.Max(total, 1);
        BusyProgress.Value = done;
    }

    void ShowEditor(string folder, bool cached)
    {
        ShowOnly("editor");
        HideStatus();
        FolderLabel.Text = folder;
        ToolTipService.SetToolTip(FolderLabel, folder);
        FileCount.Text = $"{_files.Count} 件";
        var severity = string.IsNullOrWhiteSpace(_notice)
            ? InfoBarSeverity.Informational
            : InfoBarSeverity.Warning;
        var title = cached ? "保存データから読み込みました" : "区間を確認してください";
        var body = cached
            ? "前回の区間を開きました。直すと保存データへ書き戻します。oto.ini へ反映するには「oto.iniを作成」を押してください。"
            : "位置を直したら「oto.iniを作成」を押してください。区間は OTOasibi_cache.json に保存してあり、次に開くときは再解析しません。";
        ShowNotice(title, WithSkip(body), severity);
        Widen();
        _suppressSelection = true;
        FileList.ItemsSource = null;
        FileList.ItemsSource = _files;
        _suppressSelection = false;
        if (_files.Count > 0)
        {
            FileList.SelectedIndex = 0;
            FileList.Focus(FocusState.Programmatic);
        }
    }

    void ShowError(string message)
    {
        ShowOnly("error");
        HideStatus();
        ClearNotice();
        ErrorText.Text = message;
    }

    void ShowOnly(string view)
    {
        IdleView.Visibility = view == "idle" ? Visibility.Visible : Visibility.Collapsed;
        BusyView.Visibility = view == "busy" ? Visibility.Visible : Visibility.Collapsed;
        EditorView.Visibility = view == "editor" ? Visibility.Visible : Visibility.Collapsed;
        ErrorView.Visibility = view == "error" ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = view == "busy";
        UpdateCommands();
    }

    void UpdateCommands()
    {
        var idle = !_busy;
        var ready = idle && EditorView.Visibility == Visibility.Visible && _files.Count > 0;
        OpenMenuItem.IsEnabled = idle;
        WriteMenuItem.IsEnabled = ready;
        WriteButton.IsEnabled = ready;
        ReanalyzeMenuItem.IsEnabled = ready;
    }

    void UpdateFileStatus(string name, double done, string label)
    {
        var fraction = Math.Clamp(done, 0, 1);
        var percent = (int)Math.Round(fraction * 100);
        FileProgress.IsIndeterminate = false;
        FileProgress.Maximum = 1;
        FileProgress.Value = fraction;
        var detail = string.IsNullOrEmpty(label) ? $"{percent}%" : $"{label}  {percent}%";
        var text = string.IsNullOrEmpty(name) ? detail : $"{name}  {detail}";
        FileStatusText.Text = text;
        ToolTipService.SetToolTip(FileStatusText, text);
        FileStatusBar.Visibility = Visibility.Visible;
    }

    void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusProgress.IsIndeterminate = true;
        StatusBar.Visibility = Visibility.Visible;
        FileStatusBar.Visibility = Visibility.Collapsed;
    }

    void HideStatus()
    {
        StatusBar.Visibility = Visibility.Collapsed;
        StatusProgress.IsIndeterminate = false;
        FileStatusBar.Visibility = Visibility.Collapsed;
        FileProgress.IsIndeterminate = false;
    }

    void ShowNotice(string title, string? body, InfoBarSeverity severity)
    {
        NoticeBar.Title = title;
        NoticeBar.Message = body ?? "";
        NoticeBar.Severity = severity;
        NoticeBar.IsOpen = true;
        NoticeHost.Visibility = Visibility.Visible;
    }

    void ClearNotice()
    {
        NoticeBar.IsOpen = false;
        NoticeHost.Visibility = Visibility.Collapsed;
    }

    string WithSkip(string? body)
    {
        if (string.IsNullOrWhiteSpace(_notice))
            return body ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return _notice;
        return body + "\n" + _notice;
    }

    void ShowFile(WavFile file)
    {
        FileTitle.Text = file.Name;
        ToolTipService.SetToolTip(FileTitle, file.Name);
    }

    void Widen()
    {
        if (App.MainWindow == null)
            return;
        var size = App.MainWindow.AppWindow.Size;
        var width = Math.Max(size.Width, 1540);
        var height = Math.Max(size.Height, 840);
        if (width != size.Width || height != size.Height)
            App.MainWindow.AppWindow.Resize(new SizeInt32(width, height));
    }

    static int CountTargets(string folder)
    {
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    static string? FolderFrom(IReadOnlyList<string> paths)
    {
        var folder = paths.FirstOrDefault(Directory.Exists);
        if (folder != null)
            return folder;
        var file = paths.FirstOrDefault(File.Exists);
        return file == null ? null : Path.GetDirectoryName(file);
    }
}
