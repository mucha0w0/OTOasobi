using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

// 設定スライダーの値。初期値は常にあり、それ以外は LocalAppData に残す。
sealed class OtoPreset
{
    public const string BuiltInName = "初期値";

    public string Name { get; set; } = "";
    public double Offset { get; set; }
    public double Consonant { get; set; }
    public double Blank { get; set; }
    public Dictionary<string, double> Overlap { get; set; } = [];

    [JsonIgnore]
    public bool BuiltIn { get; set; }
}

static class OtoPresetStore
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
        "presets.json");

    public static IReadOnlyList<OtoPreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<OtoPreset>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>();
            var result = new List<OtoPreset>();
            foreach (var preset in list)
            {
                var name = preset.Name.Trim();
                if (name.Length == 0 || name.Length > 40 || name == OtoPreset.BuiltInName || !seen.Add(name))
                    continue;
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Offset = Clamp(preset.Offset, -0.1, 0.1, 0, 3);
                preset.Consonant = Clamp(preset.Consonant, 0, 0.2, 0.2, 3);
                preset.Blank = Clamp(preset.Blank, -0.1, 0.1, 0, 3);
                preset.Overlap ??= [];
                foreach (var key in preset.Overlap.Keys.ToList())
                    preset.Overlap[key] = Clamp(preset.Overlap[key], 0, 100, 50, 0);
                result.Add(preset);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<OtoPreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    static double Clamp(double value, double lo, double hi, double fallback, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(Math.Round(value, digits), lo, hi);
    }
}
