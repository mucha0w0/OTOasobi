using System.Text.Json;
using Otoasobi;

namespace Otoasobi.Probe;

sealed class ProbeFrq
{
    FrqFile? _file;
    string? _path;
    int _sampleRate;

    public void Clear()
    {
        _file = null;
        _path = null;
        _sampleRate = 0;
    }

    public object View(string? wavPath)
    {
        Clear();
        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
            return Empty(Path.GetFileName(wavPath ?? "") ?? "", 0, null);
        var rate = FrqFile.TryWavSampleRate(wavPath) ?? 44100;
        var frqPath = Path.ChangeExtension(wavPath, ".frq");
        if (!File.Exists(frqPath))
            return Empty(Path.GetFileName(wavPath), rate, null);
        try
        {
            _file = FrqFile.Read(frqPath);
            _path = frqPath;
            _sampleRate = rate;
            return Snapshot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Empty(Path.GetFileName(wavPath), rate, ex.Message);
        }
    }

    public object? Stroke(JsonElement points)
    {
        if (_file == null || points.ValueKind != JsonValueKind.Array)
            return null;
        (int Frame, double Midi, bool Unvoiced)? previous = null;
        foreach (var item in points.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!item.TryGetProperty("frame", out var frameValue) || !frameValue.TryGetInt32(out var frame) || frame < 0)
                continue;
            var midi = item.TryGetProperty("midi", out var midiValue) && midiValue.TryGetDouble(out var midiParsed) ? midiParsed : 0;
            var unvoiced = item.TryGetProperty("unvoiced", out var unvoicedValue) && unvoicedValue.ValueKind == JsonValueKind.True;
            if (previous == null)
                FrqPitch.Stroke(_file.Frequency, frame, midi, unvoiced, frame, midi, unvoiced);
            else
                FrqPitch.Stroke(_file.Frequency, previous.Value.Frame, previous.Value.Midi, previous.Value.Unvoiced, frame, midi, unvoiced);
            previous = (frame, midi, unvoiced);
        }

        return previous == null ? Snapshot() : Commit();
    }

    public object? Shift(JsonElement frames, int octaves)
    {
        if (_file == null)
            return null;
        FrqPitch.ShiftOctaves(_file.Frequency, Frames(frames), octaves);
        return Commit();
    }

    public object? ClearPitch(JsonElement frames)
    {
        if (_file == null)
            return null;
        FrqPitch.Clear(_file.Frequency, Frames(frames));
        return Commit();
    }

    public static (int Wavs, int Frqs) Count(string folder)
    {
        var wavs = 0;
        var frqs = 0;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                continue;
            wavs++;
            if (File.Exists(Path.ChangeExtension(path, ".frq")))
                frqs++;
        }

        return (wavs, frqs);
    }

    object Commit()
    {
        try
        {
            _file!.Write(_path!);
            return Snapshot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new { type = "frqView", missing = false, name = Path.GetFileName(_path ?? ""), hop = _file!.Hop, sampleRate = _sampleRate, frequency = _file.Frequency, error = ex.Message };
        }
    }

    object Snapshot() => new
    {
        type = "frqView",
        missing = false,
        name = Path.GetFileNameWithoutExtension(_path ?? ""),
        hop = _file?.Hop ?? 0,
        sampleRate = _sampleRate,
        frequency = _file?.Frequency ?? [],
        error = (string?)null,
    };

    static object Empty(string name, int sampleRate, string? error) => new
    {
        type = "frqView",
        missing = true,
        name,
        hop = 0,
        sampleRate,
        frequency = Array.Empty<double>(),
        error,
    };

    static List<int> Frames(JsonElement frames)
    {
        var indices = new List<int>();
        if (frames.ValueKind != JsonValueKind.Array)
            return indices;
        foreach (var item in frames.EnumerateArray())
        {
            if (item.TryGetInt32(out var frame))
                indices.Add(frame);
        }

        return indices;
    }
}
