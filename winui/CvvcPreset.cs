using System.Text.Json.Serialization;

namespace Otoasobi;

// CVVC の通常音素と子音音素。単独音のプリセットとは別のファイルに残す。
sealed class CvvcPreset : INamedPreset
{
    public const string BuiltInName = "初期値";

    public string Name { get; set; } = "";
    public double Consonant { get; set; } = 0.2;
    public double Blank { get; set; }
    public Dictionary<string, double> Overlap { get; set; } = [];
    public double VcOffset { get; set; }
    public Dictionary<string, double> VcConsonant { get; set; } = [];
    public Dictionary<string, double> VcBlank { get; set; } = [];
    public double VcOverlap { get; set; } = 50;

    [JsonIgnore]
    public bool BuiltIn { get; set; }
}

static class CvvcPresetStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "cvvc-presets.json");

    public static IReadOnlyList<CvvcPreset> Load() =>
        JsonFiles.ReadList<CvvcPreset>(FilePath, Normalize);

    public static List<CvvcPreset> Normalize(IEnumerable<CvvcPreset>? list)
    {
        var seen = new HashSet<string>();
        var result = new List<CvvcPreset>();
        if (list == null)
            return result;
        foreach (var preset in list)
        {
            var name = preset.Name.Trim();
            if (name.Length == 0 || name.Length > 40 || name == CvvcPreset.BuiltInName || !seen.Add(name))
                continue;
            preset.Name = name;
            preset.BuiltIn = false;
            preset.Consonant = Clamp(preset.Consonant, 0, 0.2, 0.2, 3);
            preset.Blank = Clamp(preset.Blank, -0.1, 0.1, 0, 3);
            preset.VcOffset = Clamp(preset.VcOffset, -0.1, 0.1, 0, 3);
            preset.VcOverlap = Percent(preset.VcOverlap, 50);
            preset.Overlap = Percents(preset.Overlap, 50);
            preset.VcConsonant = Percents(preset.VcConsonant, 100);
            preset.VcBlank = Percents(preset.VcBlank, 100);
            result.Add(preset);
        }

        return result;
    }

    public static void Save(IEnumerable<CvvcPreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));

    public static double Percent(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
    }

    static Dictionary<string, double> Percents(Dictionary<string, double>? table, double fallback)
    {
        var result = new Dictionary<string, double>();
        if (table == null)
            return result;
        foreach (var (key, value) in table)
        {
            if (string.IsNullOrEmpty(key))
                continue;
            result[key] = Percent(value, fallback);
        }

        return result;
    }

    static double Clamp(double value, double lo, double hi, double fallback, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(Math.Round(value, digits), lo, hi);
    }
}
