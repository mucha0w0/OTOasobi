using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Otoasobi;

public sealed partial class DisplaySettings : UserControl
{
    readonly UISettings _ui = new();
    readonly DispatcherQueue? _queue;
    readonly DispatcherQueueTimer? _accentTimer;
    bool _ready;
    bool _writing;
    bool _accentPending;

    public DisplaySettings()
    {
        InitializeComponent();
        _writing = true;
        switch (AppAppearance.Theme)
        {
            case AppThemeChoice.Light:
                ThemeLight.IsChecked = true;
                break;
            case AppThemeChoice.Dark:
                ThemeDark.IsChecked = true;
                break;
            default:
                ThemeSystem.IsChecked = true;
                break;
        }

        if (AppAppearance.Accent is RgbColor accent)
        {
            AccentCustom.IsChecked = true;
            AccentPicker.Color = ToColor(accent);
        }
        else
        {
            AccentSystem.IsChecked = true;
            AccentPicker.Color = ReadSystemAccent();
        }

        _writing = false;
        _queue = DispatcherQueue.GetForCurrentThread();
        if (_queue != null)
        {
            _accentTimer = _queue.CreateTimer();
            _accentTimer.IsRepeating = false;
            _accentTimer.Interval = TimeSpan.FromMilliseconds(200);
            _accentTimer.Tick += (_, _) => CommitAccent();
        }

        _ui.ColorValuesChanged += OnSystemColorChanged;
        Unloaded += (_, _) =>
        {
            _ui.ColorValuesChanged -= OnSystemColorChanged;
            Flush();
        };
        _ready = true;
    }

    public void Flush()
    {
        _accentTimer?.Stop();
        if (_accentPending)
            CommitAccent();
    }

    void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_writing || !_ready)
            return;
        var theme = ThemeLight.IsChecked == true
            ? AppThemeChoice.Light
            : ThemeDark.IsChecked == true
                ? AppThemeChoice.Dark
                : AppThemeChoice.System;
        AppearanceTheme.UseTheme(theme);
    }

    void AccentMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_writing || !_ready)
            return;
        if (AccentSystem.IsChecked == true)
        {
            _accentPending = false;
            _accentTimer?.Stop();
            AppearanceTheme.UseAccent(null);
            _writing = true;
            AccentPicker.Color = ReadSystemAccent();
            _writing = false;
            return;
        }

        AppearanceTheme.UseAccent(Pick());
    }

    void AccentPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_writing || !_ready)
            return;
        if (AccentCustom.IsChecked != true)
        {
            _writing = true;
            AccentCustom.IsChecked = true;
            _writing = false;
        }

        ScheduleAccent();
    }

    void ScheduleAccent()
    {
        _accentPending = true;
        if (_accentTimer == null)
        {
            CommitAccent();
            return;
        }

        _accentTimer.Start();
    }

    void CommitAccent()
    {
        if (!_accentPending || AccentCustom.IsChecked != true)
        {
            _accentPending = false;
            return;
        }

        _accentPending = false;
        AppearanceTheme.UseAccent(Pick());
    }

    void OnSystemColorChanged(UISettings sender, object args)
    {
        _queue?.TryEnqueue(ShowSystemAccent);
    }

    void ShowSystemAccent()
    {
        if (_writing || AccentSystem.IsChecked != true)
            return;
        _writing = true;
        AccentPicker.Color = ReadSystemAccent();
        _writing = false;
    }

    RgbColor Pick() => new(AccentPicker.Color.R, AccentPicker.Color.G, AccentPicker.Color.B);

    static Color ToColor(RgbColor color) => Color.FromArgb(255, color.R, color.G, color.B);

    static Color ReadSystemAccent() => new UISettings().GetColorValue(UIColorType.Accent);
}
