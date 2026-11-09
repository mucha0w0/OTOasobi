namespace Otoasobi;

// 音素の強弱を軽く揃える。大きめの部分だけを下げ、息や小さい音は持ち上げない。
// 立ち上がりは遅めなので、子音の頭は残る。戻りは、音が自然に切れる速さ。
// ファイル全体を見て、下げ幅が設定を超えないようにする。
sealed class CompressorEffect : AudioEffect
{
    public const string KindId = "compressor";
    public const double MinRatio = 2;
    public const double MaxRatio = 3;
    public const double DefaultRatio = 2.5;
    public const double MinReductionDb = 0;
    public const double MaxReductionDb = 6;
    public const double DefaultReductionDb = 3;

    // 子音の立ち上がり（およそ 10〜20 ms）が揃う前に、ゲインが下がりきらない速さ。
    public const double AttackMs = 25;
    // 音節が消えるのに近い戻り。長く残さず、急にも戻さない。
    public const double ReleaseMs = 120;
    const double KneeDb = 6;
    // ピークからこの範囲だけを、声の強弱として見る。それより小さい息は計算に入れない。
    const double ActivityDb = 24;
    // 大きめの部分を、声の中心より下まで押し下げない。
    const double KeepDb = 0.5;

    double _ratio = DefaultRatio;
    double _reductionDb = DefaultReductionDb;

    public override string Kind => KindId;

    public override string Title => "コンプレッサー";

    public double Ratio
    {
        get => _ratio;
        set => _ratio = ClampRatio(value);
    }

    public double ReductionDb
    {
        get => _reductionDb;
        set => _reductionDb = ClampReduction(value);
    }

    public static double ClampRatio(double ratio)
    {
        if (!double.IsFinite(ratio))
            return DefaultRatio;
        var stepped = Math.Round(ratio, 1, MidpointRounding.AwayFromZero);
        return Math.Clamp(stepped, MinRatio, MaxRatio);
    }

    public static double ClampReduction(double db)
    {
        if (!double.IsFinite(db))
            return DefaultReductionDb;
        var stepped = Math.Round(db * 2, MidpointRounding.AwayFromZero) / 2;
        return Math.Clamp(stepped, MinReductionDb, MaxReductionDb);
    }

    public override void Reset()
    {
    }

    public override void Process(Span<float> interleaved, int channels, int sampleRate)
    {
        for (var i = 0; i < interleaved.Length; i++)
        {
            if (!float.IsFinite(interleaved[i]))
                interleaved[i] = 0;
        }

        if (!Enabled || _reductionDb <= 0 || channels < 1 || sampleRate <= 0)
            return;
        var frames = interleaved.Length / channels;
        if (frames < 8)
            return;

        var attack = Coeff(AttackMs, sampleRate);
        var release = Coeff(ReleaseMs, sampleRate);
        if (!TryMeasure(interleaved, channels, frames, sampleRate, attack, release, out var thresholdDb, out var depthDb))
            return;

        var env = 0.0;
        for (var n = 0; n < frames; n++)
        {
            env = Follow(env, PowerAt(interleaved, n, channels), attack, release);
            var gain = Gain(env, thresholdDb, _ratio, depthDb);
            var index = n * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                var sample = interleaved[index + ch] * gain;
                interleaved[index + ch] = double.IsFinite(sample) ? (float)sample : 0;
            }
        }
    }

    bool TryMeasure(
        ReadOnlySpan<float> interleaved,
        int channels,
        int frames,
        int sampleRate,
        double attack,
        double release,
        out double thresholdDb,
        out double depthDb)
    {
        thresholdDb = 0;
        depthDb = 0;
        var hop = Math.Max(1, sampleRate / 1000);
        // 立ち上がりの途中は、小さい音素と見分けない。
        var skip = (int)(sampleRate * AttackMs * 3 / 1000);
        var levels = new List<double>(frames / hop + 2);
        var env = 0.0;
        var peak = 0.0;
        for (var n = 0; n < frames; n++)
        {
            env = Follow(env, PowerAt(interleaved, n, channels), attack, release);
            if (env > peak)
                peak = env;
            if (n < skip)
                continue;
            if (n % hop == 0 || n == frames - 1)
                levels.Add(env);
        }

        if (peak < 1e-12 || levels.Count == 0)
            return false;
        var floor = peak * Math.Pow(10, -ActivityDb / 10);
        var kept = new List<double>(levels.Count);
        foreach (var power in levels)
        {
            if (power >= floor && power > 1e-16)
                kept.Add(10 * Math.Log10(power));
        }

        if (kept.Count < 12)
            return false;
        kept.Sort();
        // 長い母音に中央値が寄っても、短めの小さい部分との差は見る。
        var body = Percentile(kept, 0.20);
        var loud = Percentile(kept, 0.95);
        var depth = Math.Min(_reductionDb, Math.Max(0, loud - body - KeepDb));
        if (depth < 0.25)
            return false;

        var slope = 1 - 1 / _ratio;
        thresholdDb = loud - depth / slope;
        depthDb = depth;
        return true;
    }

    static double Gain(double power, double thresholdDb, double ratio, double depthDb)
    {
        if (power <= 1e-16)
            return 1;
        var levelDb = 10 * Math.Log10(power);
        var slope = 1 - 1 / ratio;
        var half = KneeDb * 0.5;
        var over = levelDb - thresholdDb;
        double reduction;
        if (over <= -half)
            reduction = 0;
        else if (over >= half)
            reduction = over * slope;
        else
        {
            var x = over + half;
            reduction = x * x / (2 * KneeDb) * slope;
        }

        if (reduction < 0)
            reduction = 0;
        if (reduction > depthDb)
            reduction = depthDb;
        var gain = Math.Pow(10, -reduction / 20);
        return double.IsFinite(gain) ? gain : 1;
    }

    static double PowerAt(ReadOnlySpan<float> interleaved, int frame, int channels)
    {
        var index = frame * channels;
        var power = 0.0;
        for (var ch = 0; ch < channels; ch++)
        {
            var sample = interleaved[index + ch];
            if (!float.IsFinite(sample))
                continue;
            var next = (double)sample * sample;
            if (next > power)
                power = next;
        }

        return power;
    }

    static double Follow(double env, double power, double attack, double release)
    {
        var coeff = power > env ? attack : release;
        env += (power - env) * coeff;
        if (env < 1e-20 || !double.IsFinite(env))
            return 0;
        return env;
    }

    static double Coeff(double milliseconds, int sampleRate)
    {
        var samples = milliseconds * 0.001 * sampleRate;
        if (samples < 1)
            return 1;
        return 1 - Math.Exp(-1 / samples);
    }

    static double Percentile(List<double> sorted, double position)
    {
        var index = (int)Math.Round((sorted.Count - 1) * position, MidpointRounding.AwayFromZero);
        if (index < 0)
            index = 0;
        if (index >= sorted.Count)
            index = sorted.Count - 1;
        return sorted[index];
    }
}
