using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class EqPreset : INamedPreset
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
    public static string FilePath { get; } = NamedList.DataFile("eq-presets.json");

    public static IReadOnlyList<EqPreset> Load() =>
        JsonFiles.ReadList<EqPreset>(FilePath, Normalize);

    public static List<EqPreset> Normalize(IEnumerable<EqPreset>? list) =>
        NamedList.Collect(
            list,
            preset => preset.Name,
            EqPreset.IsReservedName,
            (preset, name) =>
            {
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Gains = Clean(preset.Gains);
                return preset;
            });

    public static void Save(IEnumerable<EqPreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));

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
