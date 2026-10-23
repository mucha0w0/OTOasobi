using System.Buffers.Binary;
using System.Globalization;

namespace Otoasobi;

// UTAU の FREQ0003。otoasobi/frq.py と同じ並び。高さ 0 は音程なし。
public sealed class FrqFile
{
    public const int MaxFrames = 5_000_000;

    public int Hop { get; }

    public double[] Frequency { get; }

    public double[] Amplitude { get; }

    public FrqFile(int hop, double[] frequency, double[] amplitude)
    {
        Hop = hop;
        Frequency = frequency;
        Amplitude = amplitude;
    }

    public double Average()
    {
        double sum = 0;
        var count = 0;
        foreach (var hz in Frequency)
        {
            if (hz > 0 && double.IsFinite(hz))
            {
                sum += hz;
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    public static FrqFile Read(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 40 || !data.AsSpan(0, 8).SequenceEqual("FREQ0003"u8))
            throw new InvalidDataException("frq の形式ではありません。");
        var hop = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(36));
        if (count < 0 || count > MaxFrames || data.Length != 40 + count * 16)
            throw new InvalidDataException("frq の長さが合いません。");
        var frequency = new double[count];
        var amplitude = new double[count];
        for (var i = 0; i < count; i++)
        {
            frequency[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(40 + i * 16));
            amplitude[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(48 + i * 16));
        }

        return new FrqFile(hop, frequency, amplitude);
    }

    public void Write(string path)
    {
        var blob = Encode();
        var tmp = path + ".writing";
        try
        {
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (IOException)
            {
            }
        }
    }

    public byte[] Encode()
    {
        if (Amplitude.Length != Frequency.Length)
            throw new InvalidDataException("周波数と振幅の長さが違います。");
        var count = Frequency.Length;
        if (count > MaxFrames)
            throw new InvalidDataException("frq の長さが合いません。");
        var bytes = new byte[40 + count * 16];
        "FREQ0003"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Hop);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(12), Average());
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), count);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(40 + i * 16), Frequency[i]);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(48 + i * 16), Amplitude[i]);
        }

        return bytes;
    }

    public static int? TryWavSampleRate(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12)
                return null;
            if (reader.ReadUInt32() != 0x46464952)
                return null;
            reader.ReadUInt32();
            if (reader.ReadUInt32() != 0x45564157)
                return null;
            while (stream.Position + 8 <= stream.Length)
            {
                var id = reader.ReadUInt32();
                var size = reader.ReadUInt32();
                if (id == 0x20746D66)
                {
                    if (size < 16 || stream.Position + 16 > stream.Length)
                        return null;
                    reader.ReadUInt16();
                    reader.ReadUInt16();
                    var rate = reader.ReadInt32();
                    return rate > 0 ? rate : null;
                }

                var skip = size + (size & 1);
                if (skip < 0 || stream.Position > stream.Length - skip)
                    return null;
                stream.Position += skip;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        return null;
    }
}

static class FrqPitch
{
    // C-1 から G9。推定の 50〜1100Hz と、その上下数オクターブが入る。
    public const int MidiLow = 0;

    public const int MidiHigh = 127;

    public const double Row = 14;

    public const double KeyWidth = 54;

    public const double Ruler = 20;

    public const double Unvoiced = 22;

    public static bool IsBlack(int midi)
    {
        var degree = midi % 12;
        if (degree < 0)
            degree += 12;
        return degree is 1 or 3 or 6 or 8 or 10;
    }

    public static string NoteLabel(int midi)
    {
        var names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var degree = midi % 12;
        if (degree < 0)
            degree += 12;
        var octave = (int)Math.Floor(midi / 12.0) - 1;
        return names[degree] + octave.ToString(CultureInfo.InvariantCulture);
    }

    public static double ToMidi(double hertz) => 69 + 12 * Math.Log2(hertz / 440);

