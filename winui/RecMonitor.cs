namespace Otoasobi;

// マイクモニターの直近波形。呼び出し側がロックする。
sealed class RecLiveBuffer
{
    public const int WindowSeconds = 3;

    float[] _samples = [];
    int _rate;
    int _write;
    int _count;

    public void Clear()
    {
        _samples = [];
        _rate = 0;
        _write = 0;
        _count = 0;
    }

    public void Write(ReadOnlySpan<float> mono, int rate)
    {
        if (rate <= 0 || mono.Length == 0)
            return;
        var capacity = rate * WindowSeconds;
        if (rate != _rate || _samples.Length != capacity)
        {
            _rate = rate;
            _samples = new float[capacity];
            _write = 0;
            _count = 0;
        }

        foreach (var sample in mono)
        {
            _samples[_write] = sample;
            _write++;
            if (_write == _samples.Length)
                _write = 0;
            if (_count < _samples.Length)
                _count++;
        }
    }

    // 常に窓の長さ。まだ埋まっていない左側は 0 で、新しい音は右端に来る。
    public (float[] Samples, int Rate) Snapshot()
    {
        if (_samples.Length == 0)
            return ([], _rate);
        var copy = new float[_samples.Length];
        if (_count == 0)
            return (copy, _rate);
        var start = _write - _count;
        if (start < 0)
            start += _samples.Length;
        var offset = _samples.Length - _count;
        var tail = Math.Min(_count, _samples.Length - start);
        Array.Copy(_samples, start, copy, offset, tail);
        if (tail < _count)
            Array.Copy(_samples, 0, copy, offset + tail, _count - tail);
        return (copy, _rate);
    }
}

// 録音中の波形を、64 サンプルごとの山で持つ。
sealed class RecPeakTrack
{
    public const int Bucket = 64;

    readonly List<float> _min = [];
    readonly List<float> _max = [];
    int _pending;
    float _pendingMin;
    float _pendingMax;
    int _samples;

    public int Samples => _samples;

    public void Clear()
    {
        _min.Clear();
        _max.Clear();
        _pending = 0;
        _samples = 0;
    }

    public void Append(ReadOnlySpan<float> mono)
    {
        foreach (var sample in mono)
        {
            var value = Math.Clamp(sample, -1f, 1f);
            if (_pending == 0)
            {
                _pendingMin = value;
                _pendingMax = value;
            }
            else
            {
                if (value < _pendingMin)
                    _pendingMin = value;
                if (value > _pendingMax)
                    _pendingMax = value;
            }

            _pending++;
            _samples++;
            if (_pending == Bucket)
            {
                _min.Add(_pendingMin);
                _max.Add(_pendingMax);
                _pending = 0;
            }
        }
    }

    public (float[] Min, float[] Max) Snapshot()
    {
        var extra = _pending > 0 ? 1 : 0;
        var min = new float[_min.Count + extra];
        var max = new float[_max.Count + extra];
        for (var i = 0; i < _min.Count; i++)
        {
            min[i] = _min[i];
            max[i] = _max[i];
        }

        if (extra == 1)
        {
            min[^1] = _pendingMin;
            max[^1] = _pendingMax;
        }

        return (min, max);
    }
}
