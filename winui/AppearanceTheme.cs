using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Otoasobi;

// 明暗はウィンドウの RequestedTheme。アクセントは、ボタンが参照しているブラシの色だけを差し替える。
// ブラシを新しいインスタンスに取り替えると、StaticResource で同じブラシを指している側が古いまま残る。
static class AppearanceTheme
{
    const string BaseAccentKey = "SystemAccentColor";

    static readonly HashSet<SolidColorBrush> Overridden = [];
    static readonly Dictionary<SolidColorBrush, Color> SavedColor = [];
    static bool _baseOverride;

    public static void ApplyTo(Window window)
    {
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = ToElement(AppAppearance.Theme);
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
        if (AppAppearance.Accent is RgbColor accent)
            Paint(AccentPalette.Create(accent));
        else
            Restore();
    }

    public static void UseTheme(AppThemeChoice theme)
    {
        if (AppAppearance.Theme == theme)
            return;
        AppAppearance.SaveTheme(theme);
        ApplyThemes();
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
        var fill = light ? shades.Dark1 : shades.Light2;
        var ink = AccentPalette.PrefersDarkText(fill) ? new RgbColor(0, 0, 0) : new RgbColor(255, 255, 255);
        var selectedInk = AccentPalette.PrefersDarkText(shades.Base) ? new RgbColor(0, 0, 0) : new RgbColor(255, 255, 255);
        if (light)
        {
            SetBrush(dictionary, "AccentFillColorDefaultBrush", shades.Dark1, keepAlpha: false);
            SetBrush(dictionary, "AccentFillColorSecondaryBrush", shades.Dark1, keepAlpha: false);
            SetBrush(dictionary, "AccentFillColorTertiaryBrush", shades.Dark1, keepAlpha: false);
            SetBrush(dictionary, "AccentTextFillColorPrimaryBrush", shades.Dark2, keepAlpha: false);
            SetBrush(dictionary, "AccentTextFillColorSecondaryBrush", shades.Dark3, keepAlpha: false);
            SetBrush(dictionary, "AccentTextFillColorTertiaryBrush", shades.Dark1, keepAlpha: false);
        }
        else
        {
            SetBrush(dictionary, "AccentFillColorDefaultBrush", shades.Light2, keepAlpha: false);
            SetBrush(dictionary, "AccentFillColorSecondaryBrush", shades.Light2, keepAlpha: false);
            SetBrush(dictionary, "AccentFillColorTertiaryBrush", shades.Light2, keepAlpha: false);
            SetBrush(dictionary, "AccentTextFillColorPrimaryBrush", shades.Light3, keepAlpha: false);
            SetBrush(dictionary, "AccentTextFillColorSecondaryBrush", shades.Light3, keepAlpha: false);
            SetBrush(dictionary, "AccentTextFillColorTertiaryBrush", shades.Light2, keepAlpha: false);
        }

        SetBrush(dictionary, "AccentFillColorSelectedTextBackgroundBrush", shades.Base, keepAlpha: false);
        SetBrush(dictionary, "TextOnAccentFillColorPrimaryBrush", ink, keepAlpha: true);
        SetBrush(dictionary, "TextOnAccentFillColorSecondaryBrush", ink, keepAlpha: true);
        SetBrush(dictionary, "TextOnAccentFillColorSelectedTextBrush", selectedInk, keepAlpha: true);
    }

    static void SetBrush(ResourceDictionary dictionary, string key, RgbColor rgb, bool keepAlpha)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is not SolidColorBrush brush)
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

    static string? ThemeKey(object key) => key switch
    {
        string name => name,
        ElementTheme theme => theme.ToString(),
        _ => null,
    };
}
