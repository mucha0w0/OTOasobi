namespace Otoasobi;

// 選んだ無音のスペクトルを、定常ノイズとして抑える。
// 声が大きい帯域はそのままに近い。スライダーの最大でも、ノイズは少し残る。
sealed class NoiseSuppressorEffect : AudioEffect
{
    public const string KindId = "noise-suppressor";
    public const double MinSeconds = 0.3;
    public const double DefaultReductionDb = 8;
    public const double MaxReductionDb = 16;
    public const int FftSize = 2048;
    public const int HopSize = 512;

    // ファイル全体の起伏がこれ未満なら、発声が無い収録とみなす。
    const double FlatRangeDb = 10;
    // これより大きい音は、発声に近いとして範囲に採らない。
    const double VoiceMarginDb = 18;

    static readonly double[] Window = Hann(FftSize);

    double _reductionDb = DefaultReductionDb;
    NoiseProfile? _profile;

    public override string Kind => KindId;

    public override string Title => "ノイズサプレッサー";

    public double ReductionDb
    {
        get => _reductionDb;
        set => _reductionDb = ClampReduction(value);
    }

    public bool HasProfile => _profile != null;

    public static double ClampReduction(double db)
    {
        if (!double.IsFinite(db))
            return DefaultReductionDb;
        var stepped = Math.Round(db * 2, MidpointRounding.AwayFromZero) / 2;
        return Math.Clamp(stepped, 0, MaxReductionDb);
    }

    public void ClearProfile() => _profile = null;

    public void SetProfile(NoiseProfile profile) => _profile = profile;

    public static string? RangeMessage(float[] mono, int sampleRate, double start, double end)
    {
        if (sampleRate <= 0 || mono.Length == 0)
            return "波形がありません。";
        var duration = mono.Length / (double)sampleRate;
        Order(ref start, ref end, duration);
        if (end - start < MinSeconds - 1e-4)
            return "もう少し長く選んでください。";
        if (SoundsLikeVoice(mono, sampleRate, start, end))
            return "そこは無音ではなさそうです。";
        return null;
    }

    public static NoiseProfile BuildProfile(float[] mono, int sampleRate, double start, double end)
    {
        var duration = mono.Length / (double)sampleRate;
        Order(ref start, ref end, duration);
        var startSample = Math.Clamp((int)Math.Floor(start * sampleRate), 0, mono.Length);
        var endSample = Math.Clamp((int)Math.Ceiling(end * sampleRate), 0, mono.Length);
        var bins = FftSize / 2 + 1;
        var power = new double[bins];
        var used = 0;
        var real = new double[FftSize];
        var imag = new double[FftSize];
        for (var pos = 0; pos < mono.Length; pos += HopSize)
        {
            var center = pos + FftSize / 2;
            if (center < startSample || center >= endSample || center >= mono.Length)
                continue;
            Spectrum(mono, pos, real, imag);
            for (var k = 0; k < bins; k++)
                power[k] += real[k] * real[k] + imag[k] * imag[k];
            used++;
        }

        if (used == 0)
            throw new InvalidDataException("無音の範囲からスペクトルを取れませんでした。");
        for (var k = 0; k < bins; k++)
            power[k] /= used;
        return new NoiseProfile(sampleRate, power);
    }

    public static float[] Downmix(float[] interleaved, int channels)
    {
        if (channels < 1)
            throw new InvalidDataException("チャンネルがありません。");
        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
                sum += interleaved[i * channels + channel];
            mono[i] = (float)(sum / channels);
        }

