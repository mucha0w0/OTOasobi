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
    const string Hint = "境界をドラッグして修正。ラベル上のドラッグで区間を移動。ホイールか「拡大」「縮小」で拡大縮小、右ドラッグでスクロール、ダブルクリックか「全体」で全体表示。";

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

    void ErrorView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _ = PickFolderAsync();
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
        FileCaption.Text = Caption(file);
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
        OpenButton.IsEnabled = false;
        Editor.IsEnabled = false;
        ShowHint("oto.iniを計算しています…");
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.WriteAsync(_folder, _files);
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowBanner(string.IsNullOrEmpty(result.Error) ? "oto.iniを作成できませんでした。" : result.Error);
                ShowHint(Hint);
                return;
            }

            _txtPath = result.Txt;
            _wrote = true;
            OpenButton.IsEnabled = true;
            ShowBanner(WroteMessage());
            ShowHint(Hint);
        }
        catch (Exception ex)
        {
            ShowBanner(ex.Message);
            ShowHint(Hint);
        }
        finally
        {
            _busy = false;
            Editor.IsEnabled = true;
            WriteButton.IsEnabled = _files.Count > 0;
        }
    }

    void OnEdited()
    {
        if (!_wrote)
            return;
        _wrote = false;
        OpenButton.IsEnabled = false;
        _txtPath = null;
        ShowBanner(JoinNotice("画面上の修正は、もう一度「oto.ini作成」を押すと oto.ini に反映されます。"));
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
        IdleView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        StatusProgress.Visibility = Visibility.Visible;
        Grid.SetColumn(StatusText, 1);
        Grid.SetColumnSpan(StatusText, 1);
        StatusText.Margin = new Thickness(16, 0, 0, 0);
        UpdateStatus(done, total);
    }

    void UpdateStatus(int done, int total)
    {
        StatusText.Text = $"{done}（解析完了）/{total}（解析対象）";
        StatusProgress.IsIndeterminate = false;
        StatusProgress.Maximum = Math.Max(total, 1);
        StatusProgress.Value = done;
    }

    void ShowEditor(string folder)
    {
        IdleView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        FolderLabel.Text = folder;
        WriteButton.IsEnabled = true;
        OpenButton.IsEnabled = false;
        ShowBanner(JoinNotice("位置を直したら「oto.ini作成」を押してください。直した区間から oto.ini を計算します。"));
        ShowHint(Hint);
        Widen();
        _suppressSelection = true;
        FileList.ItemsSource = null;
        FileList.ItemsSource = _files;
        _suppressSelection = false;
        if (_files.Count > 0)
            FileList.SelectedIndex = 0;
    }

    void ShowError(string message)
    {
        IdleView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        ErrorView.Text = message;
        ErrorView.Visibility = Visibility.Visible;
    }

    void ShowHint(string text)
    {
        StatusBar.Visibility = Visibility.Visible;
        StatusProgress.Visibility = Visibility.Collapsed;
        Grid.SetColumn(StatusText, 0);
        Grid.SetColumnSpan(StatusText, 2);
        StatusText.Margin = new Thickness(0);
        StatusText.Text = text;
    }

    void ShowBanner(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Banner.Text = "";
            BannerHost.Visibility = Visibility.Collapsed;
            return;
        }

        Banner.Text = text;
        BannerHost.Visibility = Visibility.Visible;
    }

    string WroteMessage()
    {
        return JoinNotice("oto.iniを作成しました。");
    }

    string JoinNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(_notice))
            return message;
        return _notice + "\n" + message;
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

    static string Caption(WavFile file)
    {
        return $"{file.Name}    {EngineLabel(file.Engine)}    {file.Duration:0.00} 秒";
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
