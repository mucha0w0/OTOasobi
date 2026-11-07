using System.Globalization;
using System.Text.Json;
using Otoasobi;

namespace Otoasobi.Probe;

sealed class ProbeEffects
{
    readonly NoiseSuppressorEffect _noise = new();
    readonly HighPassEffect _highPass = new();
    readonly EqualizerEffect _equalizer = new();
    readonly CompressorEffect _compressor = new();
    readonly NormalizeEffect _normalize = new();
    readonly EffectChain _chain = new();
    string? _folder;
    string _noiseFile = "";
    double _noiseStart;
    double _noiseEnd;

    public ProbeEffects()
    {
        var stored = EffectChainStore.Load();
        ApplyList(stored == null || stored.Effects.Count == 0 ? EffectRackPreset.BuiltIns[0].Effects : stored.Effects);
    }

    public EffectChain Chain => _chain;

    public bool NeedsNoiseRange => _noise.Enabled && !_noise.HasProfile;

    public void SetFolder(string? folder)
    {
        _folder = folder;
        _noise.ClearProfile();
        _noiseFile = "";
        _noiseStart = 0;
        _noiseEnd = 0;
        if (string.IsNullOrEmpty(folder))
            return;
        var capture = NoiseCaptureStore.Find(folder);
        if (capture == null)
            return;
        TryNoise(capture.File, capture.Start, capture.End, save: false);
    }

    public void ApplyPreset(string? name)
    {
        var preset = EffectRackPreset.BuiltIns.FirstOrDefault(item => item.Name == name)
            ?? EffectRackPresetStore.Load().FirstOrDefault(item => item.Name == name);
        if (preset == null)
            return;
        ApplyList(preset.Effects);
        Save();
    }

    public void Set(JsonElement body)
    {
        _noise.Enabled = Flag(body, "noiseEnabled");
        _noise.ReductionDb = Number(body, "noiseReduction", NoiseSuppressorEffect.DefaultReductionDb);
        _highPass.Enabled = Flag(body, "highpassEnabled");
        _highPass.Frequency = Number(body, "highpassHz", HighPassEffect.DefaultHz);
        _highPass.Slope = (int)Math.Round(Number(body, "highpassSlope", HighPassEffect.DefaultSlope));
        _equalizer.Enabled = Flag(body, "eqEnabled");
        if (body.TryGetProperty("gains", out var gains) && gains.ValueKind == JsonValueKind.Array)
            _equalizer.SetGains(gains.EnumerateArray().Select(item => item.TryGetDouble(out var value) ? value : 0).ToArray());
        _compressor.Enabled = Flag(body, "compressorEnabled");
        _compressor.Ratio = Number(body, "compressorRatio", CompressorEffect.DefaultRatio);
        _compressor.ReductionDb = Number(body, "compressorReduction", CompressorEffect.DefaultReductionDb);
        _normalize.Enabled = Flag(body, "normalizeEnabled");
        _normalize.TargetDb = Number(body, "normalizeTarget", NormalizeEffect.DefaultDb);
        Pin();
        Save();
    }

    public void Move(int from, int to)
    {
        if (from <= 0 || to < 1)
            return;
        _chain.Move(from, to);
        Pin();
        Save();
    }

