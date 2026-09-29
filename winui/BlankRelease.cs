namespace Otoasobi;

// ブランクの右脚。計算は otoasobi/blank.py の release_blank_seconds に合わせる。
static class BlankRelease
{
    const double AttackSkipS = 0.050;
    const double SmoothS = 0.080;
    const double MinWindowS = 0.080;
    const double BodyMarginDb = 12;
    const double BaseToleranceDb = 1.5;
    const double MaxToleranceDb = 6;
    const double LateGapDb = 8;
    const double DropDb = 12;

    public static double At(
        double[]? loudness,
        double duration,
        List<PhoneSpan> spans,
        int index,
        double ratio)
    {
        var vowelEnd = spans[index].End;
        if (loudness == null || loudness.Length < 2 || duration <= 0)
            return vowelEnd;
        if (vowelEnd - spans[index].Start < MinWindowS)
            return vowelEnd;

        var count = loudness.Length;
        var hop = duration / count;
        var heavy = Smooth(loudness, hop);
        var found = Anchors(heavy, loudness, hop, spans[index].Start, WindowEnd(spans, index, duration));
        if (found == null)
            return vowelEnd;
        var placed = Math.Clamp(ratio, 0, 1);
        return found.Value.T0 + placed * (found.Value.T1 - found.Value.T0);
    }

    static double WindowEnd(List<PhoneSpan> spans, int index, double duration)
    {
        var end = spans[index].End;
        for (var i = index + 1; i < spans.Count; i++)
        {
            if (spans[i].Label is not ("無音" or "息"))
                break;
            if (spans[i].End > end)
                end = spans[i].End;
        }

        return Math.Min(duration, end);
    }

    static (double T0, double T1)? Anchors(
        double[] heavy,
        double[] light,
        double hop,
        double tStart,
        double tEnd)
    {
        if (tEnd - tStart < MinWindowS || heavy.Length < 2)
            return null;
        var levelFrom = tStart + AttackSkipS;
        if (tEnd - levelFrom < MinWindowS)
            levelFrom = tStart;

        var windowH = new List<double>();
        var scanT = new List<double>();
        var scanH = new List<double>();
        var scanL = new List<double>();
        for (var i = 0; i < heavy.Length; i++)
        {
            var time = (i + 0.5) * hop;
            if (time < tStart - 1e-6 || time > tEnd + 1e-6)
                continue;
            windowH.Add(heavy[i]);
            if (time < levelFrom - 1e-6)
                continue;
            scanT.Add(time);
            scanH.Add(heavy[i]);
            scanL.Add(light[i]);
        }

        if (windowH.Count < 4 || scanH.Count < 3)
            return null;

        var floor = Percentile(windowH, 10);
        var above = new List<double>();
        foreach (var value in scanH)
        {
            if (value > floor + BodyMarginDb)
                above.Add(value);
        }

        var top = Percentile(above.Count >= 3 ? above : scanH, 75);
        var third = scanL.Count / 3;
        double width = 0;
        if (scanL.Count - third - third >= 3)
        {
            var residual = new double[scanL.Count - third - third];
            for (var i = 0; i < residual.Length; i++)
                residual[i] = scanL[third + i] - scanH[third + i];
            width = Percentile(residual, 90) - Percentile(residual, 10);
        }

        var tolerance = Math.Min(MaxToleranceDb, Math.Max(BaseToleranceDb, width));
        var threshold = top - tolerance;
        var deep = top - LateGapDb;
        var entered = false;
        double? t0 = null;
        for (var i = 0; i < scanT.Count; i++)
        {
            if (entered && scanH[i] <= deep)
                break;
            if (scanH[i] >= threshold)
            {
                entered = true;
                t0 = scanT[i];
            }
        }

        if (t0 == null)
            return null;

        var t1 = tEnd;
        var drop = top - DropDb;
        for (var i = 0; i < scanT.Count; i++)
        {
            if (scanT[i] + 1e-9 < t0.Value)
                continue;
            if (scanH[i] <= drop)
            {
                t1 = scanT[i];
                break;
            }
        }

        if (t1 < t0.Value)
            t1 = t0.Value;
        return (t0.Value, t1);
    }

    static double[] Smooth(double[] source, double hop)
    {
        var win = hop > 0 ? (int)Math.Round(SmoothS / hop) : 1;
        if (win % 2 == 0)
            win += 1;
        if (win <= 1 || source.Length < win)
            return (double[])source.Clone();
        var pad = win / 2;
        var dest = new double[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            double sum = 0;
            for (var k = -pad; k <= pad; k++)
            {
                var j = i + k;
                if (j < 0)
                    j = 0;
                else if (j >= source.Length)
                    j = source.Length - 1;
                sum += source[j];
            }

            dest[i] = sum / win;
        }

        return dest;
    }

    static double Percentile(IReadOnlyList<double> values, double q)
    {
        var sorted = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
            sorted[i] = values[i];
        Array.Sort(sorted);
        if (sorted.Length == 1)
            return sorted[0];
        var pos = (sorted.Length - 1) * (q / 100.0);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        if (lo == hi)
            return sorted[lo];
        var frac = pos - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }
}