        return mono;
    }

    public override void Reset()
    {
    }

    public override void Process(Span<float> interleaved, int channels, int sampleRate)
    {
        var profile = _profile;
        if (profile == null || _reductionDb <= 0 || channels < 1 || sampleRate <= 0)
            return;
        var frames = interleaved.Length / channels;
        if (frames < 8)
            return;
        var noise = PowerAt(profile, sampleRate);
        var keep = Math.Pow(10, -_reductionDb / 20);
        for (var channel = 0; channel < channels; channel++)
        {
            var mono = new float[frames];
            for (var i = 0; i < frames; i++)
                mono[i] = interleaved[i * channels + channel];
            var processed = Suppress(mono, noise, keep);
            for (var i = 0; i < frames; i++)
                interleaved[i * channels + channel] = processed[i];
        }
    }

    static float[] Suppress(float[] input, double[] noisePower, double keep)
    {
        var length = input.Length;
        var output = new double[length];
        var weight = new double[length];
        var bins = FftSize / 2 + 1;
        var gainState = new double[bins];
        var started = false;
        var real = new double[FftSize];
        var imag = new double[FftSize];
        var gain = new double[bins];
        var subtract = 1 - keep;
        for (var pos = 0; pos < length; pos += HopSize)
        {
            Spectrum(input, pos, real, imag);
            for (var k = 0; k < bins; k++)
            {
                var power = real[k] * real[k] + imag[k] * imag[k];
                var ratio = power > 1e-20 ? noisePower[k] / power : 1;
                if (ratio > 1)
                    ratio = 1;
                // 平均より大きい帯域も目標まで届くよう、推定より多めに引いてから下限で止める。
                gain[k] = 1 - subtract * 2 * ratio;
            }

            SmoothFrequency(gain);
            for (var k = 0; k < bins; k++)
                gain[k] = Math.Clamp(gain[k], keep, 1);
            if (!started)
            {
                gain.AsSpan().CopyTo(gainState);
                started = true;
            }
            else
            {
                for (var k = 0; k < bins; k++)
                {
                    var coeff = gain[k] < gainState[k] ? 0.5 : 0.25;
                    gainState[k] += (gain[k] - gainState[k]) * coeff;
                    gain[k] = Math.Clamp(gainState[k], keep, 1);
                }
            }

            for (var k = 0; k < bins; k++)
            {
                real[k] *= gain[k];
                imag[k] *= gain[k];
            }

            for (var k = 1; k < bins - 1; k++)
            {
                real[FftSize - k] *= gain[k];
                imag[FftSize - k] *= gain[k];
            }

            Fft(real, imag, inverse: true);
            var count = Math.Min(FftSize, length - pos);
            for (var i = 0; i < count; i++)
            {
                output[pos + i] += real[i];
                weight[pos + i] += Window[i];
            }
        }

        var result = new float[length];
        for (var i = 0; i < length; i++)
        {
            var sample = weight[i] > 1e-8 ? output[i] / weight[i] : input[i];
            result[i] = double.IsFinite(sample) ? (float)sample : 0;
        }

        return result;
    }

    static void Spectrum(float[] mono, int pos, double[] real, double[] imag)
    {
        Array.Clear(real);
        Array.Clear(imag);
        var count = Math.Min(FftSize, mono.Length - pos);
        for (var i = 0; i < count; i++)
            real[i] = mono[pos + i] * Window[i];
        Fft(real, imag, inverse: false);
    }

    static double[] PowerAt(NoiseProfile profile, int sampleRate)
    {
        if (profile.SampleRate == sampleRate)
            return profile.Power;
        var bins = profile.Power.Length;
        var power = new double[bins];
        var srcHz = (double)profile.SampleRate / FftSize;
        var dstHz = (double)sampleRate / FftSize;
        for (var k = 0; k < bins; k++)
        {
            var srcPos = dstHz * k / srcHz;
            var psd = Interpolate(profile.Power, srcPos) / srcHz;
            power[k] = psd * dstHz;
        }

        return power;
    }

    static double Interpolate(double[] values, double index)
    {
        if (index <= 0)
            return values[0];
        var last = values.Length - 1;
        if (index >= last)
            return values[last];
        var left = (int)index;
        var fraction = index - left;
        return values[left] * (1 - fraction) + values[left + 1] * fraction;
    }

    static void SmoothFrequency(double[] gain)
    {
        var copy = (double[])gain.Clone();
        for (var k = 0; k < gain.Length; k++)
        {
            double sum = 0;
            var count = 0;
            var from = Math.Max(0, k - 2);
            var to = Math.Min(gain.Length - 1, k + 2);
            for (var j = from; j <= to; j++)
            {
                sum += copy[j];
                count++;
            }

            gain[k] = sum / count;
        }
    }

    static bool SoundsLikeVoice(float[] mono, int sampleRate, double start, double end)
    {
        var frame = Math.Max(1, sampleRate / 50);
        var frames = mono.Length / frame;
        if (frames < 4)
            return false;
        var levels = new double[frames];
        for (var i = 0; i < frames; i++)
            levels[i] = Rms(mono, i * frame, frame);
        var ordered = (double[])levels.Clone();
        Array.Sort(ordered);
        var floor = ordered[Math.Clamp((int)(frames * 0.10), 0, frames - 1)];
        var loud = ordered[Math.Clamp((int)(frames * 0.95), 0, frames - 1)];
        if (loud < 1e-8)
            return false;
        if (loud < floor * DbToGain(FlatRangeDb))
            return false;
        var i0 = Math.Clamp((int)Math.Floor(start * sampleRate), 0, mono.Length);
        var i1 = Math.Clamp((int)Math.Ceiling(end * sampleRate), i0 + 1, mono.Length);
        var selected = Rms(mono, i0, i1 - i0);
        return selected > loud * DbToGain(-VoiceMarginDb);
    }

    static double Rms(float[] mono, int offset, int count)
    {
        if (count <= 0)
            return 0;
        double sum = 0;
        var end = Math.Min(mono.Length, offset + count);
        for (var i = offset; i < end; i++)
            sum += mono[i] * (double)mono[i];
        return Math.Sqrt(sum / count);
    }

    static double DbToGain(double db) => Math.Pow(10, db / 20);

    static void Order(ref double start, ref double end, double duration)
    {
        start = Math.Clamp(start, 0, duration);
        end = Math.Clamp(end, 0, duration);
        if (end < start)
            (start, end) = (end, start);
    }

    static double[] Hann(int length)
    {
        var window = new double[length];
        for (var i = 0; i < length; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / length);
        return window;
    }

    static void Fft(double[] real, double[] imag, bool inverse)
    {
        var n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = 2 * Math.PI / len * (inverse ? 1 : -1);
            var stepRe = Math.Cos(angle);
            var stepIm = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double twiddleRe = 1;
                double twiddleIm = 0;
                for (var j = 0; j < len / 2; j++)
                {
                    var even = i + j;
                    var odd = even + len / 2;
                    var mixedRe = twiddleRe * real[odd] - twiddleIm * imag[odd];
                    var mixedIm = twiddleRe * imag[odd] + twiddleIm * real[odd];
                    real[odd] = real[even] - mixedRe;
                    imag[odd] = imag[even] - mixedIm;
                    real[even] += mixedRe;
                    imag[even] += mixedIm;
                    var nextRe = twiddleRe * stepRe - twiddleIm * stepIm;
                    twiddleIm = twiddleRe * stepIm + twiddleIm * stepRe;
                    twiddleRe = nextRe;
                }
            }
        }

        if (!inverse)
            return;
        for (var i = 0; i < n; i++)
        {
            real[i] /= n;
            imag[i] /= n;
        }
    }
}

