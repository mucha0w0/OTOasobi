using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

sealed class EffectRackPreset
{
    public const string CustomName = "カスタム";

    public string Name { get; set; } = "";

    public List<StoredEffect> Effects { get; set; } = [];

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static IReadOnlyList<EffectRackPreset> BuiltIns { get; } =
    [
        MakeStandard(),
        MakeBright(),
        MakeWarm(),
        MakeClear(),
    ];

    public static bool IsReservedName(string name) =>
        name == CustomName || BuiltIns.Any(preset => preset.Name == name);

    public bool Matches(IReadOnlyList<StoredEffect> snapshot) =>
        StoredEffectSequence.Matches(Effects, snapshot);

    public EffectRackPreset CloneAsUser(string name) =>
        new()
        {
            Name = name,
            BuiltIn = false,
            Effects = Effects.Select(effect => effect.Clone()).ToList(),
        };

    static EffectRackPreset MakeStandard()
    {
        var flat = EqPreset.BuiltIns[0];
        return Make("標準",
        [
            Noise(false, "標準", NoiseSuppressorEffect.DefaultReductionDb),
            HighPass(true, "標準", HighPassEffect.DefaultHz, HighPassEffect.DefaultSlope),
            Eq(true, "フラット", flat.Gains),
            Compressor(true, "標準", CompressorEffect.DefaultRatio, CompressorEffect.DefaultReductionDb),
            Normalize(false, "標準", NormalizeEffect.DefaultDb),
        ]);
    }

    static EffectRackPreset MakeBright()
    {
        var eq = EqPreset.BuiltIns.First(preset => preset.Name == "明るい");
        var comp = CompressorPreset.BuiltIns.First(preset => preset.Name == "軽い");
        return Make("明るく整える",
        [
            Noise(false, "標準", NoiseSuppressorEffect.DefaultReductionDb),
            HighPass(true, "標準", HighPassEffect.DefaultHz, HighPassEffect.DefaultSlope),
            Eq(true, eq.Name, eq.Gains),
            Compressor(true, comp.Name, comp.Ratio, comp.Reduction),
            Normalize(false, "標準", NormalizeEffect.DefaultDb),
        ]);
    }

    static EffectRackPreset MakeWarm()
    {
        var eq = EqPreset.BuiltIns.First(preset => preset.Name == "暖かい");
        return Make("暖かく整える",
        [
            Noise(false, "標準", NoiseSuppressorEffect.DefaultReductionDb),
            HighPass(true, "標準", HighPassEffect.DefaultHz, HighPassEffect.DefaultSlope),
            Eq(true, eq.Name, eq.Gains),
            Compressor(true, "標準", CompressorEffect.DefaultRatio, CompressorEffect.DefaultReductionDb),
            Normalize(false, "標準", NormalizeEffect.DefaultDb),
        ]);
    }

    static EffectRackPreset MakeClear()
    {
        var eq = EqPreset.BuiltIns.First(preset => preset.Name == "明瞭");
        return Make("明瞭に整える",
        [
            Noise(false, "標準", NoiseSuppressorEffect.DefaultReductionDb),
            HighPass(true, "標準", HighPassEffect.DefaultHz, HighPassEffect.DefaultSlope),
            Eq(true, eq.Name, eq.Gains),
            Compressor(true, "標準", CompressorEffect.DefaultRatio, CompressorEffect.DefaultReductionDb),
            Normalize(false, "標準", NormalizeEffect.DefaultDb),
        ]);
    }

    static EffectRackPreset Make(string name, List<StoredEffect> effects) =>
        new()
        {
            Name = name,
            BuiltIn = true,
            Effects = effects,
        };

    static StoredEffect Noise(bool enabled, string preset, double reduction) =>
        new()
        {
            Kind = NoiseSuppressorEffect.KindId,
            Enabled = enabled,
            Preset = preset,
            Reduction = reduction,
        };

    static StoredEffect HighPass(bool enabled, string preset, double hz, int slope) =>
        new()
        {
            Kind = HighPassEffect.KindId,
            Enabled = enabled,
            Preset = preset,
            Frequency = hz,
            Slope = slope,
        };

    static StoredEffect Eq(bool enabled, string preset, IReadOnlyList<double> gains) =>
        new()
        {
            Kind = EqualizerEffect.KindId,
            Enabled = enabled,
            Preset = preset,
            Gains = gains.ToArray(),
        };

