using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class NormalizePreset : INamedPreset
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
    public static string FilePath { get; } = NamedList.DataFile("normalize-presets.json");

    public static IReadOnlyList<NormalizePreset> Load() =>
        JsonFiles.ReadList<NormalizePreset>(FilePath, Normalize);

    public static List<NormalizePreset> Normalize(IEnumerable<NormalizePreset>? list) =>
        NamedList.Collect(
            list,
            preset => preset.Name,
            NormalizePreset.IsReservedName,
            (preset, name) =>
            {
                preset.Name = name;
                preset.BuiltIn = false;
                preset.Target = NormalizeEffect.Clamp(preset.Target);
                return preset;
            });

    public static void Save(IEnumerable<NormalizePreset> presets) =>
        JsonFiles.Write(FilePath, presets.Where(preset => !preset.BuiltIn));
}
