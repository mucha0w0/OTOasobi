namespace Otoasobi;

// ブランクの位置用。features.loudness_db と、load_wav のピーク正規化に合わせる。
static class FileLoudness
{
    public static double[]? FromFile(string path)
    {
        var audio = WavAudio.Read(path);
        var channels = audio.Channels;
        if (channels < 1 || audio.SampleRate <= 0 || audio.Samples.Length < channels)
            return null;
        var frames = audio.Samples.Length / channels;
        var mono = new double[frames];
        var peak = 0.0;
        for (var i = 0; i < frames; i++)
        {
            var sum = 0.0;
            var at = i * channels;
            for (var channel = 0; channel < channels; channel++)
                sum += audio.Samples[at + channel];
            var sample = sum / channels;
            mono[i] = sample;
            var abs = Math.Abs(sample);
            if (abs > peak)
                peak = abs;
        }

        if (peak > 0)
        {
            var scale = 0.99 / peak;
            for (var i = 0; i < mono.Length; i++)
                mono[i] *= scale;
        }

        return Curve(mono, audio.SampleRate);
    }

    public static double[] Curve(double[] samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Length == 0)
            return [-80, -80];
        var duration = samples.Length / (double)sampleRate;
        var bins = (int)Math.Clamp(Math.Round(duration / 0.008), 2, 2000);
        var edges = new int[bins + 1];
        for (var i = 0; i < bins; i++)
            edges[i] = (int)(samples.Length * (double)i / bins);
        edges[bins] = samples.Length;

        var sum = new double[samples.Length + 1];
        for (var i = 0; i < samples.Length; i++)
            sum[i + 1] = sum[i] + samples[i] * samples[i];

        var rms = new double[bins];
        for (var i = 0; i < bins; i++)
        {
            var count = Math.Max(1, edges[i + 1] - edges[i]);
            var energy = sum[edges[i + 1]] - sum[edges[i]];
            rms[i] = Math.Sqrt(Math.Max(0, energy) / count);
        }

        var hop = Math.Max(duration / bins, 1e-4);
        var smooth = Math.Max(1, (int)Math.Round(0.030 / hop));
        if (smooth > 1)
        {
            if (smooth % 2 == 0)
                smooth++;
            rms = Smooth(rms, smooth);
        }

        var db = new double[rms.Length];
        for (var i = 0; i < rms.Length; i++)
            db[i] = Math.Clamp(20.0 * Math.Log10(Math.Max(rms[i], 1e-8)), -80, 0);
        return db;
    }

    static double[] Smooth(double[] source, int window)
    {
        var pad = window / 2;
        var padded = new double[source.Length + pad * 2];
        for (var i = 0; i < pad; i++)
            padded[i] = source[0];
        Array.Copy(source, 0, padded, pad, source.Length);
        for (var i = 0; i < pad; i++)
            padded[pad + source.Length + i] = source[^1];
        var dest = new double[source.Length];
        var weight = 1.0 / window;
        for (var i = 0; i < dest.Length; i++)
        {
            var total = 0.0;
            for (var k = 0; k < window; k++)
                total += padded[i + k];
            dest[i] = total * weight;
        }

        return dest;
    }
}
