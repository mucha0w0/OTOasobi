using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class HighPassPreset
{
    public const string CustomName = "カスタム";

    public string Name { get; set; } = "";

    public double Frequency { get; set; } = HighPassEffect.DefaultHz;

    public int Slope { get; set; } = HighPassEffect.DefaultSlope;

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static IReadOnlyList<HighPassPreset> BuiltIns { get; } =
    [
        Make("標準", 80, HighPassEffect.Slope12),
        Make("急", 80, HighPassEffect.Slope24),
        Make("高め", 100, HighPassEffect.Slope12),
        Make("しっかり", 100, HighPassEffect.Slope24),
    ];

    public static bool IsReservedName(string name) =>
        name == CustomName || BuiltIns.Any(preset => preset.Name == name);

    public bool Matches(double hz, int slope) =>
        Math.Round(Frequency) == Math.Round(HighPassEffect.ClampHz(hz))
        && Slope == HighPassEffect.ClampSlope(slope);

    static HighPassPreset Make(string name, double hz, int slope) =>
        new()
        {
            Name = name,
            BuiltIn = true,
            Frequency = HighPassEffect.ClampHz(hz),
            Slope = HighPassEffect.ClampSlope(slope),
        };
}

static class HighPassPresetStore
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
        "highpass-presets.json");

    public static IReadOnlyList<HighPassPreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<HighPassPreset>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>();
            var result = new List<HighPassPreset>();
            foreach (var preset in list)
            {
                var name = preset.Name.Trim();
                if (name.Length == 0 || name.Length > 40 || HighPassPreset.IsReservedName(name) || !seen.Add(name))
                    continue;
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Frequency = HighPassEffect.ClampHz(preset.Frequency);
                preset.Slope = HighPassEffect.ClampSlope(preset.Slope);
                result.Add(preset);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<HighPassPreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
