namespace Otoasobi;

// ガイド音。音名は A4 = 440 Hz。録音ファイルへ書く直前に 44.1 kHz のモノラルへ揃える。
static class RecTone
{
    public const int TargetRate = 44100;

    public static readonly string[] Notes =
    [
        "C3", "D3", "E3", "F3", "G3", "A3", "B3",
        "C4", "D4", "E4", "F4", "G4", "A4", "B4",
        "C5", "D5", "E5", "F5", "G5", "A5", "B5",
        "C6",
    ];

    public static bool TryHertz(string note, out double hz)
    {
        hz = 0;
        if (note.Length is < 2 or > 3)
            return false;
        var semis = note[0] switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => -1,
        };
        if (semis < 0)
            return false;
        if (note.Length == 3)
        {
            if (note[1] != '#')
                return false;
            semis++;
        }

        if (note[^1] is < '0' or > '9')
            return false;
        var octave = note[^1] - '0';
        var midi = (octave + 1) * 12 + semis;
        hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0);
        return true;
    }

    public const int DefaultMoras = 5;

    public const int MinMoras = 1;

    public const int MaxMoras = 16;

    public const int DefaultCountBeats = 4;

    public const int MinCountBeats = 1;

    public const int MaxCountBeats = 16;

    public static float[] Tone(double hz, int sampleRate, double seconds)
    {
        if (sampleRate <= 0 || seconds <= 0 || hz <= 0)
            return [];
        var count = Math.Max(1, (int)Math.Round(sampleRate * seconds));
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / sampleRate;
            var env = Math.Exp(-t / 0.35) * Math.Min(1, t / 0.008);
            var wave = Math.Sin(2 * Math.PI * hz * t)
                + 0.35 * Math.Sin(2 * Math.PI * hz * 2 * t)
                + 0.16 * Math.Sin(2 * Math.PI * hz * 3 * t)
                + 0.08 * Math.Sin(2 * Math.PI * hz * 4 * t);
            samples[i] = (float)(wave / 1.6 * env);
        }

        return samples;
    }

    // テンポの二分音符で、同じ音をモーラ数だけ繰り返す。
    public static float[] MoraGuide(double hz, int bpm, int moras, int sampleRate)
    {
        if (sampleRate <= 0 || hz <= 0)
            return [];
        bpm = Math.Clamp(bpm, 40, 240);
        moras = Math.Clamp(moras, MinMoras, MaxMoras);
        var halfSeconds = 120.0 / bpm;
        var perMora = Math.Max(1, (int)Math.Round(sampleRate * halfSeconds));
        var samples = new float[perMora * moras];
        for (var mora = 0; mora < moras; mora++)
        {
            var tone = Tone(hz, sampleRate, halfSeconds);
            var offset = mora * perMora;
            var copy = Math.Min(tone.Length, samples.Length - offset);
            if (copy > 0)
                Array.Copy(tone, 0, samples, offset, copy);
        }

        return samples;
    }

    // 発声前のカウントと、発声中のクリック重ね。
    public static float[] ManualGuide(double hz, int bpm, int moras, int sampleRate, bool preCount, int countBeatsBefore, int countBeatsAfter)
    {
        var mora = MoraGuide(hz, bpm, moras, sampleRate);
        if (mora.Length == 0)
            return mora;
        countBeatsBefore = Math.Clamp(countBeatsBefore, MinCountBeats, MaxCountBeats);
        countBeatsAfter = Math.Clamp(countBeatsAfter, 0, MaxCountBeats);
        float[] samples;
        var moraStart = 0;
        if (preCount)
        {
            var clicks = CountIn(bpm, sampleRate, countBeatsBefore);
            if (clicks.Length == 0)
                samples = mora;
            else
            {
                samples = new float[clicks.Length + mora.Length];
                Array.Copy(clicks, 0, samples, 0, clicks.Length);
                Array.Copy(mora, 0, samples, clicks.Length, mora.Length);
                moraStart = clicks.Length;
            }
        }
        else
            samples = mora;

        if (countBeatsAfter > 0)
            OverlayBeatClicks(samples, moraStart, mora.Length, bpm, sampleRate, countBeatsAfter);
        return samples;
    }

    // 1 小節のクリック。長さは拍数そのものなので、終わった瞬間が録音の開始になる。
    public static float[] CountIn(int bpm, int sampleRate, int beats = DefaultCountBeats)
    {
        if (sampleRate <= 0)
            return [];
        bpm = Math.Clamp(bpm, 40, 240);
        beats = Math.Clamp(beats, MinCountBeats, MaxCountBeats);
        var beat = sampleRate * 60.0 / bpm;
        var count = Math.Max(1, (int)Math.Round(beat * beats));
        var samples = new float[count];
        for (var index = 0; index < beats; index++)
        {
            var start = (int)Math.Round(beat * index);
            if (start >= count)
                break;
            AddClick(samples, start, sampleRate, accent: index == 0);
        }

        return samples;
    }

    static void OverlayBeatClicks(float[] samples, int regionStart, int regionLength, int bpm, int sampleRate, int beatCount)
    {
        if (sampleRate <= 0 || regionLength <= 0 || beatCount <= 0)
            return;
        bpm = Math.Clamp(bpm, 40, 240);
        beatCount = Math.Clamp(beatCount, 0, MaxCountBeats);
        var beat = sampleRate * 60.0 / bpm;
        var regionBeats = Math.Max(0, (int)Math.Floor(regionLength / beat));
        var beats = Math.Min(beatCount, regionBeats);
        for (var index = 0; index < beats; index++)
        {
            var start = regionStart + (int)Math.Round(beat * index);
            if (start >= samples.Length)
                break;
            AddClick(samples, start, sampleRate, accent: index % 2 == 0);
        }
    }

    static void AddClick(float[] samples, int start, int sampleRate, bool accent)
    {
        var hz = accent ? 1568.0 : 1046.0;
        var length = Math.Min(samples.Length - start, (int)(sampleRate * 0.04));
        for (var i = 0; i < length; i++)
        {
            var t = (double)i / sampleRate;
            var sample = (float)(Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-t / 0.012) * 0.8);
            samples[start + i] = Math.Clamp(samples[start + i] + sample, -1f, 1f);
        }
    }

    public static float[] Downmix(float[] interleaved, int channels)
    {
        if (channels <= 1 || interleaved.Length == 0)
            return interleaved;
        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
                sum += interleaved[frame * channels + channel];
            mono[frame] = (float)(sum / channels);
        }

        return mono;
    }

    public static float[] Resample(float[] mono, int fromRate, int toRate)
    {
        if (mono.Length == 0 || fromRate <= 0 || toRate <= 0 || fromRate == toRate)
            return mono;
        return ResampleRatio(mono, (double)toRate / fromRate);
    }

    // ガイド BGM。速さは長さだけ、半音は高さだけを変える。
    public static float[] ShapeGuide(float[] interleaved, int channels, int sampleRate, double speed, int semitones)
    {
        if (interleaved.Length == 0 || channels < 1 || sampleRate <= 0)
            return interleaved;
        speed = Math.Clamp(speed, 0.5, 2);
        semitones = Math.Clamp(semitones, -12, 12);
        if (Math.Abs(speed - 1) < 1e-4 && semitones == 0)
            return interleaved;

        var frames = interleaved.Length / channels;
        if (frames == 0)
            return interleaved;
        var pitch = Math.Pow(2, semitones / 12.0);
        var stretch = pitch / speed;
        if (channels == 1)
            return ShapeChannel(interleaved, pitch, stretch, sampleRate);

        var perChannel = new float[channels][];
        var length = int.MaxValue;
        for (var channel = 0; channel < channels; channel++)
        {
            var samples = new float[frames];
            for (var frame = 0; frame < frames; frame++)
                samples[frame] = interleaved[frame * channels + channel];
            perChannel[channel] = ShapeChannel(samples, pitch, stretch, sampleRate);
            length = Math.Min(length, perChannel[channel].Length);
        }

        var output = new float[length * channels];
        for (var frame = 0; frame < length; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
                output[frame * channels + channel] = perChannel[channel][frame];
        }

        return output;
    }

    static float[] ShapeChannel(float[] mono, double pitch, double stretch, int sampleRate)
    {
        var shifted = Math.Abs(pitch - 1) < 1e-4 ? mono : ResampleRatio(mono, 1 / pitch);
        if (Math.Abs(stretch - 1) < 1e-3)
            return shifted;
        return TimeStretch(shifted, sampleRate, stretch);
    }

    public static float[] ResampleRatio(float[] mono, double lengthRatio)
    {
        if (mono.Length == 0 || lengthRatio <= 0 || Math.Abs(lengthRatio - 1) < 1e-6)
            return mono;
        var frames = Math.Max(1, (int)Math.Round(mono.Length * lengthRatio));
        var output = new float[frames];
        var last = mono.Length - 1;
        for (var i = 0; i < frames; i++)
        {
            var source = i / lengthRatio;
            var index = Math.Min((int)source, last);
            var next = Math.Min(index + 1, last);
            var fraction = source - index;
            output[i] = (float)(mono[index] + (mono[next] - mono[index]) * fraction);
        }

        return output;
    }

    // 50% 重ねの波形合わせ。stretch が 1 より大きいと長くなる。
    public static float[] TimeStretch(float[] input, int sampleRate, double stretch)
    {
        if (input.Length == 0 || sampleRate <= 0 || stretch <= 0 || Math.Abs(stretch - 1) < 1e-3)
            return input;
        var target = Math.Max(1, (int)Math.Round(input.Length * stretch));
        if (input.Length < 64)
            return ResampleRatio(input, stretch);

        var win = 1024;
        if (input.Length < win * 2)
            win = Math.Max(64, input.Length / 4) & ~1;
        var synthesis = win / 2;
        var search = Math.Min(synthesis / 2, Math.Max(8, sampleRate / 1000));
        var corrLen = Math.Min(128, synthesis);
        var window = new float[win];
        for (var i = 0; i < win; i++)
            window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / win)));

        var output = new float[target + win];
        var weight = new float[output.Length];
        var analysis = synthesis / stretch;

        void Add(int source, int dest)
        {
            var count = Math.Min(win, Math.Min(input.Length - source, output.Length - dest));
            for (var i = 0; i < count; i++)
            {
                var gain = window[i];
                output[dest + i] += input[source + i] * gain;
                weight[dest + i] += gain;
            }
        }

        Add(0, 0);
        var outPos = synthesis;
        double inPos = analysis;
        var guard = 0;
        while (outPos < target && guard++ < target)
        {
            var expected = (int)Math.Round(inPos);
            var best = Math.Clamp(expected, 0, Math.Max(0, input.Length - win));
            var from = Math.Max(0, best - search);
            var to = Math.Min(input.Length - win, best + search);
            if (to > from)
            {
                var bestCorr = double.NegativeInfinity;
                for (var candidate = from; candidate <= to; candidate += 4)
                {
                    double corr = 0;
                    var length = Math.Min(corrLen, input.Length - candidate);
                    for (var i = 0; i < length; i++)
                        corr += output[outPos + i] * input[candidate + i];
                    if (corr > bestCorr)
                    {
                        bestCorr = corr;
                        best = candidate;
                    }
                }
            }

            Add(best, outPos);
            outPos += synthesis;
            inPos = best + analysis;
            if (inPos >= input.Length - win)
                break;
        }

        var result = new float[target];
        for (var i = 0; i < target; i++)
            result[i] = weight[i] > 1e-6f ? output[i] / weight[i] : 0;
        return result;
    }

    public static float[] ToMono44100(float[] interleaved, int channels, int sampleRate)
    {
        if (interleaved.Length == 0 || channels < 1 || sampleRate <= 0)
            return [];
        return Resample(Downmix(interleaved, channels), sampleRate, TargetRate);
    }
}
