using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

// CVVC の通常音素と子音音素。単独音のプリセットとは別のファイルに残す。
sealed class CvvcPreset
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
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "cvvc-presets.json");

    public static IReadOnlyList<CvvcPreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<CvvcPreset>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>();
            var result = new List<CvvcPreset>();
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<CvvcPreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

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
