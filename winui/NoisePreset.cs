using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class NoisePreset
{
    public const string CustomName = "カスタム";

    public string Name { get; set; } = "";

    public double Reduction { get; set; } = NoiseSuppressorEffect.DefaultReductionDb;

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static IReadOnlyList<NoisePreset> BuiltIns { get; } =
    [
        Make("控えめ", 4),
        Make("標準", NoiseSuppressorEffect.DefaultReductionDb),
        Make("しっかり", 12),
        Make("最大", NoiseSuppressorEffect.MaxReductionDb),
    ];

    public static bool IsReservedName(string name) =>
        name == CustomName || BuiltIns.Any(preset => preset.Name == name);

    public bool Matches(double db) =>
        Math.Round(Reduction, 1) == Math.Round(NoiseSuppressorEffect.ClampReduction(db), 1);

    static NoisePreset Make(string name, double reduction) =>
        new()
        {
            Name = name,
            BuiltIn = true,
            Reduction = NoiseSuppressorEffect.ClampReduction(reduction),
        };
}

static class NoisePresetStore
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
        "noise-presets.json");

    public static IReadOnlyList<NoisePreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<NoisePreset>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>();
            var result = new List<NoisePreset>();
            foreach (var preset in list)
            {
                var name = preset.Name.Trim();
                if (name.Length == 0 || name.Length > 40 || NoisePreset.IsReservedName(name) || !seen.Add(name))
                    continue;
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Reduction = NoiseSuppressorEffect.ClampReduction(preset.Reduction);
                result.Add(preset);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<NoisePreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
