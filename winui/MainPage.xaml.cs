using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

namespace Otoasobi;

public sealed partial class MainPage : Page
{
    static AnalysisClient? _analysis;

    string? _txtPath;
    bool _busy;

    public MainPage()
    {
        InitializeComponent();
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

    async Task RunAsync(string folderPath)
    {
        _busy = true;
        var total = CountTargets(folderPath);
        ShowBusy(0, total);
        var queue = DispatcherQueue;
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.AnalyzeAsync(folderPath, (done, count) =>
            {
                queue.TryEnqueue(() =>
                {
                    if (_busy)
                        UpdateStatus(done, count);
                });
            });
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowError(string.IsNullOrEmpty(result.Error) ? "解析に失敗しました。" : result.Error);
                return;
            }

            _txtPath = result.Txt;
            ShowDone(result.Notice);
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

    void ShowBusy(int done, int total)
    {
        IdleView.Visibility = Visibility.Collapsed;
        DoneView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        UpdateStatus(done, total);
    }

    void UpdateStatus(int done, int total)
    {
        StatusText.Text = $"{done}（解析完了）/{total}（解析対象）";
        StatusProgress.IsIndeterminate = false;
        StatusProgress.Maximum = Math.Max(total, 1);
        StatusProgress.Value = done;
    }

    void ShowDone(string? notice)
    {
        IdleView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(notice))
        {
            SkipNotice.Text = "";
            SkipNoticeHost.Visibility = Visibility.Collapsed;
        }
        else
        {
            SkipNotice.Text = notice;
            SkipNoticeHost.Visibility = Visibility.Visible;
        }

        DoneView.Visibility = Visibility.Visible;
    }

    void ShowError(string message)
    {
        IdleView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Collapsed;
        DoneView.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        ErrorView.Text = message;
        ErrorView.Visibility = Visibility.Visible;
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
