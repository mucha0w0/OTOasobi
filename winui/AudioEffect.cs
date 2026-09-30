using System.Text.Encodings.Web;
using System.Text.Json;

namespace Otoasobi;

// 音声へ順にかける処理。リストの並びがかかる順で、Move で入れ替える。
public abstract class AudioEffect
{
    public bool Enabled { get; set; } = true;

    public abstract string Kind { get; }

    public abstract string Title { get; }

    // サンプルはチャンネルが交互に並ぶ。状態は呼び出しのあいだ保持する。
    public abstract void Process(Span<float> interleaved, int channels, int sampleRate);

    public abstract void Reset();
}

public sealed class EffectChain
{
    readonly List<AudioEffect> _effects = [];

    public IReadOnlyList<AudioEffect> Effects => _effects;

    public void Add(AudioEffect effect) => _effects.Add(effect);

    public void SetOrder(IEnumerable<AudioEffect> effects)
    {
        _effects.Clear();
        _effects.AddRange(effects);
    }

    public void Move(int from, int to)
    {
        if ((uint)from >= (uint)_effects.Count || (uint)to >= (uint)_effects.Count || from == to)
            return;
        var effect = _effects[from];
        _effects.RemoveAt(from);
        _effects.Insert(to, effect);
    }

    public void Reset()
    {
        foreach (var effect in _effects)
            effect.Reset();
    }

    public void Process(Span<float> interleaved, int channels, int sampleRate)
    {
        foreach (var effect in _effects)
        {
            if (effect.Enabled)
                effect.Process(interleaved, channels, sampleRate);
        }
    }
}

sealed class StoredEffect
{
    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Preset { get; set; } = "";

    public double[] Gains { get; set; } = [];

    public double Target { get; set; } = -1;
}

sealed class StoredChain
{
    public List<StoredEffect> Effects { get; set; } = [];
}

static class EffectChainStore
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
        "effects.json");

    public static StoredChain? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var stored = JsonSerializer.Deserialize<StoredChain>(File.ReadAllText(FilePath), JsonOptions);
            if (stored == null)
                return null;
            stored.Effects ??= [];
            foreach (var effect in stored.Effects)
            {
                effect.Kind ??= "";
                effect.Preset ??= "";
                effect.Gains ??= [];
            }

            return stored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(StoredChain chain)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(chain, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
