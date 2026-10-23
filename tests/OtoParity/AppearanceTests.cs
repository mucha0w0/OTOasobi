using Otoasobi;

namespace OtoParity;

public class AppearanceTests
{
    [Fact]
    public void ParsesThemeAndAccent()
    {
        Assert.Equal(AppThemeChoice.Light, AppThemes.Parse(" Light "));
        Assert.Equal(AppThemeChoice.Dark, AppThemes.Parse("DARK"));
        Assert.Equal(AppThemeChoice.System, AppThemes.Parse("nope"));
        Assert.Equal(AppThemeChoice.System, AppThemes.Parse(null));

        Assert.True(RgbColor.TryParse("#0078d4", out var color));
        Assert.Equal(new RgbColor(0, 120, 212), color);
        Assert.True(RgbColor.TryParse("E85D04", out color));
        Assert.Equal("#E85D04", color.ToHex());
        Assert.False(RgbColor.TryParse("#fff", out _));
        Assert.False(RgbColor.TryParse("red", out _));
        Assert.False(RgbColor.TryParse(null, out _));
    }

    [Fact]
    public void ShadesMoveFromTheBaseColor()
    {
        var shades = AccentPalette.Create(new RgbColor(0, 120, 212));
        Assert.True(AccentPalette.Luminance(shades.Light3) > AccentPalette.Luminance(shades.Light2));
        Assert.True(AccentPalette.Luminance(shades.Light2) > AccentPalette.Luminance(shades.Light1));
        Assert.True(AccentPalette.Luminance(shades.Light1) > AccentPalette.Luminance(shades.Base));
        Assert.True(AccentPalette.Luminance(shades.Base) > AccentPalette.Luminance(shades.Dark1));
        Assert.True(AccentPalette.Luminance(shades.Dark1) > AccentPalette.Luminance(shades.Dark2));
        Assert.True(AccentPalette.Luminance(shades.Dark2) > AccentPalette.Luminance(shades.Dark3));
    }

    [Fact]
    public void PicksTextThatStaysReadable()
    {
        Assert.True(AccentPalette.PrefersDarkText(new RgbColor(255, 255, 255)));
        Assert.True(AccentPalette.PrefersDarkText(new RgbColor(255, 220, 0)));
        Assert.False(AccentPalette.PrefersDarkText(new RgbColor(0, 0, 0)));
        Assert.False(AccentPalette.PrefersDarkText(new RgbColor(0, 60, 120)));
    }

    [Fact]
    public void ButtonInkStaysReadableOnEitherSurface()
    {
        var mauve = AccentPalette.ForSurface(new RgbColor(0xBA, 0x92, 0xB9), lightSurface: true);
        Assert.Equal(new RgbColor(255, 255, 255), mauve.Ink);
        Assert.False(AccentPalette.PrefersDarkText(mauve.Fill));
        Assert.True(AccentPalette.Luminance(mauve.AccentText) < AccentPalette.Luminance(new RgbColor(0xBA, 0x92, 0xB9)));

        var blue = AccentPalette.ForSurface(new RgbColor(0, 60, 120), lightSurface: false);
        Assert.Equal(new RgbColor(0, 0, 0), blue.Ink);
        Assert.True(AccentPalette.PrefersDarkText(blue.Fill));
        Assert.True(AccentPalette.Luminance(blue.AccentText) > 0.4);

        var windows = AccentPalette.ForSurface(new RgbColor(0, 120, 212), lightSurface: true);
        Assert.False(AccentPalette.PrefersDarkText(windows.Fill));
    }

    [Fact]
    public void RoundTripsTheAppearanceFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "oto-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "appearance.json");
            AppAppearanceStore.Write(path, new AppAppearanceState(AppThemeChoice.Dark, new RgbColor(0xE8, 0x5D, 0x04)));
            var loaded = AppAppearanceStore.Read(path);
            Assert.Equal(AppThemeChoice.Dark, loaded.Theme);
            Assert.Equal(new RgbColor(0xE8, 0x5D, 0x04), loaded.Accent);

            File.WriteAllText(path, """{ "theme": "nope", "accent": "red" }""");
            loaded = AppAppearanceStore.Read(path);
            Assert.Equal(AppThemeChoice.System, loaded.Theme);
            Assert.Null(loaded.Accent);

            Assert.Equal(AppThemeChoice.System, AppAppearanceStore.Read(Path.Combine(directory, "missing.json")).Theme);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
