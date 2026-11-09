using System.Text.Json.Serialization;

namespace Otoasobi;

// 設定スライダーの値。初期値は常にあり、それ以外は LocalAppData に残す。
sealed class OtoPreset : INamedPreset
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
    public static string FilePath { get; } = NamedList.DataFile("presets.json");

    public static IReadOnlyList<OtoPreset> Load() =>
        JsonFiles.ReadList<OtoPreset>(FilePath, Normalize);

    public static List<OtoPreset> Normalize(IEnumerable<OtoPreset>? list) =>
        NamedList.Collect(
            list,
            preset => preset.Name,
            name => name == OtoPreset.BuiltInName,
            (preset, name) =>
            {
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Offset = Clamp(preset.Offset, -0.1, 0.1, 0, 3);
                preset.Consonant = Clamp(preset.Consonant, 0, 0.2, 0.2, 3);
                preset.Blank = Clamp(preset.Blank, -0.1, 0.1, 0, 3);
                preset.Overlap ??= [];
                foreach (var key in preset.Overlap.Keys.ToList())
                    preset.Overlap[key] = ConsonantOverlap.Stored(preset.Overlap[key]);
                return preset;
            });

    public static void Save(IEnumerable<OtoPreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));

    static double Clamp(double value, double lo, double hi, double fallback, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(Math.Round(value, digits), lo, hi);
    }
}
