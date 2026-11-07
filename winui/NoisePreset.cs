using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class NoisePreset : INamedPreset
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
    public static string FilePath { get; } = NamedList.DataFile("noise-presets.json");

    public static IReadOnlyList<NoisePreset> Load() =>
        JsonFiles.ReadList<NoisePreset>(FilePath, Normalize);

    public static List<NoisePreset> Normalize(IEnumerable<NoisePreset>? list) =>
        NamedList.Collect(
            list,
            preset => preset.Name,
            NoisePreset.IsReservedName,
            (preset, name) =>
            {
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Reduction = NoiseSuppressorEffect.ClampReduction(preset.Reduction);
                return preset;
            });

    public static void Save(IEnumerable<NoisePreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));
}