    public static double ToHertz(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    public static bool ShiftOctaves(double[] frequency, IEnumerable<int> indices, int octaves)
    {
        if (octaves == 0)
            return false;
        var factor = Math.Pow(2, octaves);
        var changed = false;
        foreach (var index in indices)
        {
            if ((uint)index >= (uint)frequency.Length)
                continue;
            var hz = frequency[index];
            if (!(hz > 0) || !double.IsFinite(hz))
                continue;
            var next = hz * factor;
            if (!double.IsFinite(next))
                continue;
            var midi = ToMidi(next);
            // 鍵盤の外へは出さない。境界は Hz の丸めで半音より少しはみ出す。
            if (!double.IsFinite(midi) || midi < MidiLow - 0.02 || midi > MidiHigh + 0.02)
                continue;
            frequency[index] = next;
            changed = true;
        }

        return changed;
    }

    public static bool Clear(double[] frequency, IEnumerable<int> indices)
    {
        var changed = false;
        foreach (var index in indices)
        {
            if ((uint)index >= (uint)frequency.Length)
                continue;
            if (frequency[index] == 0)
                continue;
            frequency[index] = 0;
            changed = true;
        }

        return changed;
    }

    // 同じフレームのときは、あとのサンプル（いまのポインタ）を書く。
    // 片側が音程なしのときは、近い側の半分を 0 にし、音程のあいだは補間しない。
    public static void Stroke(
        double[] frequency,
        int frameA,
        double midiA,
        bool unvoicedA,
        int frameB,
        double midiB,
        bool unvoicedB)
    {
        if (frequency.Length == 0)
            return;
        if (frameA == frameB)
        {
            Write(frequency, frameA, midiB, unvoicedB);
            return;
        }

        var span = (double)(frameB - frameA);
        var from = Math.Min(frameA, frameB);
        var to = Math.Max(frameA, frameB);
        for (var frame = from; frame <= to; frame++)
        {
            if ((uint)frame >= (uint)frequency.Length)
                continue;
            var t = (frame - frameA) / span;
            if (unvoicedA && unvoicedB)
            {
                frequency[frame] = 0;
                continue;
            }

            if (unvoicedA || unvoicedB)
            {
                var voiced = unvoicedA ? midiB : midiA;
                var unvoiced = unvoicedA ? t < 0.5 : t >= 0.5;
                frequency[frame] = unvoiced ? 0 : ToHertz(voiced);
                continue;
            }

            frequency[frame] = ToHertz(midiA + (midiB - midiA) * t);
        }
    }

    static void Write(double[] frequency, int frame, double midi, bool unvoiced)
    {
        if ((uint)frame >= (uint)frequency.Length)
            return;
        frequency[frame] = unvoiced ? 0 : ToHertz(midi);
    }
}

readonly struct FrqView
{
    public int MidiLow { get; init; }

    public int MidiHigh { get; init; }

    public double PixelsPerFrame { get; init; }

    public double ScrollX { get; init; }

    public double ScrollY { get; init; }

    public double LaneTop { get; init; }

    public double ViewportX(int frame) =>
        FrqPitch.KeyWidth + (frame + 0.5) * PixelsPerFrame - ScrollX;

    public double ViewportY(double midi) =>
        FrqPitch.Ruler + (MidiHigh - midi + 0.5) * FrqPitch.Row - ScrollY;

    public int FrameAt(double viewportX)
    {
        if (PixelsPerFrame <= 0)
            return 0;
        return (int)Math.Floor((viewportX + ScrollX - FrqPitch.KeyWidth) / PixelsPerFrame);
    }

    public double MidiAt(double viewportY) =>
        MidiHigh + 0.5 - (viewportY + ScrollY - FrqPitch.Ruler) / FrqPitch.Row;

    public void Collect(double[] frequency, double x0, double y0, double x1, double y1, List<int> into)
    {
        into.Clear();
        if (frequency.Length == 0 || PixelsPerFrame <= 0 || MidiHigh < MidiLow)
            return;
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);
        var laneCenter = LaneTop + FrqPitch.Unvoiced / 2;
        var lane = laneCenter >= top && laneCenter <= bottom;
        for (var i = 0; i < frequency.Length; i++)
        {
            var x = ViewportX(i);
            if (x < FrqPitch.KeyWidth || x < left || x > right)
                continue;
            var hz = frequency[i];
            if (!(hz > 0) || !double.IsFinite(hz))
            {
                if (lane)
                    into.Add(i);
                continue;
            }

            var y = ViewportY(FrqPitch.ToMidi(hz));
            if (y < FrqPitch.Ruler || y >= LaneTop)
                continue;
            if (y >= top && y <= bottom)
                into.Add(i);
        }
    }
}
