using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Otoasobi;

// 明暗はウィンドウの RequestedTheme。アプリの RequestedTheme は起動時にしか合わせられない。
// 起動後に明暗を変えたときは、画面が参照しているブラシへ、選んだ明暗の色を書き戻す。
// アクセントは、そのブラシの色だけを差し替える。新しいインスタンスに取り替えると、StaticResource 側が古いまま残る。
static class AppearanceTheme
{
    const string BaseAccentKey = "SystemAccentColor";

    static readonly HashSet<SolidColorBrush> Overridden = [];
    static readonly Dictionary<SolidColorBrush, Color> SavedColor = [];
    static bool _baseOverride;

    public static void ApplyTo(Window window)
    {
        var theme = ToElement(AppAppearance.Theme);
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = theme;
        ApplyCaption(window, theme);
    }

    public static void ApplyThemes()
    {
        if (App.MainWindow is Window main)
            ApplyTo(main);
        SettingsWindow.ApplyThemeIfOpen();
    }

    public static void ApplyAccent()
    {
        if (Application.Current == null)
            return;
        EnsureSnapshots();
        if (AppAppearance.Accent is RgbColor accent)
            Paint(AccentPalette.Create(accent));
        else
        {
            Restore();
            RetintActive();
        }

        FixDisabledInk();
    }

    public static void UseTheme(AppThemeChoice theme)
    {
        if (AppAppearance.Theme == theme)
            return;
        AppAppearance.SaveTheme(theme);
        ApplyThemes();
        ApplyAccent();
    }

    public static void UseAccent(RgbColor? accent)
    {
        if (AppAppearance.Accent == accent)
            return;
        AppAppearance.SaveAccent(accent);
        ApplyAccent();
        AppAppearance.RaiseChanged();
    }

    static ElementTheme ToElement(AppThemeChoice theme) => theme switch
    {
        AppThemeChoice.Light => ElementTheme.Light,
        AppThemeChoice.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    static void Paint(AccentPalette.Shades shades)
    {
        Restore();
        RetintActive();
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

    static void PaintDictionary(ResourceDictionary dictionary, bool dictIsLight, AccentPalette.Shades shades)
    {
        var light = dictIsLight;
        if (WantsLight() != ActiveIsLight && OwnsActive(dictionary))
            light = WantsLight();
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
        if (lightFormula != WantsLight()
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

    static Dictionary<string, Color>? _lightColors;
    static Dictionary<string, Color>? _darkColors;
    static bool _awayFromStartup;
    static readonly Dictionary<ResourceDictionary, object> DisabledOriginal = [];
    static readonly Dictionary<ResourceDictionary, object> DisabledColorOriginal = [];
    static readonly Dictionary<ResourceDictionary, SolidColorBrush> DisabledInks = [];

    static bool ActiveIsLight => Application.Current?.RequestedTheme == ApplicationTheme.Light;

    static bool WantsLight() => AppAppearance.Theme switch
    {
        AppThemeChoice.Light => true,
        AppThemeChoice.Dark => false,
        _ => new UISettings().GetColorValue(UIColorType.Background).R > 128,
    };

    static bool OwnsActive(ResourceDictionary dictionary)
    {
        if (!dictionary.TryGetValue("TextFillColorPrimaryBrush", out var local))
            return false;
        return Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var active)
            && ReferenceEquals(local, active);
    }

    static void EnsureSnapshots()
    {
        if (_lightColors != null || Application.Current == null)
            return;
        _lightColors = [];
        _darkColors = [];
        CaptureThemes(Application.Current.Resources, []);
    }

    static void CaptureThemes(ResourceDictionary dictionary, HashSet<ResourceDictionary> seen)
    {
        if (!seen.Add(dictionary))
            return;
        foreach (var pair in dictionary.ThemeDictionaries)
        {
            if (pair.Value is not ResourceDictionary child)
                continue;
            var name = ThemeKey(pair.Key);
            if (name == "Light")
                ReadBrushes(child, _lightColors!);
            else if (name is "Default" or "Dark")
                ReadBrushes(child, _darkColors!);
            if (name != "HighContrast")
                CaptureThemes(child, seen);
        }

        foreach (var merged in dictionary.MergedDictionaries)
            CaptureThemes(merged, seen);
    }

    static void ReadBrushes(ResourceDictionary dictionary, Dictionary<string, Color> target)
    {
        try
        {
            foreach (var entry in dictionary)
            {
                if (entry.Key is string key && entry.Value is SolidColorBrush brush && !target.ContainsKey(key))
                    target[key] = brush.Color;
            }
        }
        catch (Exception)
        {
            // 列挙できない辞書は、起動時の色のままにする。
        }
    }

    // 画面の文字や地は、起動時のテーマ辞書のブラシを掴んだままになる。選んだ明暗の色をそこへ写す。
    static void RetintActive()
    {
        if (Application.Current == null || _lightColors == null || _darkColors == null)
            return;
        var away = WantsLight() != ActiveIsLight;
        if (!away && !_awayFromStartup)
            return;
        var source = WantsLight() ? _lightColors : _darkColors;
        var resources = Application.Current.Resources;
        foreach (var (key, color) in source)
        {
            if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush && brush.Color != color)
            {
                try
                {
                    brush.Color = color;
                }
                catch (Exception)
                {
                    // 凍結されたブラシは、その項目だけ元の色のままにする。
                }
            }
        }

        _awayFromStartup = away;
    }

    // 無効なアクセントボタンは、色そのものを前景にしているので白地で消える。辞書ごとに別のブラシを置く。
    static void FixDisabledInk()
    {
        if (Application.Current == null)
            return;
        VisitThemes(Application.Current.Resources, (dictionary, _) => ApplyDisabledInk(dictionary), []);
    }

    static void ApplyDisabledInk(ResourceDictionary dictionary)
    {
        const string key = "AccentButtonForegroundDisabled";
        const string colorKey = "TextOnAccentFillColorDisabled";
        if (!WantsLight())
        {
            if (DisabledOriginal.TryGetValue(dictionary, out var original))
            {
                try
                {
                    dictionary[key] = original;
                }
                catch (Exception)
                {
                    // 元の項目を戻せない辞書は、次の明暗の切り替えで再度置く。
                }
            }

            if (DisabledColorOriginal.TryGetValue(dictionary, out var savedColor))
            {
                try
                {
                    dictionary[colorKey] = savedColor;
                }
                catch (Exception)
                {
                    // 色キーを持たない辞書は、ブラシの戻しだけで足りる。
                }
            }

            return;
        }

        if (!dictionary.ContainsKey(key))
            return;
        var color = Color.FromArgb(0x9E, 0, 0, 0);
        if (!DisabledOriginal.ContainsKey(dictionary))
        {
            try
            {
                DisabledOriginal[dictionary] = dictionary[key];
            }
            catch (Exception)
            {
                return;
            }
        }

        if (!DisabledInks.TryGetValue(dictionary, out var ink))
        {
            ink = new SolidColorBrush(color);
            try
            {
                dictionary[key] = ink;
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
            if (dictionary.ContainsKey(colorKey))
            {
                if (!DisabledColorOriginal.ContainsKey(dictionary))
                    DisabledColorOriginal[dictionary] = dictionary[colorKey];
                dictionary[colorKey] = color;
            }
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