sealed class NoiseProfile
{
    public NoiseProfile(int sampleRate, double[] power)
    {
        SampleRate = sampleRate;
        Power = power;
    }

    public int SampleRate { get; }

    public double[] Power { get; }
}

readonly record struct NoiseSource(string Name, string Path, bool FromOriginal);

static class NoiseSources
{
    public static IReadOnlyList<NoiseSource> List(string folder)
    {
        var found = ListOriginal(folder);
        if (found.Count > 0)
            return found;
        return WavFiles(folder, fromOriginal: false);
    }

    public static IReadOnlyList<NoiseSource> ListOriginal(string folder)
    {
        var original = Path.Combine(folder, OriginalLibrary.FolderName);
        return WavFiles(original, fromOriginal: true);
    }

    public static string? Resolve(string folder, string name)
    {
        if (name.Length == 0 || name != Path.GetFileName(name))
            return null;
        foreach (var source in List(folder))
        {
            if (string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase))
                return source.Path;
        }

        return null;
    }

    static List<NoiseSource> WavFiles(string folder, bool fromOriginal)
    {
        var list = new List<NoiseSource>();
        if (!Directory.Exists(folder))
            return list;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(new NoiseSource(Path.GetFileName(path), path, fromOriginal));
        }

        list.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return list;
    }
}

sealed class NoiseCapture
{
    public string Folder { get; set; } = "";

    public string File { get; set; } = "";

    public double Start { get; set; }

    public double End { get; set; }
}

sealed class NoiseCaptureFile
{
    public List<NoiseCapture> Captures { get; set; } = [];
}

static class NoiseCaptureStore
{
    const int MaxCaptures = 30;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "noise-captures.json");

    public static NoiseCapture? Find(string folder)
    {
        var stored = Load();
        return stored.Captures.LastOrDefault(capture => SameFolder(capture.Folder, folder));
    }

    public static void Save(NoiseCapture capture)
    {
        var stored = Load();
        stored.Captures.RemoveAll(item => SameFolder(item.Folder, capture.Folder));
        stored.Captures.Add(capture);
        if (stored.Captures.Count > MaxCaptures)
            stored.Captures.RemoveRange(0, stored.Captures.Count - MaxCaptures);
        Write(stored);
    }

    public static void Remove(string folder)
    {
        var stored = Load();
        if (stored.Captures.RemoveAll(item => SameFolder(item.Folder, folder)) == 0)
            return;
        Write(stored);
    }

    static NoiseCaptureFile Load()
    {
        var stored = JsonFiles.Read<NoiseCaptureFile>(FilePath) ?? new NoiseCaptureFile();
        stored.Captures ??= [];
        return stored;
    }

    static void Write(NoiseCaptureFile stored) => JsonFiles.Write(FilePath, stored);

    static bool SameFolder(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
