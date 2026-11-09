using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class HighPassPreset : INamedPreset
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
    public static string FilePath { get; } = NamedList.DataFile("highpass-presets.json");

    public static IReadOnlyList<HighPassPreset> Load() =>
        JsonFiles.ReadList<HighPassPreset>(FilePath, Normalize);

    public static List<HighPassPreset> Normalize(IEnumerable<HighPassPreset>? list) =>
        NamedList.Collect(
            list,
            preset => preset.Name,
            HighPassPreset.IsReservedName,
            (preset, name) =>
            {
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Frequency = HighPassEffect.ClampHz(preset.Frequency);
                preset.Slope = HighPassEffect.ClampSlope(preset.Slope);
                return preset;
            });

    public static void Save(IEnumerable<HighPassPreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));
}
