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

    public void OpenAudio(IReadOnlyList<string> paths)
    {
        if (_busy)
            return;
        var wav = paths.FirstOrDefault(IsWav);
        if (wav == null)
        {
            ShowError("WAVファイルを選んでください。");
            return;
        }

        _ = RunAsync(wav);
    }

    void IdleView_Click(object sender, RoutedEventArgs e)
    {
        _ = PickFileAsync();
    }

    void ErrorView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _ = PickFileAsync();
    }

    async Task PickFileAsync()
    {
        if (_busy || App.MainWindow == null)
            return;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        var path = await WavPicker.PickAsync(hwnd);
        if (path != null)
            OpenAudio([path]);
    }

    void OpenTxt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_txtPath))
            return;
        Process.Start(new ProcessStartInfo(_txtPath) { UseShellExecute = true });
    }

    async Task RunAsync(string wavPath)
    {
        _busy = true;
        ShowBusy();
        try
        {
            _analysis ??= new AnalysisClient();
            var result = await _analysis.AnalyzeAsync(wavPath);
            if (!result.Ok || string.IsNullOrEmpty(result.Txt))
            {
                ShowError(string.IsNullOrEmpty(result.Error) ? "解析に失敗しました。" : result.Error);
                return;
            }

            _txtPath = result.Txt;
            ShowDone();
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

    void ShowBusy()
    {
        IdleView.Visibility = Visibility.Collapsed;
        DoneView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Visible;
    }

    void ShowDone()
    {
        IdleView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        DoneView.Visibility = Visibility.Visible;
    }

    void ShowError(string message)
    {
        IdleView.Visibility = Visibility.Collapsed;
        BusyView.Visibility = Visibility.Collapsed;
        DoneView.Visibility = Visibility.Collapsed;
        ErrorView.Text = message;
        ErrorView.Visibility = Visibility.Visible;
    }

    static bool IsWav(string path)
    {
        return path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
    }
}
