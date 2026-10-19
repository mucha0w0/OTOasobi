namespace Otoasobi;

// ドラッグで直した oto.ini の位置。自動推定は OtoMarks、ここは上書きだけを載せる。
// 時刻はファイル先頭からの秒。書き出しは oto.py の apply_file_adjustments と同じ手順。
static class OtoTune
{
    public const int PointCount = 5;
    public const double MagnetSeconds = 0.0005;
    public const double MinGapSeconds = 0.001;
    public const double MinOverlapSeconds = -0.1;

    public static bool Has(double?[]? points)
    {
        if (points == null)
            return false;
        foreach (var point in points)
        {
            if (point != null)
                return true;
        }

        return false;
    }

    public static OtoMark Apply(OtoMark auto, double?[]? points, double durationSeconds)
    {
        if (!Has(points))
            return auto;
        return Place(
            auto,
            points![0] ?? auto.Offset,
            points[1] ?? auto.Overlap,
            points[2] ?? auto.Preutter,
            points[3] ?? auto.Consonant,
            points[4] ?? auto.Blank,
            durationSeconds);
    }

    public static OtoMark Place(
        OtoMark basis,
        double offsetS,
        double overlapS,
        double preutterS,
        double consonantS,
        double blankS,
        double durationS)
    {
        var off = Math.Max(0, OtoMarks.Round3(offsetS * 1000.0));
        var preAt = OtoMarks.Round3(preutterS * 1000.0);
        var conAt = OtoMarks.Round3(consonantS * 1000.0);
        var end = OtoMarks.Round3(blankS * 1000.0);
        var ovAt = OtoMarks.Round3(overlapS * 1000.0);
        var dur = OtoMarks.Round3(Math.Max(durationS, 0) * 1000.0);
        dur = Math.Max(dur, Math.Max(off, Math.Max(end, Math.Max(conAt, Math.Max(preAt, ovAt)))));
        preAt = Math.Max(off, preAt);
        var conRel = Math.Max(0, OtoMarks.Round3(conAt - off));
        var placed = OtoMarks.PlaceBlank(end, off, conRel, dur);
        var preRel = Math.Max(0, OtoMarks.Round3(preAt - off));
        ovAt = Math.Clamp(ovAt, -100, dur);
        var ovRel = OtoMarks.Round3(ovAt - off);
        return basis with
        {
            Offset = off / 1000.0,
            Overlap = (off + ovRel) / 1000.0,
            Preutter = (off + preRel) / 1000.0,
            Consonant = (off + placed.Consonant) / 1000.0,
            Blank = (off - placed.Blank) / 1000.0,
        };
    }

    // 重なっている線は、同じ位置の値をまとめて動かす。止まっている線は壁になる。
    public static double ClampGroup(OtoMark mark, bool[] stack, double time, double duration)
    {
        var hi = Math.Max(Math.Max(duration, 0), mark.Blank);
        var overlapOnly = stack[1] && !stack[0] && !stack[2] && !stack[3] && !stack[4];
        if (overlapOnly)
            return RoundSeconds(Math.Clamp(time, MinOverlapSeconds, hi));

        var lo = 0.0;
        if (!stack[0] && (stack[2] || stack[3] || stack[4]))
            lo = Math.Max(lo, mark.Offset);
        if (!stack[2] && stack[0])
            hi = Math.Min(hi, mark.Preutter);
        if (!stack[3] && stack[0])
            hi = Math.Min(hi, mark.Consonant);
        if (!stack[3] && stack[4])
            lo = Math.Max(lo, mark.Consonant + MinGapSeconds);
        if (!stack[4] && (stack[0] || stack[3]))
            hi = Math.Min(hi, mark.Blank - MinGapSeconds);
        if (lo > hi)
            lo = hi;
        return RoundSeconds(Math.Clamp(time, lo, hi));
    }

    public static double RoundSeconds(double seconds) =>
        OtoMarks.Round3(seconds * 1000.0) / 1000.0;
}
