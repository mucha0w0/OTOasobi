using System.Globalization;

namespace Otoasobi;

enum AppThemeChoice
{
    System,
    Light,
    Dark,
}

readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var hex = text.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length != 6)
            return false;
        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return false;
        color = new RgbColor(r, g, b);
        return true;
    }

    public string ToHex() => FormattableString.Invariant($"#{R:X2}{G:X2}{B:X2}");
}

readonly record struct AppAppearanceState(AppThemeChoice Theme, RgbColor? Accent, double Scale = 1, bool LockPanels = false);

// 配布画面の全体倍率。50% から 200% まで、10% 刻み。
static class UiScale
{
    public const double Default = 1;

    public static double Clamp(double scale)
    {
        if (!double.IsFinite(scale))
            return Default;
        var stepped = Math.Round(scale * 10, MidpointRounding.AwayFromZero) / 10;
        if (stepped < 0.5)
            return 0.5;
        if (stepped > 2)
            return 2;
        return stepped;
    }
}

static class AppThemes
{
    public static AppThemeChoice Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "light" => AppThemeChoice.Light,
        "dark" => AppThemeChoice.Dark,
        _ => AppThemeChoice.System,
    };

    public static string FileName(AppThemeChoice theme) => theme switch
    {
        AppThemeChoice.Light => "light",
        AppThemeChoice.Dark => "dark",
        _ => "system",
    };
}

// 暗い画面では明るい段階、明るい画面では暗い段階をボタンに使う。
static class AccentPalette
{
    public readonly record struct Shades(
        RgbColor Base,
        RgbColor Light1,
        RgbColor Light2,
        RgbColor Light3,
        RgbColor Dark1,
        RgbColor Dark2,
        RgbColor Dark3);

    public static Shades Create(RgbColor color)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return new Shades(
            color,
            FromHsl(hue, saturation * 0.92, lightness + 0.12),
            FromHsl(hue, saturation * 0.78, lightness + 0.28),
            FromHsl(hue, saturation * 0.60, lightness + 0.46),
            FromHsl(hue, Math.Min(1, saturation * 1.05), lightness - 0.10),
            FromHsl(hue, Math.Min(1, saturation * 1.08), lightness - 0.22),
            FromHsl(hue, Math.Min(1, saturation * 1.10), lightness - 0.36));
    }

    // この明るさより上なら、アクセントの上の文字は黒の方が読める。
    public static bool PrefersDarkText(RgbColor fill) => Luminance(fill) > 0.179;

    // 明るい面のボタンは白文字が読める濃さ、暗い面のボタンは黒文字が読める明るさにする。
    public readonly record struct SurfacePaint(RgbColor Fill, RgbColor Ink, RgbColor AccentText);

    public static SurfacePaint ForSurface(RgbColor color, bool lightSurface)
    {
        var luminance = Luminance(color);
        if (lightSurface)
        {
            var fill = luminance <= 0.16 ? color : AdjustLuminance(color, 0.16);
            var text = luminance <= 0.20 ? color : AdjustLuminance(color, 0.16);
            return new SurfacePaint(fill, new RgbColor(255, 255, 255), text);
        }

        var lightFill = luminance is >= 0.38 and <= 0.70 ? color : AdjustLuminance(color, luminance > 0.70 ? 0.62 : 0.50);
        var lightText = luminance is >= 0.48 and <= 0.82 ? color : AdjustLuminance(color, luminance > 0.82 ? 0.70 : 0.60);
        return new SurfacePaint(lightFill, new RgbColor(0, 0, 0), lightText);
    }

    static RgbColor AdjustLuminance(RgbColor color, double target)
    {
        var (hue, saturation, _) = ToHsl(color);
        var adjusted = Math.Clamp(saturation * (target < Luminance(color) ? 1.15 : 0.92), 0, 1);
        var lo = 0d;
        var hi = 0.95;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            if (Luminance(FromHsl(hue, adjusted, mid)) < target)
                lo = mid;
            else
                hi = mid;
        }

        return FromHsl(hue, adjusted, hi);
    }

    public static double Luminance(RgbColor color)
    {
        static double Channel(byte value)
        {
            var s = value / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    static (double H, double S, double L) ToHsl(RgbColor color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta < 0.00001)
            return (0, 0, lightness);
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == r)
            hue = ((g - b) / delta + (g < b ? 6 : 0)) * 60;
        else if (max == g)
            hue = ((b - r) / delta + 2) * 60;
        else
            hue = ((r - g) / delta + 4) * 60;
        return (hue, saturation, lightness);
    }

    static RgbColor FromHsl(double hue, double saturation, double lightness)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        hue = ((hue % 360) + 360) % 360;
        if (saturation <= 0)
        {
            var gray = ToByte(lightness);
            return new RgbColor(gray, gray, gray);
        }

        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new RgbColor(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255), 0, 255);
}

static class AppAppearanceStore
{
    public static AppAppearanceState Read(string path)
    {
        var file = JsonFiles.Read<AppAppearanceFile>(path);
        RgbColor? accent = RgbColor.TryParse(file?.Accent, out var color) ? color : null;
        var scale = file?.Scale is double value ? UiScale.Clamp(value) : UiScale.Default;
        return new AppAppearanceState(AppThemes.Parse(file?.Theme), accent, scale, file?.LockPanels == true);
    }

    public static void Write(string path, AppAppearanceState state)
    {
        JsonFiles.Write(path, new AppAppearanceFile
        {
            Theme = AppThemes.FileName(state.Theme),
            Accent = state.Accent?.ToHex(),
            Scale = UiScale.Clamp(state.Scale),
            LockPanels = state.LockPanels,
        }, JsonFiles.OptionsIgnoringNull);
    }

    sealed class AppAppearanceFile
    {
        public string Theme { get; set; } = "system";

        public string? Accent { get; set; }

        public double? Scale { get; set; }

        public bool LockPanels { get; set; }
    }
}

static class AppAppearance
{
    public static AppThemeChoice Theme { get; private set; } = AppThemeChoice.System;

    public static RgbColor? Accent { get; private set; }

    public static double Scale { get; private set; } = UiScale.Default;

    public static bool LockPanels { get; private set; }

    public static event Action? Changed;

    public static void Load()
    {
        var state = AppAppearanceStore.Read(AppPaths.AppearanceFile);
        Theme = state.Theme;
        Accent = state.Accent;
        Scale = state.Scale;
        LockPanels = state.LockPanels;
    }

    public static void SaveTheme(AppThemeChoice theme)
    {
        if (Theme == theme)
            return;
        Theme = theme;
        AppAppearanceStore.Write(AppPaths.AppearanceFile, Current());
    }

    public static void SaveAccent(RgbColor? accent)
    {
        if (Accent == accent)
            return;
        Accent = accent;
        AppAppearanceStore.Write(AppPaths.AppearanceFile, Current());
    }

    public static void SaveScale(double scale)
    {
        scale = UiScale.Clamp(scale);
        if (Scale == scale)
            return;
        Scale = scale;
        AppAppearanceStore.Write(AppPaths.AppearanceFile, Current());
    }

    public static void SaveLockPanels(bool locked)
    {
        if (LockPanels == locked)
            return;
        LockPanels = locked;
        AppAppearanceStore.Write(AppPaths.AppearanceFile, Current());
    }

    static AppAppearanceState Current() => new(Theme, Accent, Scale, LockPanels);

    public static void RaiseChanged() => Changed?.Invoke();
}
