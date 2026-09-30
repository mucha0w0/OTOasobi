using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class NormalizePreset
{
    public const string CustomName = "カスタム";

    public string Name { get; set; } = "";

    public double Target { get; set; } = NormalizeEffect.DefaultDb;

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static IReadOnlyList<NormalizePreset> BuiltIns { get; } =
    [
        Make("最大", 0),
        Make("標準", -1),
        Make("余裕", -3),
        Make("控えめ", -6),
    ];

    public static bool IsReservedName(string name) =>
        name == CustomName || BuiltIns.Any(preset => preset.Name == name);

    public bool Matches(double db) =>
        Math.Round(Target, 1) == Math.Round(NormalizeEffect.Clamp(db), 1);

    static NormalizePreset Make(string name, double target) =>
        new()
        {
            Name = name,
            BuiltIn = true,
            Target = NormalizeEffect.Clamp(target),
        };
}

static class NormalizePresetStore
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
        "normalize-presets.json");

    public static IReadOnlyList<NormalizePreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<NormalizePreset>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>();
            var result = new List<NormalizePreset>();
            foreach (var preset in list)
            {
                var name = preset.Name.Trim();
                if (name.Length == 0 || name.Length > 40 || NormalizePreset.IsReservedName(name) || !seen.Add(name))
                    continue;
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Target = NormalizeEffect.Clamp(preset.Target);
                result.Add(preset);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<NormalizePreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
