using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Otoasobi.Probe;

public sealed partial class MainWindow : Window
{
    readonly ProbeSession _session;

    public MainWindow()
    {
        InitializeComponent();
        Title = "オトアソビ試作";
        AppWindow.Title = "オトアソビ試作";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 960;
            presenter.PreferredMinimumHeight = 640;
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        _session = new ProbeSession(this, View, ShowMissing);
        Closed += (_, _) => _session.Dispose();
        _ = _session.AttachAsync();
    }

    public void PaintCaption(bool dark)
    {
        var bar = AppWindow.TitleBar;
        var foreground = dark ? Color.FromArgb(255, 245, 245, 245) : Color.FromArgb(255, 32, 32, 32);
        var inactive = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 140, 140, 140);
        var hover = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);
        var pressed = dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        var ground = dark ? Color.FromArgb(255, 22, 22, 22) : Color.FromArgb(255, 244, 244, 245);
        DragRegion.Background = new SolidColorBrush(ground);
        CaptionText.Foreground = new SolidColorBrush(foreground);
        CaptionText.Text = dark ? "オトアソビ試作 · ダーク" : "オトアソビ試作 · ライト";
    }

    void ShowMissing(string message)
    {
        View.Visibility = Visibility.Collapsed;
        MissingRuntime.Text = message;
        MissingRuntime.Visibility = Visibility.Visible;
    }
}
