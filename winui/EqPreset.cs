using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class EqPreset
{
    public const string CustomName = "カスタム";

    public string Name { get; set; } = "";

    public double[] Gains { get; set; } = [];

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static IReadOnlyList<EqPreset> BuiltIns { get; } =
    [
        Make("フラット", 0, 0, 0, 0, 0, 0, 0, 0),
        Make("明るい", -1.5, -1, 0, 0.5, 1, 2.5, 3.5, 2.5),
        Make("暖かい", 2, 2.5, 1.5, 0.5, 0, -0.5, -1.5, -2),
        Make("明瞭", -0.5, -1, -3, -2, 0.5, 2.5, 3, 1),
        Make("歯擦音を抑える", 0, 0, 0, 0, 0.5, 1, -3.5, -3),
    ];

    public static bool IsReservedName(string name) =>
        name == CustomName || BuiltIns.Any(preset => preset.Name == name);

    static EqPreset Make(string name, params double[] gains) =>
        new()
        {
            Name = name,
            BuiltIn = true,
            Gains = gains,
        };
}

static class EqPresetStore
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
        "eq-presets.json");

    public static IReadOnlyList<EqPreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<EqPreset>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>();
            var result = new List<EqPreset>();
            foreach (var preset in list)
            {
                var name = preset.Name.Trim();
                if (name.Length == 0 || name.Length > 40 || EqPreset.IsReservedName(name) || !seen.Add(name))
                    continue;
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Gains = Clean(preset.Gains);
                result.Add(preset);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<EqPreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    static double[] Clean(double[]? gains)
    {
        var result = new double[EqualizerEffect.BandCount];
        if (gains == null)
            return result;
        for (var i = 0; i < result.Length && i < gains.Length; i++)
            result[i] = EqualizerEffect.QuantizeGain(gains[i], magnet: false);
        return result;
    }
}
