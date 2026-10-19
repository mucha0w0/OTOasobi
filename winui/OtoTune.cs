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
        var locked = new bool[PointCount];
        double Pick(int point, double autoAt)
        {
            if (points![point] is double time)
            {
                locked[point] = true;
                return time;
            }

            return autoAt;
        }

        return Place(
            auto,
            Pick(0, auto.Offset),
            Pick(1, auto.Overlap),
            Pick(2, auto.Preutter),
            Pick(3, auto.Consonant),
            Pick(4, auto.Blank),
            locked,
            durationSeconds);
    }

    // 直した点（locked）は、スライダーで隣が動いても絶対時刻を変えない。
    // 空ける必要があるときは、直していない点だけを動かす。oto.py の _apply_line と同じ。
    public static OtoMark Place(
        OtoMark basis,
        double offsetS,
        double overlapS,
        double preutterS,
        double consonantS,
        double blankS,
        bool[] locked,
        double durationS)
    {
        var off = Ms(offsetS);
        var pre = Ms(preutterS);
        var con = Ms(consonantS);
        var end = Ms(blankS);
        var ov = Ms(overlapS);
        var dur = Ms(Math.Max(durationS, 0));
        dur = Math.Max(dur, Math.Max(off, Math.Max(end, Math.Max(con, Math.Max(pre, ov)))));
        if (!locked[0] && off < 0)
            off = 0;
        if (!locked[2] && pre < off)
            pre = off;
        if (!locked[3] && con < off)
            con = off;
        if (!locked[4] && end > dur)
            end = dur;
        var minEnd = OtoMarks.Round3(con + MinGapSeconds * 1000.0);
        if (end < minEnd)
        {
            if (!locked[4] && !locked[3])
            {
                var rel = Math.Max(0, OtoMarks.Round3(con - off));
                var placed = OtoMarks.PlaceBlank(end, off, rel, dur);
                con = OtoMarks.Round3(off + placed.Consonant);
                end = OtoMarks.Round3(off - placed.Blank);
            }
            else if (!locked[4])
            {
                end = minEnd;
            }
            else if (!locked[3])
            {
                con = OtoMarks.Round3(end - MinGapSeconds * 1000.0);
            }
        }

        if (!locked[3] && con < off)
        {
            var floor = off;
            if (locked[4])
                floor = Math.Min(floor, OtoMarks.Round3(end - MinGapSeconds * 1000.0));
            con = floor;
        }

        return basis with
        {
            Offset = off / 1000.0,
            Overlap = ov / 1000.0,
            Preutter = pre / 1000.0,
            Consonant = con / 1000.0,
            Blank = end / 1000.0,
        };
    }

    static double Ms(double seconds) => OtoMarks.Round3(seconds * 1000.0);

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
