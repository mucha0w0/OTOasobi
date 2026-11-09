namespace Otoasobi;

// ファイルごとのピークを、指定した dB に揃える。無音はそのまま。
sealed class NormalizeEffect : AudioEffect
{
    public const string KindId = "normalize";
    public const double MinDb = -12;
    public const double MaxDb = 0;
    public const double DefaultDb = -1;

    double _targetDb = DefaultDb;

    public override string Kind => KindId;

    public override string Title => "ノーマライズ";

    public double TargetDb
    {
        get => _targetDb;
        set => _targetDb = Clamp(value);
    }

    public static double Clamp(double db)
    {
        if (!double.IsFinite(db))
            return DefaultDb;
        return Math.Clamp(Math.Round(db, 1, MidpointRounding.AwayFromZero), MinDb, MaxDb);
    }

    public override void Reset()
    {
    }

    public override void Process(Span<float> interleaved, int channels, int sampleRate)
    {
        if (!Enabled || channels < 1 || interleaved.Length < channels)
            return;
        float peak = 0;
        foreach (var sample in interleaved)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
                peak = magnitude;
        }

        if (peak < 1e-8f || !float.IsFinite(peak))
            return;
        var gain = (float)(Math.Pow(10, _targetDb / 20) / peak);
        if (!float.IsFinite(gain) || Math.Abs(gain - 1f) < 1e-6f)
            return;
        for (var i = 0; i < interleaved.Length; i++)
        {
            var sample = interleaved[i] * gain;
            interleaved[i] = float.IsFinite(sample) ? sample : 0;
        }
    }
}
