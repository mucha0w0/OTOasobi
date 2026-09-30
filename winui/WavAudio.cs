namespace Otoasobi;

// プロジェクト直下へ書き戻すための WAV。チャンネル数、サンプリング周波数、ビット数を保つ。
sealed class WavAudio
{
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitsPerSample { get; init; }
    public bool Float { get; init; }
    public float[] Samples { get; init; } = [];

    public static WavAudio Read(string path)
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
        var guid = Guid.Empty;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = reader.ReadUInt32();
            var chunkSize = reader.ReadUInt32();
            if (chunkSize > 512L * 1024 * 1024)
                throw new InvalidDataException($"{name} は大きすぎます。");
            var start = stream.Position;
            if (id == 0x20746D66)
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                blockAlign = reader.ReadUInt16();
                bits = reader.ReadUInt16();
                if (chunkSize >= 40 && format == 0xFFFE)
                {
                    reader.ReadUInt16();
                    reader.ReadUInt16();
                    reader.ReadUInt32();
                    guid = new Guid(reader.ReadBytes(16));
                }
            }
            else if (id == 0x61746164)
            {
                data = reader.ReadBytes((int)chunkSize);
            }

            var skip = chunkSize - (stream.Position - start);
            if (skip > 0)
                stream.Position += skip;
            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                stream.Position++;
        }

        if (data == null || data.Length == 0)
            throw new InvalidDataException($"{name} に波形がありません。");
        var encoding = format == 0xFFFE ? guid.ToByteArray()[0] | (guid.ToByteArray()[1] << 8) : format;
        var isFloat = encoding == 3;
        var isPcm = encoding == 1;
        if ((!isFloat && !isPcm) || channels is < 1 or > 16 || sampleRate <= 0)
            throw new InvalidDataException($"{name} の形式には対応していません。");
        if (bits is not (8 or 16 or 24 or 32) || (isFloat && bits != 32))
            throw new InvalidDataException($"{name} のビット数には対応していません。");
        var sampleBytes = bits / 8;
        if (blockAlign < channels * sampleBytes)
            throw new InvalidDataException($"{name} の形式には対応していません。");

        var frames = data.Length / blockAlign;
        var samples = new float[frames * channels];
        for (var frame = 0; frame < frames; frame++)
        {
            var frameOffset = frame * blockAlign;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = frameOffset + channel * sampleBytes;
                samples[frame * channels + channel] = Decode(data, offset, bits, isFloat);
            }
        }

        return new WavAudio
        {
            SampleRate = sampleRate,
            Channels = channels,
            BitsPerSample = bits,
            Float = isFloat,
            Samples = samples,
        };
    }

    public static void Write(string path, WavAudio audio)
    {
        var sampleBytes = audio.BitsPerSample / 8;
        var block = sampleBytes * audio.Channels;
        var dataBytes = audio.Samples.Length * sampleBytes;
        var buffer = new byte[dataBytes];
        for (var i = 0; i < audio.Samples.Length; i++)
            Encode(buffer.AsSpan(i * sampleBytes, sampleBytes), audio.Samples[i], audio.BitsPerSample, audio.Float);

        var fmtSize = 16;
        var riffSize = 4 + (8 + fmtSize) + (8 + dataBytes);
        if (audio.Float)
            riffSize += 12;
        if ((dataBytes & 1) == 1)
            riffSize++;

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x46464952);
        writer.Write(riffSize);
        writer.Write(0x45564157);
        writer.Write(0x20746D66);
        writer.Write(fmtSize);
        writer.Write((ushort)(audio.Float ? 3 : 1));
        writer.Write((ushort)audio.Channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.SampleRate * block);
        writer.Write((ushort)block);
        writer.Write((ushort)audio.BitsPerSample);
        if (audio.Float)
        {
            writer.Write(0x74636166);
            writer.Write(4);
            writer.Write(audio.Samples.Length / audio.Channels);
        }

        writer.Write(0x61746164);
        writer.Write(dataBytes);
        writer.Write(buffer);
        if ((dataBytes & 1) == 1)
            writer.Write((byte)0);
    }

    static float Decode(byte[] data, int offset, int bits, bool isFloat)
    {
        if (isFloat)
            return BitConverter.ToSingle(data, offset);
        if (bits == 8)
            return (data[offset] - 128) / 128f;
        if (bits == 16)
            return BitConverter.ToInt16(data, offset) / 32768f;
        if (bits == 24)
        {
            var value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
            if ((value & 0x800000) != 0)
                value |= unchecked((int)0xFF000000);
            return value / 8388608f;
        }

        return BitConverter.ToInt32(data, offset) / 2147483648f;
    }

    static void Encode(Span<byte> dest, float sample, int bits, bool isFloat)
    {
        if (!float.IsFinite(sample))
            sample = 0;
        if (isFloat)
        {
            BitConverter.TryWriteBytes(dest, sample);
            return;
        }

        sample = Math.Clamp(sample, -1f, 1f);
        if (bits == 8)
        {
            dest[0] = (byte)Math.Clamp((int)Math.Round(sample * 127f + 128f), 0, 255);
            return;
        }

        if (bits == 16)
        {
            var value = (short)Math.Clamp((int)Math.Round(sample * 32767f), -32768, 32767);
            BitConverter.TryWriteBytes(dest, value);
            return;
        }

        if (bits == 24)
        {
            var value = Math.Clamp((int)Math.Round(sample * 8388607f), -8388608, 8388607);
            dest[0] = (byte)value;
            dest[1] = (byte)(value >> 8);
            dest[2] = (byte)(value >> 16);
            return;
        }

        var wide = (int)Math.Clamp(Math.Round(sample * 2147483647.0), int.MinValue, int.MaxValue);
        BitConverter.TryWriteBytes(dest, wide);
    }
}