    public string? SetNoise(string? file, double start, double end)
    {
        if (string.IsNullOrEmpty(_folder))
            return "フォルダを開いてください。";
        if (string.IsNullOrEmpty(file))
        {
            _noise.ClearProfile();
            _noiseFile = "";
            _noiseStart = 0;
            _noiseEnd = 0;
            try
            {
                NoiseCaptureStore.Remove(_folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }

            return null;
        }

        return TryNoise(file, start, end, save: true);
    }

    public object Snapshot()
    {
        var files = string.IsNullOrEmpty(_folder) ? [] : NoiseSources.List(_folder).Select(item => item.Name).ToArray();
        var selected = MatchPreset();
        return new
        {
            type = "effects",
            preset = selected,
            presets = EffectRackPreset.BuiltIns.Select(item => item.Name).Concat(EffectRackPresetStore.Load().Select(item => item.Name)).ToArray(),
            noiseEnabled = _noise.Enabled,
            noiseReduction = _noise.ReductionDb,
            noiseFiles = files,
            noiseFile = _noiseFile,
            noiseStart = _noiseStart,
            noiseEnd = _noiseEnd,
            noiseReady = _noise.HasProfile,
            highpassEnabled = _highPass.Enabled,
            highpassHz = _highPass.Frequency,
            highpassSlope = _highPass.Slope,
            eqEnabled = _equalizer.Enabled,
            gains = _equalizer.CopyGains(),
            compressorEnabled = _compressor.Enabled,
            compressorRatio = _compressor.Ratio,
            compressorReduction = _compressor.ReductionDb,
            normalizeEnabled = _normalize.Enabled,
            normalizeTarget = _normalize.TargetDb,
            noiseNote = NoiseNote(),
            order = _chain.Effects.Select(KindOf).ToArray(),
        };
    }

    static string KindOf(AudioEffect effect) => effect.Kind;

    StoredEffect StoredOf(AudioEffect effect)
    {
        if (ReferenceEquals(effect, _noise))
            return new StoredEffect { Kind = _noise.Kind, Enabled = _noise.Enabled, Reduction = _noise.ReductionDb, Preset = "標準" };
        if (ReferenceEquals(effect, _highPass))
            return new StoredEffect { Kind = _highPass.Kind, Enabled = _highPass.Enabled, Frequency = _highPass.Frequency, Slope = _highPass.Slope, Preset = "標準" };
        if (ReferenceEquals(effect, _equalizer))
            return new StoredEffect { Kind = _equalizer.Kind, Enabled = _equalizer.Enabled, Gains = _equalizer.CopyGains(), Preset = "フラット" };
        if (ReferenceEquals(effect, _compressor))
            return new StoredEffect { Kind = _compressor.Kind, Enabled = _compressor.Enabled, Ratio = _compressor.Ratio, Reduction = _compressor.ReductionDb, Preset = "標準" };
        return new StoredEffect { Kind = _normalize.Kind, Enabled = _normalize.Enabled, Target = _normalize.TargetDb, Preset = "標準" };
    }

    string? NoiseNote()
    {
        if (!_noise.Enabled || !_noise.HasProfile)
            return null;
        var seconds = Math.Abs(_noiseEnd - _noiseStart).ToString("0.00", CultureInfo.InvariantCulture);
        return $"ノイズサプレッサーは、選んだ {seconds}秒の無音で抑えます。";
    }

    string? TryNoise(string file, double start, double end, bool save)
    {
        var path = NoiseSources.Resolve(_folder!, file);
        if (path == null)
            return "ノイズの原音を選べませんでした。";
        WavAudio audio;
        try
        {
            audio = WavAudio.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ex.Message;
        }

        var mono = Downmix(audio);
        var message = NoiseSuppressorEffect.RangeMessage(mono, audio.SampleRate, start, end);
        if (message != null)
            return message;
        try
        {
            _noise.SetProfile(NoiseSuppressorEffect.BuildProfile(mono, audio.SampleRate, start, end));
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }

        _noiseFile = file;
        _noiseStart = start;
        _noiseEnd = end;
        if (!save)
            return null;
        try
        {
            NoiseCaptureStore.Save(new NoiseCapture { Folder = _folder!, File = file, Start = start, End = end });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return null;
    }

    void ApplyList(IEnumerable<StoredEffect> effects)
    {
        foreach (var entry in effects)
        {
            if (entry.Kind == NoiseSuppressorEffect.KindId)
            {
                _noise.Enabled = entry.Enabled;
                _noise.ReductionDb = entry.Reduction;
            }
            else if (entry.Kind == HighPassEffect.KindId)
            {
                _highPass.Enabled = entry.Enabled;
                _highPass.Frequency = entry.Frequency;
                _highPass.Slope = entry.Slope;
            }
            else if (entry.Kind == EqualizerEffect.KindId)
            {
                _equalizer.Enabled = entry.Enabled;
                _equalizer.SetGains(entry.Gains);
            }
            else if (entry.Kind == CompressorEffect.KindId)
            {
                _compressor.Enabled = entry.Enabled;
                _compressor.Ratio = entry.Ratio;
                _compressor.ReductionDb = entry.Reduction;
            }
            else if (entry.Kind == NormalizeEffect.KindId)
            {
                _normalize.Enabled = entry.Enabled;
                _normalize.TargetDb = entry.Target;
            }
        }

        var tail = new List<AudioEffect>();
        foreach (var entry in effects)
        {
            var effect = EffectFor(entry.Kind);
            if (effect == null || ReferenceEquals(effect, _noise) || tail.Contains(effect))
                continue;
            tail.Add(effect);
        }

        foreach (var effect in new AudioEffect[] { _highPass, _equalizer, _compressor, _normalize })
        {
            if (!tail.Contains(effect))
                tail.Add(effect);
        }

        _chain.SetOrder(new[] { _noise }.Concat(tail));
    }

    void Pin()
    {
        var tail = _chain.Effects.Where(effect => !ReferenceEquals(effect, _noise)).ToList();
        foreach (var effect in new AudioEffect[] { _highPass, _equalizer, _compressor, _normalize })
        {
            if (!tail.Contains(effect))
                tail.Add(effect);
        }

        _chain.SetOrder(new[] { _noise }.Concat(tail));
    }

    AudioEffect? EffectFor(string? kind) => kind switch
    {
        NoiseSuppressorEffect.KindId => _noise,
        HighPassEffect.KindId => _highPass,
        EqualizerEffect.KindId => _equalizer,
        CompressorEffect.KindId => _compressor,
        NormalizeEffect.KindId => _normalize,
        _ => null,
    };

    void Save()
    {
        EffectChainStore.Save(new StoredChain
        {
            Effects = _chain.Effects.Select(StoredOf).ToList(),
        });
    }

    string MatchPreset()
    {
        var snapshot = new List<StoredEffect>
        {
            new() { Kind = NoiseSuppressorEffect.KindId, Enabled = _noise.Enabled, Reduction = _noise.ReductionDb },
            new() { Kind = HighPassEffect.KindId, Enabled = _highPass.Enabled, Frequency = _highPass.Frequency, Slope = _highPass.Slope },
            new() { Kind = EqualizerEffect.KindId, Enabled = _equalizer.Enabled, Gains = _equalizer.CopyGains() },
            new() { Kind = CompressorEffect.KindId, Enabled = _compressor.Enabled, Ratio = _compressor.Ratio, Reduction = _compressor.ReductionDb },
            new() { Kind = NormalizeEffect.KindId, Enabled = _normalize.Enabled, Target = _normalize.TargetDb },
        };
        var match = EffectRackPreset.BuiltIns.FirstOrDefault(preset => SameOrder(preset.Effects, snapshot))
            ?? EffectRackPresetStore.Load().FirstOrDefault(preset => SameOrder(preset.Effects, snapshot));
        return match?.Name ?? EffectRackPreset.CustomName;
    }

    static bool SameOrder(IReadOnlyList<StoredEffect> preset, IReadOnlyList<StoredEffect> current)
    {
        if (preset.Count != current.Count)
            return false;
        for (var i = 0; i < preset.Count; i++)
        {
            if (preset[i].Kind != current[i].Kind)
                return false;
        }

        return StoredEffectSequence.Matches(preset, current);
    }

    static float[] Downmix(WavAudio audio)
    {
        if (audio.Channels <= 1)
            return audio.Samples;
        var frames = audio.Samples.Length / audio.Channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var channel = 0; channel < audio.Channels; channel++)
                sum += audio.Samples[i * audio.Channels + channel];
            mono[i] = (float)(sum / audio.Channels);
        }

        return mono;
    }

    static bool Flag(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    static double Number(JsonElement body, string name, double fallback) =>
        body.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : fallback;
}