    static StoredEffect Compressor(bool enabled, string preset, double ratio, double reduction) =>
        new()
        {
            Kind = CompressorEffect.KindId,
            Enabled = enabled,
            Preset = preset,
            Ratio = ratio,
            Reduction = reduction,
        };

    static StoredEffect Normalize(bool enabled, string preset, double target) =>
        new()
        {
            Kind = NormalizeEffect.KindId,
            Enabled = enabled,
            Preset = preset,
            Target = target,
        };
}

static class StoredEffectSequence
{
    public static bool Matches(IReadOnlyList<StoredEffect> left, IReadOnlyList<StoredEffect> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!Same(left[i], right[i]))
                return false;
        }

        return true;
    }

    static bool Same(StoredEffect left, StoredEffect right)
    {
        if (left.Kind != right.Kind || left.Enabled != right.Enabled)
            return false;
        return left.Kind switch
        {
            EqualizerEffect.KindId => SameGains(left.Gains, right.Gains),
            NormalizeEffect.KindId =>
                Math.Round(left.Target, 1) == Math.Round(NormalizeEffect.Clamp(right.Target), 1),
            HighPassEffect.KindId =>
                Math.Round(left.Frequency) == Math.Round(HighPassEffect.ClampHz(right.Frequency))
                && left.Slope == HighPassEffect.ClampSlope(right.Slope),
            NoiseSuppressorEffect.KindId =>
                Math.Round(left.Reduction, 1) == Math.Round(NoiseSuppressorEffect.ClampReduction(right.Reduction), 1),
            CompressorEffect.KindId =>
                Math.Round(left.Ratio, 1) == Math.Round(CompressorEffect.ClampRatio(right.Ratio), 1)
                && Math.Round(left.Reduction, 1) == Math.Round(CompressorEffect.ClampReduction(right.Reduction), 1),
            _ => true,
        };
    }

    static bool SameGains(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count < EqualizerEffect.BandCount || right.Count < EqualizerEffect.BandCount)
            return false;
        for (var i = 0; i < EqualizerEffect.BandCount; i++)
        {
            if (Math.Round(left[i], 1) != Math.Round(right[i], 1))
                return false;
        }

        return true;
    }
}

static class EffectRackPresetStore
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
        "effect-rack-presets.json");

    public static IReadOnlyList<EffectRackPreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<EffectRackPreset>>(File.ReadAllText(FilePath), JsonOptions);
            return Normalize(list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static List<EffectRackPreset> Normalize(IEnumerable<EffectRackPreset>? list)
    {
        var seen = new HashSet<string>();
        var result = new List<EffectRackPreset>();
        if (list == null)
            return result;
        foreach (var preset in list)
        {
            var name = preset.Name.Trim();
            if (name.Length == 0 || name.Length > 40 || EffectRackPreset.IsReservedName(name) || !seen.Add(name))
                continue;
            preset.Name = name;
            preset.BuiltIn = false;
            preset.Effects = CleanEffects(preset.Effects);
            if (preset.Effects.Count == 0)
                continue;
            result.Add(preset);
        }

        return result;
    }

    public static List<StoredEffect> NormalizeEffects(IEnumerable<StoredEffect>? effects)
    {
        var cleaned = CleanEffects(effects == null ? [] : effects.ToList());
        var seen = new HashSet<string>();
        var result = new List<StoredEffect>();
        foreach (var effect in cleaned)
        {
            if (!IsKnownKind(effect.Kind) || !seen.Add(effect.Kind))
                continue;
            result.Add(effect);
        }

        return result;
    }

    static bool IsKnownKind(string kind) => kind is
        EqualizerEffect.KindId
        or NormalizeEffect.KindId
        or HighPassEffect.KindId
        or NoiseSuppressorEffect.KindId
        or CompressorEffect.KindId;

    public static void Save(IEnumerable<EffectRackPreset> presets)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(presets.Where(preset => !preset.BuiltIn), JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    static List<StoredEffect> CleanEffects(List<StoredEffect>? effects)
    {
        if (effects == null)
            return [];
        var result = new List<StoredEffect>();
        foreach (var effect in effects)
        {
            effect.Kind ??= "";
            effect.Preset ??= "";
            effect.Gains ??= [];
            if (effect.Kind.Length == 0)
                continue;
            result.Add(effect.Clone());
        }

        return result;
    }
}
