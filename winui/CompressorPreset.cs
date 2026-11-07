using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class CompressorPreset : INamedPreset
{
    public const string CustomName = "カスタム";

    public string Name { get; set; } = "";

    public double Ratio { get; set; } = CompressorEffect.DefaultRatio;

    public double Reduction { get; set; } = CompressorEffect.DefaultReductionDb;

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static IReadOnlyList<CompressorPreset> BuiltIns { get; } =
    [
        Make("軽い", 2, 2),
        Make("標準", CompressorEffect.DefaultRatio, CompressorEffect.DefaultReductionDb),
        Make("しっかり", 3, 4),
    ];

    public static bool IsReservedName(string name) =>
        name == CustomName || BuiltIns.Any(preset => preset.Name == name);

    public bool Matches(double ratio, double reduction) =>
        Math.Round(Ratio, 1) == Math.Round(CompressorEffect.ClampRatio(ratio), 1)
        && Math.Round(Reduction, 1) == Math.Round(CompressorEffect.ClampReduction(reduction), 1);

    static CompressorPreset Make(string name, double ratio, double reduction) =>
        new()
        {
            Name = name,
            BuiltIn = true,
            Ratio = CompressorEffect.ClampRatio(ratio),
            Reduction = CompressorEffect.ClampReduction(reduction),
        };
}

static class CompressorPresetStore
{
    public static string FilePath { get; } = NamedList.DataFile("compressor-presets.json");

    public static IReadOnlyList<CompressorPreset> Load() =>
        JsonFiles.ReadList<CompressorPreset>(FilePath, Normalize);

    public static List<CompressorPreset> Normalize(IEnumerable<CompressorPreset>? list) =>
        NamedList.Collect(
            list,
            preset => preset.Name,
            CompressorPreset.IsReservedName,
            (preset, name) =>
            {
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Ratio = CompressorEffect.ClampRatio(preset.Ratio);
                preset.Reduction = CompressorEffect.ClampReduction(preset.Reduction);
                return preset;
            });

    public static void Save(IEnumerable<CompressorPreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));
}
