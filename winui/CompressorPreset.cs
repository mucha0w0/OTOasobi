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
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "compressor-presets.json");

    public static IReadOnlyList<CompressorPreset> Load() =>
        JsonFiles.ReadList<CompressorPreset>(FilePath, Normalize);

    public static List<CompressorPreset> Normalize(IEnumerable<CompressorPreset>? list)
    {
        var seen = new HashSet<string>();
        var result = new List<CompressorPreset>();
        if (list == null)
            return result;
        foreach (var preset in list)
        {
            var name = preset.Name.Trim();
            if (name.Length == 0 || name.Length > 40 || CompressorPreset.IsReservedName(name) || !seen.Add(name))
                continue;
            preset.Name = name;
            preset.BuiltIn = false;
            preset.Ratio = CompressorEffect.ClampRatio(preset.Ratio);
            preset.Reduction = CompressorEffect.ClampReduction(preset.Reduction);
            result.Add(preset);
        }

        return result;
    }

    public static void Save(IEnumerable<CompressorPreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));
}
