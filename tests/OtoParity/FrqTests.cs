using System.Buffers.Binary;
using Otoasobi;

namespace OtoParity;

public class FrqTests
{
    [Fact]
    public void RoundTripKeepsHopPairsAndAverage()
    {
        var frequency = new[] { 0.0, 100.0, 300.0, 0.0 };
        var amplitude = new[] { 0.0, 0.2, 0.4, 0.1 };
        var file = new FrqFile(128, frequency, amplitude);
        Assert.Equal(200, file.Average(), 6);

        var bytes = file.Encode();
        Assert.Equal("FREQ0003"u8.ToArray(), bytes[..8]);
        Assert.Equal(40 + frequency.Length * 16, bytes.Length);
        Assert.Equal(128, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(200, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(12)), 6);
        Assert.Equal(new byte[16], bytes[20..36]);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(36)));

        var dir = Path.Combine(Path.GetTempPath(), "oto-frq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.frq");
            file.Write(path);
            Assert.False(File.Exists(path + ".writing"));
            var loaded = FrqFile.Read(path);
            Assert.Equal(128, loaded.Hop);
            Assert.Equal(frequency, loaded.Frequency);
            Assert.Equal(amplitude, loaded.Amplitude);

            loaded.Frequency[1] = 50;
            loaded.Write(path);
            var again = FrqFile.Read(path);
            Assert.Equal(175, again.Average(), 6);
            Assert.Equal(0.2, again.Amplitude[1], 6);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EmptyFileAndRejectedBytes()
    {
        var empty = new FrqFile(256, [], []);
        var dir = Path.Combine(Path.GetTempPath(), "oto-frq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "empty.frq");
            empty.Write(path);
            var loaded = FrqFile.Read(path);
            Assert.Empty(loaded.Frequency);
            Assert.Equal(0, loaded.Average());
            File.WriteAllBytes(path, "FREQ0002"u8.ToArray());
            var format = Assert.Throws<InvalidDataException>(() => FrqFile.Read(path));
            Assert.Equal("frq の形式ではありません。", format.Message);
            var broken = new FrqFile(256, [1.0], [0.2]).Encode();
            File.WriteAllBytes(path, broken[..^1]);
            var length = Assert.Throws<InvalidDataException>(() => FrqFile.Read(path));
            Assert.Equal("frq の長さが合いません。", length.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PitchConvertsAndOctaveStaysOnTheKeyboard()
    {
        Assert.Equal(69, FrqPitch.ToMidi(440), 6);
        Assert.Equal(81, FrqPitch.ToMidi(880), 6);
        Assert.Equal(440, FrqPitch.ToHertz(69), 6);
        Assert.False(FrqPitch.IsBlack(60));
        Assert.True(FrqPitch.IsBlack(61));
        Assert.Equal("C4", FrqPitch.NoteLabel(60));
        Assert.Equal("C-1", FrqPitch.NoteLabel(0));
        Assert.Equal("G9", FrqPitch.NoteLabel(127));

        var low = FrqPitch.ToHertz(100);
        var top = FrqPitch.ToHertz(120);
        var edge = FrqPitch.ToHertz(FrqPitch.MidiHigh - 12);
        var values = new[] { 0.0, low, top, edge };
        Assert.True(FrqPitch.ShiftOctaves(values, [0, 1, 2, 3], 1));
        Assert.Equal(0, values[0]);
        Assert.Equal(low * 2, values[1], 4);
        Assert.Equal(top, values[2]);
        Assert.InRange(FrqPitch.ToMidi(values[3]), FrqPitch.MidiHigh - 0.02, FrqPitch.MidiHigh + 0.02);

        Assert.True(FrqPitch.ShiftOctaves(values, [1], -1));
        Assert.Equal(low, values[1], 4);
        Assert.False(FrqPitch.ShiftOctaves(values, [0], 1));
        Assert.False(FrqPitch.ShiftOctaves(values, [1], 0));
    }

    [Fact]
    public void ClearAndStrokeRewriteOnlyTheAskedFrames()
    {
        var frequency = new[] { 10.0, 20.0, 30.0 };
        Assert.True(FrqPitch.Clear(frequency, [0, 2, 9]));
        Assert.Equal([0.0, 20, 0], frequency);
        Assert.False(FrqPitch.Clear(frequency, [0]));

        var stroke = new double[5];
        FrqPitch.Stroke(stroke, 0, 69, false, 4, 81, false);
        Assert.Equal(440, stroke[0], 4);
        Assert.Equal(FrqPitch.ToHertz(75), stroke[2], 4);
        Assert.Equal(880, stroke[4], 4);

        var same = new[] { 100.0 };
        FrqPitch.Stroke(same, 0, 60, false, 0, 69, false);
        Assert.Equal(440, same[0], 4);

        var into = new double[5];
        Array.Fill(into, 100);
        FrqPitch.Stroke(into, 0, 0, true, 4, 69, false);
        Assert.Equal(0, into[0]);
        Assert.Equal(0, into[1]);
        Assert.Equal(440, into[2], 4);
        Assert.Equal(440, into[4], 4);

        var drop = new[] { 440.0, 440.0, 440.0, 440.0, 440.0 };
        FrqPitch.Stroke(drop, 0, 69, false, 4, 0, true);
        Assert.Equal(440, drop[0], 4);
        Assert.Equal(440, drop[1], 4);
        Assert.Equal(0, drop[2]);
        Assert.Equal(0, drop[4]);

        var both = new[] { 50.0, 50.0 };
        FrqPitch.Stroke(both, 0, 0, true, 1, 0, true);
        Assert.Equal([0.0, 0], both);
    }

    [Fact]
    public void RectangleSelectsCentersAndTheUnvoicedLane()
    {
        var view = new FrqView
        {
            MidiLow = FrqPitch.MidiLow,
            MidiHigh = FrqPitch.MidiHigh,
            PixelsPerFrame = 10,
            ScrollX = 0,
            ScrollY = 589,
            LaneTop = 500,
        };
        var midi = 60.0;
        Assert.Equal(midi, view.MidiAt(view.ViewportY(midi)), 6);
        Assert.Equal(3, view.FrameAt(view.ViewportX(3)));

        var frequency = new[] { 440.0, 0.0, 880.0 };
        var note = view.ViewportY(FrqPitch.ToMidi(440));
        var x = view.ViewportX(0);
        var hits = new List<int>();
        view.Collect(frequency, x - 1, note - 1, x + 1, note + 1, hits);
        Assert.Equal([0], hits);

        var lane = view.LaneTop + FrqPitch.Unvoiced / 2;
        view.Collect(frequency, view.ViewportX(1) - 2, lane - 2, view.ViewportX(1) + 2, lane + 2, hits);
        Assert.Equal([1], hits);

        view.Collect(
            frequency,
            view.ViewportX(0) - 1,
            Math.Min(note, view.ViewportY(FrqPitch.ToMidi(880))) - 1,
            view.ViewportX(2) + 1,
            lane + 1,
            hits);
        Assert.Equal([0, 1, 2], hits);

        view.Collect(frequency, view.ViewportX(2) + 20, note, view.ViewportX(2) + 30, note + 4, hits);
        Assert.Empty(hits);
    }

    [Fact]
    public void WavHeaderReportsTheSampleRate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oto-frq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.wav");
            File.WriteAllBytes(path, Wav(44100));
            Assert.Equal(44100, FrqFile.TryWavSampleRate(path));
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            Assert.Null(FrqFile.TryWavSampleRate(path));
            Assert.Null(FrqFile.TryWavSampleRate(Path.Combine(dir, "missing.wav")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    static byte[] Wav(int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46464952);
        writer.Write(38);
        writer.Write(0x45564157);
        writer.Write(0x20746D66);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write(0x61746164);
        writer.Write(2);
        writer.Write((ushort)0);
        return stream.ToArray();
    }
}
