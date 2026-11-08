namespace Otoasobi.Probe;

using Otoasobi;

// プレビューの波形だけを、解析プロセスの外で WAV から作る。
static class ProbeWave
{
    public const int Columns = 1024;

    public sealed class Audio
    {
        public int SampleRate { get; init; }
        public float[] Mono { get; init; } = [];
        public double Duration => SampleRate > 0 ? Mono.Length / (double)SampleRate : 0;
    }

    public static Audio Load(string path)
    {
        var wav = WavAudio.Read(path);
        var frames = wav.Channels > 0 ? wav.Samples.Length / wav.Channels : 0;
        var mono = new float[frames];
        if (wav.Channels <= 1)
        {
            Array.Copy(wav.Samples, mono, frames);
        }
        else
        {
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                var offset = frame * wav.Channels;
                for (var channel = 0; channel < wav.Channels; channel++)
                    sum += wav.Samples[offset + channel];
                mono[frame] = (float)(sum / wav.Channels);
            }
        }

        return new Audio { SampleRate = wav.SampleRate, Mono = mono };
    }

    public static (double[] Lo, double[] Hi, double T0, double T1) Peaks(Audio audio, double t0, double t1, int columns)
    {
        var count = audio.Mono.Length;
        var rate = audio.SampleRate;
        var duration = audio.Duration;
        if (t1 <= t0)
        {
            t0 = 0;
            t1 = duration;
        }

        t0 = Math.Clamp(t0, 0, duration);
        t1 = Math.Clamp(t1, t0, duration);
        columns = Math.Clamp(columns, 1, Columns);
        if (count == 0 || rate <= 0 || t1 <= t0)
            return ([], [], t0, t1);

        var start = (int)Math.Clamp(Math.Floor(t0 * rate), 0, count - 1);
        var end = (int)Math.Clamp(Math.Ceiling(t1 * rate), start + 1, count);
        var samples = end - start;
        var bins = Math.Min(columns, samples);
        var lo = new double[bins];
        var hi = new double[bins];
        for (var bin = 0; bin < bins; bin++)
        {
            var from = start + (int)((long)bin * samples / bins);
            var to = start + (int)((long)(bin + 1) * samples / bins);
            if (to <= from)
                to = from + 1;
            var min = audio.Mono[from];
            var max = min;
            for (var index = from + 1; index < to; index++)
            {
                var value = audio.Mono[index];
                if (value < min)
                    min = value;
                if (value > max)
                    max = value;
            }

            lo[bin] = min;
            hi[bin] = max;
        }

        return (lo, hi, start / (double)rate, end / (double)rate);
    }
}

// 読み込んだ波形を直近のファイル分残す。時刻か大きさが変わったら捨てる。
sealed class ProbeWaveCache
{
    public const int Keep = 12;

    readonly object _gate = new();
    readonly Dictionary<string, LinkedListNode<Entry>> _nodes = new(StringComparer.OrdinalIgnoreCase);
    readonly LinkedList<Entry> _order = new();
    int _epoch;

    sealed class Entry
    {
        public required string Path { get; init; }
        public long Length { get; init; }
        public long WriteTicks { get; init; }
        public required ProbeWave.Audio Audio { get; init; }
    }

    public int Epoch
    {
        get
        {
            lock (_gate)
                return _epoch;
        }
    }

    public int Bump()
    {
        lock (_gate)
        {
            _order.Clear();
            _nodes.Clear();
            return ++_epoch;
        }
    }

    public ProbeWave.Audio? TryGet(string path)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(path, out var node))
                return null;
            if (!SameFile(path, node.Value))
            {
                _order.Remove(node);
                _nodes.Remove(path);
                return null;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Audio;
        }
    }

    public void Store(string path, ProbeWave.Audio audio, int epoch)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        if (!info.Exists)
            return;
        lock (_gate)
        {
            if (epoch != _epoch)
                return;
            if (_nodes.TryGetValue(path, out var existing))
            {
                _order.Remove(existing);
                _nodes.Remove(path);
            }

            var node = _order.AddFirst(new Entry
            {
                Path = path,
                Length = info.Length,
                WriteTicks = info.LastWriteTimeUtc.Ticks,
                Audio = audio,
            });
            _nodes[path] = node;
            while (_order.Last != null && _order.Count > Keep)
            {
                var last = _order.Last;
                _order.RemoveLast();
                _nodes.Remove(last.Value.Path);
            }
        }
    }

    static bool SameFile(string path, Entry entry)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == entry.Length && info.LastWriteTimeUtc.Ticks == entry.WriteTicks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
