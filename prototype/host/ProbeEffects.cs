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
    const int NoiseColumns = 2048;
    const double QuietWindowSeconds = 0.8;
    const double OpenViewSeconds = 2;
    const double MinViewSeconds = 0.12;

    string? _folder;
    string _noiseFile = "";
    double _noiseStart;
    double _noiseEnd;
    float[] _noiseMono = [];
    int _noiseRate;
    double[] _noiseLo = [];
    double[] _noiseHi = [];
    double _noiseDuration;
    double _noiseViewStart;
    double _noiseViewLength;
    string? _noiseMessage;

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
        ClearNoise();
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

    public string? SetNoise(string? file, double? start, double? end)
    {
        if (string.IsNullOrEmpty(_folder))
            return Remember("フォルダを開いてください。");
        if (string.IsNullOrEmpty(file))
        {
            ClearNoise();
            try
            {
                NoiseCaptureStore.Remove(_folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Remember(ex.Message);
            }

            return null;
        }

        if (start is not double rangeStart || end is not double rangeEnd)
            return OpenNoise(file);
        return TryNoise(file, rangeStart, rangeEnd, save: true);
    }

    public object NoiseWave() => new
    {
        type = "noiseWave",
        file = _noiseFile,
        lo = _noiseLo,
        hi = _noiseHi,
        duration = _noiseDuration,
        viewStart = _noiseViewStart,
        viewLength = _noiseViewLength,
    };

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
            noiseMessage = _noiseMessage,
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

    string? OpenNoise(string file)
    {
        var loaded = ReadNoise(file);
        if (loaded.Error != null)
            return Remember(loaded.Error);
        Adopt(loaded);
        _noise.ClearProfile();
        _noiseStart = 0;
        _noiseEnd = 0;
        _noiseMessage = null;
        try
        {
            NoiseCaptureStore.Remove(_folder!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Remember(ex.Message);
        }

        return null;
    }

    string? TryNoise(string file, double start, double end, bool save)
    {
        var loaded = ReadNoise(file);
        if (loaded.Error != null)
            return Remember(loaded.Error);
        var message = NoiseSuppressorEffect.RangeMessage(loaded.Mono, loaded.Rate, start, end);
        if (message != null)
            return Remember(message);
        try
        {
            _noise.SetProfile(NoiseSuppressorEffect.BuildProfile(loaded.Mono, loaded.Rate, start, end));
        }
        catch (InvalidDataException ex)
        {
            return Remember(ex.Message);
        }

        Adopt(loaded);
        if (end < start)
            (start, end) = (end, start);
        _noiseStart = start;
        _noiseEnd = end;
        _noiseMessage = null;
        if (!save)
            return null;
        try
        {
            NoiseCaptureStore.Save(new NoiseCapture { Folder = _folder!, File = file, Start = start, End = end });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Remember(ex.Message);
        }

        return null;
    }

    NoiseLoad ReadNoise(string file)
    {
        if (string.Equals(_noiseFile, file, StringComparison.OrdinalIgnoreCase) && _noiseMono.Length > 0)
        {
            return new NoiseLoad
            {
                File = _noiseFile,
                Mono = _noiseMono,
                Rate = _noiseRate,
                Lo = _noiseLo,
                Hi = _noiseHi,
                Duration = _noiseDuration,
                ViewStart = _noiseViewStart,
                ViewLength = _noiseViewLength,
            };
        }

        var path = NoiseSources.Resolve(_folder!, file);
        if (path == null)
            return new NoiseLoad { Error = "ノイズの原音を選べませんでした。" };
        WavAudio audio;
        try
        {
            audio = WavAudio.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new NoiseLoad { Error = ex.Message };
        }

        var mono = Downmix(audio);
        var duration = audio.SampleRate <= 0 ? 0 : mono.Length / (double)audio.SampleRate;
        FocusQuiet(mono, audio.SampleRate, duration, out var viewStart, out var viewLength);
        var (lo, hi) = ProbePeaks.Columns(mono, NoiseColumns);
        return new NoiseLoad
        {
            File = file,
            Mono = mono,
            Rate = audio.SampleRate,
            Lo = lo,
            Hi = hi,
            Duration = duration,
            ViewStart = viewStart,
            ViewLength = viewLength,
        };
    }

    void Adopt(NoiseLoad loaded)
    {
        _noiseFile = loaded.File;
        _noiseMono = loaded.Mono;
        _noiseRate = loaded.Rate;
        _noiseLo = loaded.Lo;
        _noiseHi = loaded.Hi;
        _noiseDuration = loaded.Duration;
        _noiseViewStart = loaded.ViewStart;
        _noiseViewLength = loaded.ViewLength;
    }

    void ClearNoise()
    {
        _noise.ClearProfile();
        _noiseFile = "";
        _noiseStart = 0;
        _noiseEnd = 0;
        _noiseMono = [];
        _noiseRate = 0;
        _noiseLo = [];
        _noiseHi = [];
        _noiseDuration = 0;
        _noiseViewStart = 0;
        _noiseViewLength = 0;
        _noiseMessage = null;
    }

    string? Remember(string message)
    {
        _noiseMessage = message;
        return message;
    }

    static void FocusQuiet(float[] mono, int rate, double duration, out double viewStart, out double viewLength)
    {
        if (duration <= 0 || rate <= 0 || mono.Length == 0)
        {
            viewStart = 0;
            viewLength = 1;
            return;
        }

        viewLength = Math.Min(duration, OpenViewSeconds);
        var frame = Math.Max(1, rate / 50);
        var frames = mono.Length / frame;
        if (frames < 2)
        {
            viewStart = 0;
            ClampView(duration, ref viewStart, ref viewLength);
            return;
        }

        var energy = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            var offset = i * frame;
            for (var n = 0; n < frame; n++)
            {
                var sample = mono[offset + n];
                sum += sample * (double)sample;
            }

            energy[i] = sum / frame;
        }

        var window = Math.Clamp((int)Math.Round(QuietWindowSeconds * rate / frame), 1, frames);
        double running = 0;
        for (var i = 0; i < window; i++)
            running += energy[i];
        var best = 0;
        var bestMean = running / window;
        for (var i = window; i < frames; i++)
        {
            running += energy[i] - energy[i - window];
            var mean = running / window;
            if (mean < bestMean)
            {
                bestMean = mean;
                best = i - window + 1;
            }
        }

        var center = (best + window * 0.5) * frame / rate;
        viewStart = center - viewLength / 2;
        ClampView(duration, ref viewStart, ref viewLength);
    }

    static void ClampView(double duration, ref double viewStart, ref double viewLength)
    {
        viewLength = Math.Clamp(viewLength, Math.Min(duration, MinViewSeconds), Math.Max(duration, MinViewSeconds));
        if (viewLength >= duration)
        {
            viewStart = 0;
            viewLength = duration;
            return;
        }

        viewStart = Math.Clamp(viewStart, 0, duration - viewLength);
    }

    sealed class NoiseLoad
    {
        public string File { get; init; } = "";
        public float[] Mono { get; init; } = [];
        public int Rate { get; init; }
        public double[] Lo { get; init; } = [];
        public double[] Hi { get; init; } = [];
        public double Duration { get; init; }
        public double ViewStart { get; init; }
        public double ViewLength { get; init; }
        public string? Error { get; init; }
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

        Pin();
    }

    void Pin()
    {
        _chain.SetOrder(new AudioEffect[] { _noise, _highPass, _equalizer, _compressor, _normalize });
    }

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
