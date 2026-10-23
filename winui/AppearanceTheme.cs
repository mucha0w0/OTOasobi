using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Otoasobi;

// 文字と地の色は、起動時の Application.RequestedTheme が選んだ辞書に従う。
// ウィンドウの RequestedTheme はそれと同じ明暗にする。ずらすと、暗い地に暗い文字が残る。
// 明暗を変えたときは、保存してから起動し直す。アクセントは、使っている辞書のブラシの色だけを差し替える。
static class AppearanceTheme
{
    const string BaseAccentKey = "SystemAccentColor";

    static readonly HashSet<SolidColorBrush> Overridden = [];
    static readonly Dictionary<SolidColorBrush, Color> SavedColor = [];
    static bool _baseOverride;

    public static void ApplyTo(Window window)
    {
        var theme = MatchingElementTheme();
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = theme;
        ApplyCaption(window, theme);
    }

    public static void ApplyAccent()
    {
        if (Application.Current == null)
            return;
        if (AppAppearance.Accent is RgbColor accent)
            Paint(AccentPalette.Create(accent));
        else
            Restore();
        FixDisabledInk();
    }

    public static void UseTheme(AppThemeChoice theme)
    {
        if (AppAppearance.Theme == theme)
            return;
        AppAppearance.SaveTheme(theme);
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null)
            Relaunch();
        else
            queue.TryEnqueue(Relaunch);
    }

    public static void UseAccent(RgbColor? accent)
    {
        if (AppAppearance.Accent == accent)
            return;
        AppAppearance.SaveAccent(accent);
        ApplyAccent();
        AppAppearance.RaiseChanged();
    }

    // Default を代入すると、端末がライトでもダークになる。リソースと同じ明暗を明示する。
    static ElementTheme MatchingElementTheme()
    {
        if (AppAppearance.Theme == AppThemeChoice.Light)
            return ElementTheme.Light;
        if (AppAppearance.Theme == AppThemeChoice.Dark)
            return ElementTheme.Dark;
        return Application.Current?.RequestedTheme == ApplicationTheme.Light
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    static bool _relaunching;

    static void Relaunch()
    {
        if (_relaunching)
            return;
        _relaunching = true;
        if (App.MainWindow is MainWindow window)
            window.FlushContent();
        MainPage.ShutdownAnalysis();
        SettingsWindow.CloseIfOpen();
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            _relaunching = false;
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            });
        }
        catch (Exception)
        {
            _relaunching = false;
            return;
        }

        Application.Current.Exit();
    }

    static void Paint(AccentPalette.Shades shades)
    {
        Restore();
        SetBase(shades.Base);
        var seen = new HashSet<ResourceDictionary>();
        Walk(Application.Current.Resources, null, shades, seen);
    }

    static void Walk(ResourceDictionary dictionary, string? theme, AccentPalette.Shades shades, HashSet<ResourceDictionary> seen)
    {
        if (!seen.Add(dictionary))
            return;
        foreach (var pair in dictionary.ThemeDictionaries)
        {
            if (pair.Value is not ResourceDictionary child)
                continue;
            var name = ThemeKey(pair.Key);
            if (name == "HighContrast")
                continue;
            var next = name is "Light" or "Dark" or "Default" ? name : theme;
            Walk(child, next, shades, seen);
        }

        if (theme is "Light" or "Dark" or "Default")
            PaintDictionary(dictionary, theme == "Light", shades);
        foreach (var merged in dictionary.MergedDictionaries)
            Walk(merged, theme, shades, seen);
    }

    static void PaintDictionary(ResourceDictionary dictionary, bool light, AccentPalette.Shades shades)
    {
        if (!light)
        {
            var fill = shades.Light2;
            var ink = AccentPalette.PrefersDarkText(fill) ? new RgbColor(0, 0, 0) : new RgbColor(255, 255, 255);
            var selectedInk = AccentPalette.PrefersDarkText(shades.Base) ? new RgbColor(0, 0, 0) : new RgbColor(255, 255, 255);
            SetBrush(dictionary, "AccentFillColorDefaultBrush", shades.Light2, keepAlpha: false, light);
            SetBrush(dictionary, "AccentFillColorSecondaryBrush", shades.Light2, keepAlpha: false, light);
            SetBrush(dictionary, "AccentFillColorTertiaryBrush", shades.Light2, keepAlpha: false, light);
            SetBrush(dictionary, "AccentTextFillColorPrimaryBrush", shades.Light3, keepAlpha: false, light);
            SetBrush(dictionary, "AccentTextFillColorSecondaryBrush", shades.Light3, keepAlpha: false, light);
            SetBrush(dictionary, "AccentTextFillColorTertiaryBrush", shades.Light2, keepAlpha: false, light);
            SetBrush(dictionary, "AccentFillColorSelectedTextBackgroundBrush", shades.Base, keepAlpha: false, light);
            SetBrush(dictionary, "TextOnAccentFillColorPrimaryBrush", ink, keepAlpha: true, light);
            SetBrush(dictionary, "TextOnAccentFillColorSecondaryBrush", ink, keepAlpha: true, light);
            SetBrush(dictionary, "TextOnAccentFillColorSelectedTextBrush", selectedInk, keepAlpha: true, light);
            return;
        }

        var paint = AccentPalette.ForSurface(shades.Base, lightSurface: true);
        SetBrush(dictionary, "AccentFillColorDefaultBrush", paint.Fill, keepAlpha: false, light);
        SetBrush(dictionary, "AccentFillColorSecondaryBrush", paint.Fill, keepAlpha: false, light);
        SetBrush(dictionary, "AccentFillColorTertiaryBrush", paint.Fill, keepAlpha: false, light);
        SetBrush(dictionary, "AccentTextFillColorPrimaryBrush", paint.AccentText, keepAlpha: false, light);
        SetBrush(dictionary, "AccentTextFillColorSecondaryBrush", paint.AccentText, keepAlpha: false, light);
        SetBrush(dictionary, "AccentTextFillColorTertiaryBrush", paint.AccentText, keepAlpha: false, light);
        SetBrush(dictionary, "AccentFillColorSelectedTextBackgroundBrush", paint.Fill, keepAlpha: false, light);
        SetBrush(dictionary, "TextOnAccentFillColorPrimaryBrush", paint.Ink, keepAlpha: true, light);
        SetBrush(dictionary, "TextOnAccentFillColorSecondaryBrush", paint.Ink, keepAlpha: true, light);
        SetBrush(dictionary, "TextOnAccentFillColorSelectedTextBrush", paint.Ink, keepAlpha: true, light);
    }

    static void SetBrush(ResourceDictionary dictionary, string key, RgbColor rgb, bool keepAlpha, bool lightFormula)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not SolidColorBrush brush)
            return;
        if (lightFormula != ActiveIsLight
            && Application.Current.Resources.TryGetValue(key, out var active)
            && ReferenceEquals(active, brush))
            return;
        var alpha = (byte)255;
        if (keepAlpha)
            alpha = SavedColor.TryGetValue(brush, out var saved) ? saved.A : brush.Color.A;
        Override(brush, Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B), keepAlpha);
    }

    static void Override(SolidColorBrush brush, Color color, bool restoreExact)
    {
        if (Overridden.Add(brush) && restoreExact)
            SavedColor[brush] = brush.Color;
        brush.Color = color;
    }

    static void SetBase(RgbColor color)
    {
        Application.Current.Resources[BaseAccentKey] = Color.FromArgb(255, color.R, color.G, color.B);
        _baseOverride = true;
    }

    static void Restore()
    {
        if (Application.Current != null && _baseOverride)
        {
            Application.Current.Resources.Remove(BaseAccentKey);
            _baseOverride = false;
        }

        foreach (var brush in Overridden)
        {
            if (SavedColor.TryGetValue(brush, out var color))
                brush.Color = color;
            else
                brush.ClearValue(SolidColorBrush.ColorProperty);
        }

        Overridden.Clear();
        SavedColor.Clear();
    }

    static readonly Dictionary<ResourceDictionary, SolidColorBrush> DisabledInks = [];

    static bool ActiveIsLight => Application.Current?.RequestedTheme == ApplicationTheme.Light;

    // 無効なアクセントボタンは、色そのものを前景にしているので白地で消える。明るい辞書だけ、辞書ごとに別のブラシを置く。
    static void FixDisabledInk()
    {
        if (Application.Current == null)
            return;
        VisitThemes(Application.Current.Resources, ApplyDisabledInk, []);
    }

    static void ApplyDisabledInk(ResourceDictionary dictionary, bool light)
    {
        if (!light || !dictionary.ContainsKey("AccentButtonForegroundDisabled"))
            return;
        var color = Color.FromArgb(0x9E, 0, 0, 0);
        if (!DisabledInks.TryGetValue(dictionary, out var ink))
        {
            ink = new SolidColorBrush(color);
            try
            {
                dictionary["AccentButtonForegroundDisabled"] = ink;
            }
            catch (Exception)
            {
                return;
            }

            DisabledInks[dictionary] = ink;
        }
        else
        {
            ink.Color = color;
        }

        try
        {
            if (dictionary.ContainsKey("TextOnAccentFillColorDisabled"))
                dictionary["TextOnAccentFillColorDisabled"] = color;
        }
        catch (Exception)
        {
            // 色キーを持たない辞書は、上のブラシだけで足りる。
        }

        if (dictionary.TryGetValue("TextOnAccentFillColorDisabledBrush", out var value) && value is SolidColorBrush brush)
            Override(brush, color, restoreExact: true);
    }

    public static ElementTheme Resolve(FrameworkElement element)
    {
        if (element.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            return element.ActualTheme;
        return Application.Current?.RequestedTheme == ApplicationTheme.Light
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    public static Brush? Brush(ElementTheme theme, string key)
    {
        if (Application.Current == null)
            return null;
        var names = theme == ElementTheme.Light ? new[] { "Light" } : new[] { "Dark", "Default" };
        foreach (var name in names)
        {
            var found = FindBrush(Application.Current.Resources, name, key, []);
            if (found != null)
                return found;
        }

        return null;
    }

    static Brush? FindBrush(ResourceDictionary dictionary, string themeName, string key, HashSet<ResourceDictionary> seen)
    {
        if (!seen.Add(dictionary))
            return null;
        foreach (var pair in dictionary.ThemeDictionaries)
        {
            if (pair.Value is not ResourceDictionary child)
                continue;
            var name = ThemeKey(pair.Key);
            if (name == themeName)
            {
                if (child.TryGetValue(key, out var value) && value is Brush brush)
                    return brush;
            }

            if (name is not ("Light" or "Dark" or "Default" or "HighContrast"))
            {
                var nested = FindBrush(child, themeName, key, seen);
                if (nested != null)
                    return nested;
            }
            else if (name == themeName)
            {
                var nested = FindBrush(child, themeName, key, seen);
                if (nested != null)
                    return nested;
            }
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            var nested = FindBrush(merged, themeName, key, seen);
            if (nested != null)
                return nested;
        }

        return null;
    }

    static void VisitThemes(ResourceDictionary dictionary, Action<ResourceDictionary, bool> visit, HashSet<ResourceDictionary> seen)
    {
        if (!seen.Add(dictionary))
            return;
        foreach (var pair in dictionary.ThemeDictionaries)
        {
            if (pair.Value is not ResourceDictionary child)
                continue;
            var name = ThemeKey(pair.Key);
            if (name is "Light" or "Default")
                visit(child, name == "Light");
            if (name != "HighContrast")
                VisitThemes(child, visit, seen);
        }

        foreach (var merged in dictionary.MergedDictionaries)
            VisitThemes(merged, visit, seen);
    }

    static void ApplyCaption(Window window, ElementTheme theme)
    {
        var bar = window.AppWindow.TitleBar;
        if (theme != ElementTheme.Light)
        {
            bar.ButtonForegroundColor = null;
            bar.ButtonHoverForegroundColor = null;
            bar.ButtonPressedForegroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            bar.ButtonBackgroundColor = null;
            bar.ButtonHoverBackgroundColor = null;
            bar.ButtonPressedBackgroundColor = null;
            bar.ButtonInactiveBackgroundColor = null;
            return;
        }

        var foreground = Color.FromArgb(255, 32, 32, 32);
        var inactive = Color.FromArgb(255, 140, 140, 140);
        var hover = Color.FromArgb(28, 0, 0, 0);
        var pressed = Color.FromArgb(18, 0, 0, 0);
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    static string? ThemeKey(object key) => key switch
    {
        string name => name,
        ElementTheme theme => theme.ToString(),
        _ => null,
    };
}
