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

    // 1 小節のクリック。長さは拍数そのものなので、終わった瞬間が録音の開始になる。
    public static float[] CountIn(int bpm, int sampleRate, int beats = 4)
    {
        if (sampleRate <= 0)
            return [];
        bpm = Math.Clamp(bpm, 40, 240);
        beats = Math.Max(1, beats);
        var beat = sampleRate * 60.0 / bpm;
        var count = Math.Max(1, (int)Math.Round(beat * beats));
        var samples = new float[count];
        for (var index = 0; index < beats; index++)
        {
            var start = (int)Math.Round(beat * index);
            if (start >= count)
                break;
            var hz = index == 0 ? 1568.0 : 1046.0;
            var length = Math.Min(count - start, (int)(sampleRate * 0.04));
            for (var i = 0; i < length; i++)
            {
                var t = (double)i / sampleRate;
                samples[start + i] += (float)(Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-t / 0.012) * 0.8);
            }
        }

        return samples;
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
        var frames = Math.Max(1, (int)Math.Round(mono.Length * (double)toRate / fromRate));
        var output = new float[frames];
        var last = mono.Length - 1;
        for (var i = 0; i < frames; i++)
        {
            var source = i * (double)fromRate / toRate;
            var index = Math.Min((int)source, last);
            var next = Math.Min(index + 1, last);
            var fraction = source - index;
            output[i] = (float)(mono[index] + (mono[next] - mono[index]) * fraction);
        }

        return output;
    }

    public static float[] ToMono44100(float[] interleaved, int channels, int sampleRate)
    {
        if (interleaved.Length == 0 || channels < 1 || sampleRate <= 0)
            return [];
        return Resample(Downmix(interleaved, channels), sampleRate, TargetRate);
    }
}
