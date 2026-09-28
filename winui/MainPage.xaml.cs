using System.Diagnostics;
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
    string? _txtPath;
    string? _notice;
    bool _busy;
    bool _wrote;
    bool _suppressSelection;
    int _previewGen;
    IDisposable? _previewStream;

    public MainPage()
    {
        InitializeComponent();
        Editor.Edited += (_, _) => OnEdited();
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPageWheel), true);
        // メインキーボードの - と +。この SDK の VirtualKey には名前が無い。
        ZoomOutButton.KeyboardAccelerators.Add(Key((Windows.System.VirtualKey)189));
        ZoomInButton.KeyboardAccelerators.Add(Key((Windows.System.VirtualKey)187));
    }

    static KeyboardAccelerator Key(Windows.System.VirtualKey key)
    {
        return new KeyboardAccelerator
        {
            Key = key,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
    }

    void ZoomIn_Click(object sender, RoutedEventArgs e) => Editor.ZoomIn();

    void ZoomOut_Click(object sender, RoutedEventArgs e) => Editor.ZoomOut();

    void ZoomReset_Click(object sender, RoutedEventArgs e) => Editor.ZoomReset();

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

        _ = RunAsync(folder);
    }

    public void ChooseFolder()
    {
        _ = PickFolderAsync();
    }

    void IdleView_Click(object sender, RoutedEventArgs e)
    {
        _ = PickFolderAsync();
    }

    void OpenFolder_Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = PickFolderAsync();
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

    void OpenTxt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_txtPath))
            return;
        Process.Start(new ProcessStartInfo(_txtPath) { UseShellExecute = true });
    }

    void WriteOto_Click(object sender, RoutedEventArgs e)
    {
        _ = WriteAsync();
    }

    void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;
        if (FileList.SelectedItem is not WavFile file)
            return;
        _ = LoadPreviewAsync(file);
    }

    async Task RunAsync(string folderPath)
    {
        _busy = true;
        _folder = folderPath;
        _files.Clear();
        _txtPath = null;
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
            });
            if (!result.Ok || result.Files.Count == 0)
            {
                ShowError(string.IsNullOrEmpty(result.Error) ? "解析に失敗しました。" : result.Error);
                return;
            }

            _notice = result.Notice;
            _files.AddRange(result.Files);
            ShowEditor(folderPath);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    async Task LoadPreviewAsync(WavFile file)
    {
        if (_folder == null)
            return;
        var gen = ++_previewGen;
        ShowFile(file);
        Editor.Clear("波形を読み込んでいます…");
        try
        {
            _analysis ??= new AnalysisClient();
            var preview = await _analysis.PreviewAsync(Path.Combine(_folder, file.Name));
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
            Editor.SetFile(file, image, preview.SpecT0, preview.SpecT1, preview.Lo, preview.Hi, preview.Loudness);
        }
        catch (Exception ex)
        {
            if (gen == _previewGen)
                Editor.Clear(ex.Message);
        }
    }

    async Task WriteAsync()
    {
        if (_busy || _folder == null || _files.Count == 0)
            return;
        _busy = true;
        WriteButton.IsEnabled = false;
        SetOpenEnabled(false);
        Editor.IsEnabled = false;
        ShowStatus("oto.iniを計算しています");
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteAsync(_folder, _files);
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowNotice(
                    "oto.iniを作成できませんでした",
                    string.IsNullOrEmpty(result.Error) ? null : result.Error,
                    InfoBarSeverity.Error);
                return;
            }

            _txtPath = result.Txt;
            _wrote = true;
            SetOpenEnabled(true);
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
            WriteButton.IsEnabled = _files.Count > 0;
            HideStatus();
        }
    }

    void OnEdited()
    {
        if (!_wrote)
            return;
        _wrote = false;
        SetOpenEnabled(false);
        _txtPath = null;
        ShowNotice(
            "修正はまだファイルに入っていません",
            WithSkip("もう一度「oto.iniを作成」を押すと、oto.ini に反映されます。"),
            InfoBarSeverity.Warning);
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
        UpdateStatus(done, total);
    }

    void UpdateStatus(int done, int total)
    {
        BusyCount.Text = $"{done} / {total} ファイル";
        BusyProgress.IsIndeterminate = false;
        BusyProgress.Maximum = Math.Max(total, 1);
        BusyProgress.Value = done;
    }

    void ShowEditor(string folder)
    {
        ShowOnly("editor");
        HideStatus();
        FolderLabel.Text = folder;
        ToolTipService.SetToolTip(FolderLabel, folder);
        FileCount.Text = $"{_files.Count} 件";
        WriteButton.IsEnabled = true;
        SetOpenEnabled(false);
        var severity = string.IsNullOrWhiteSpace(_notice)
            ? InfoBarSeverity.Informational
            : InfoBarSeverity.Warning;
        ShowNotice(
            "区間を確認してください",
            WithSkip("直した区間から計算します。位置を直したら「oto.iniを作成」を押してください。"),
            severity);
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
    }

    void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusProgress.IsIndeterminate = true;
        StatusBar.Visibility = Visibility.Visible;
    }

    void HideStatus()
    {
        StatusBar.Visibility = Visibility.Collapsed;
        StatusProgress.IsIndeterminate = false;
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

    void SetOpenEnabled(bool enabled)
    {
        OpenButton.IsEnabled = enabled;
        ToolTipService.SetToolTip(
            OpenButton,
            enabled ? "作成した oto.ini を開きます。" : "「oto.iniを作成」を押すと開きます。");
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
        FileMeta.Text = $"{EngineLabel(file.Engine)}  ·  {file.Duration:0.00} 秒";
    }

    void Widen()
    {
        if (App.MainWindow == null)
            return;
        var size = App.MainWindow.AppWindow.Size;
        var width = Math.Max(size.Width, 1280);
        var height = Math.Max(size.Height, 840);
        if (width != size.Width || height != size.Height)
            App.MainWindow.AppWindow.Resize(new SizeInt32(width, height));
    }

    static string EngineLabel(string engine)
    {
        return engine switch
        {
            "mms+praat" => "高精度 (MMS+Praat)",
            "mms" => "高精度 (MMS)",
            "praat" => "Praat+音響",
            "acoustic" => "音響",
            _ => engine,
        };
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
