namespace Otoasobi;

// フォルダ読込み時に、WAV を 44100Hz・16bit・モノラルへ書き換える。
// すでにその形式の PCM は開くだけで、書き戻さない。
static class WavStandard
{
    public const int SampleRate = 44100;
    public const int BitsPerSample = 16;
    public const int Channels = 1;

    const int Lobes = 16;
    const int KernelSteps = 8192;
    static readonly double[] Kernel = BuildKernel();

    public static void Convert(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            if (IsCanonical(path))
                return;

            var audio = WavAudio.Read(path);
            if (audio.Channels < 1 || audio.Samples.Length < audio.Channels)
                throw new InvalidDataException($"{name} に波形がありません。");

            var mono = audio.Channels == 1 ? audio.Samples : Downmix(audio.Samples, audio.Channels);
            var samples = audio.SampleRate == SampleRate ? mono : Resample(mono, audio.SampleRate, SampleRate);
            if (samples.Length == 0)
                throw new InvalidDataException($"{name} に波形がありません。");

            var standard = new WavAudio
            {
                SampleRate = SampleRate,
                Channels = Channels,
                BitsPerSample = BitsPerSample,
                Float = false,
                Samples = samples,
            };
            var temp = path + ".converting";
            try
            {
                WavAudio.Write(temp, standard);
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                if (File.Exists(temp))
                    File.Delete(temp);
                throw;
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"{name} は WAV として読めません。", ex);
        }
    }

    public static bool IsCanonical(string path)
    {
        var name = Path.GetFileName(path);
        using var reader = new BinaryReader(File.OpenRead(path));
        var stream = reader.BaseStream;
        if (stream.Length < 12)
            throw new InvalidDataException($"{name} は WAV として読めません。");
        var riff = reader.ReadUInt32();
        reader.ReadUInt32();
        var wave = reader.ReadUInt32();
        if (riff != 0x46464952 || wave != 0x45564157)
            throw new InvalidDataException($"{name} は WAV として読めません。");

        ushort format = 0;
        ushort channels = 0;
        var sampleRate = 0;
        ushort bits = 0;
        ushort blockAlign = 0;
        var sawFmt = false;
        long dataBytes = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = reader.ReadUInt32();
            var chunkSize = reader.ReadUInt32();
            if (chunkSize > 512L * 1024 * 1024)
                throw new InvalidDataException($"{name} は大きすぎます。");
            var start = stream.Position;
            if (start + chunkSize > stream.Length)
                throw new InvalidDataException($"{name} は WAV として読めません。");
            if (id == 0x20746D66)
            {
                if (chunkSize < 16)
                    throw new InvalidDataException($"{name} は WAV として読めません。");
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                blockAlign = reader.ReadUInt16();
                bits = reader.ReadUInt16();
                sawFmt = true;
            }
            else if (id == 0x61746164)
            {
                dataBytes = chunkSize;
            }

            var skip = chunkSize - (stream.Position - start);
            if (skip > 0)
                stream.Position += skip;
            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                stream.Position++;
        }

        return sawFmt
            && format == 1
            && channels == Channels
            && sampleRate == SampleRate
            && bits == BitsPerSample
            && blockAlign == Channels * (BitsPerSample / 8)
            && dataBytes >= blockAlign;
    }

    static float[] Downmix(float[] interleaved, int channels)
    {
        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            var offset = i * channels;
            for (var channel = 0; channel < channels; channel++)
                sum += interleaved[offset + channel];
            mono[i] = (float)(sum / channels);
        }

        return mono;
    }

    static float[] Resample(float[] source, int sourceRate, int targetRate)
    {
        var scaled = source.Length * (double)targetRate / sourceRate;
        if (scaled > int.MaxValue - 1)
            throw new InvalidDataException("変換後の波形が大きすぎます。");
        var outLength = Math.Max(1, (int)Math.Round(scaled));
        var dest = new float[outLength];
        var cutoff = Math.Min(1.0, (double)targetRate / sourceRate);
        var half = (int)Math.Ceiling(Lobes / cutoff);
        var step = (double)sourceRate / targetRate;
        var length = source.Length;
        for (var i = 0; i < outLength; i++)
        {
            var srcPos = i * step;
            var center = (int)Math.Floor(srcPos);
            double sum = 0;
            double norm = 0;
            var from = center - half;
            var to = center + half;
            for (var j = from; j <= to; j++)
            {
                var weight = KernelAt((j - srcPos) * cutoff);
                if (weight == 0 || (uint)j >= (uint)length)
                    continue;
                sum += source[j] * weight;
                norm += weight;
            }

            dest[i] = Math.Abs(norm) < 1e-12 ? 0 : (float)(sum / norm);
        }

        return dest;
    }

    static double KernelAt(double x)
    {
        var ax = Math.Abs(x);
        if (ax >= Lobes)
            return 0;
        var pos = ax / Lobes * KernelSteps;
        var index = (int)pos;
        var frac = pos - index;
        return Kernel[index] + (Kernel[index + 1] - Kernel[index]) * frac;
    }

    static double[] BuildKernel()
    {
        var table = new double[KernelSteps + 1];
        for (var i = 0; i <= KernelSteps; i++)
        {
            var x = Lobes * i / (double)KernelSteps;
            table[i] = Sinc(x) * Blackman(x);
        }

        return table;
    }

    static double Sinc(double x)
    {
        if (x < 1e-12)
            return 1;
        var pix = Math.PI * x;
        return Math.Sin(pix) / pix;
    }

    static double Blackman(double x)
    {
        var u = 0.5 * (1.0 - x / Lobes);
        var angle = 2 * Math.PI * u;
        return 0.42 - 0.5 * Math.Cos(angle) + 0.08 * Math.Cos(2 * angle);
    }
}
