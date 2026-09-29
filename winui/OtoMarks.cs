namespace Otoasobi;

// 単独音 oto.ini の位置。計算は oto.py の mono_oto_line に合わせる。
// 各値はファイル先頭からの秒で、設定スライダーを動かすたびに引き直す。
readonly record struct OtoMark(
    string Alias,
    double Offset,
    double Overlap,
    double Preutter,
    double Consonant,
    double Blank)
{
    public double At(int point) => point switch
    {
        0 => Offset,
        1 => Overlap,
        2 => Preutter,
        3 => Consonant,
        _ => Blank,
    };
}

static class OtoMarks
{
    const double MinGapMs = 1;
    const double MaxPadMs = 2000;

    public static void Fill(WavFile file, OtoExport settings, List<OtoMark> dest, double[]? loudness)
    {
        dest.Clear();
        var offsetMs = Bounded(settings.OffsetMs, 200, 0, MaxPadMs);
        var consonantMs = Bounded(settings.ConsonantMs, 200, 0, MaxPadMs);
        var blankAt = Bounded(settings.Blank, 0, -1, 1);
        var overlapAt = Bounded(settings.Overlap, 0.5, 0, 1);
        var spans = file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (!IsVowelOnset(spans[i].Label))
                continue;
            dest.Add(Build(
                file.Name,
                spans,
                i,
                file.Duration,
                offsetMs,
                consonantMs,
                blankAt,
                overlapAt,
                loudness));
        }
    }

    static OtoMark Build(
        string filename,
        List<PhoneSpan> spans,
        int index,
        double duration,
        double offsetMs,
        double consonantMs,
        double blankAt,
        double overlapAt,
        double[]? loudness)
    {
        var vowel = spans[index];
        var alias = vowel.Token;
        if (string.IsNullOrEmpty(alias))
            alias = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(alias))
            alias = vowel.Label;

        var c0 = Ms(ConsonantStart(spans, index, vowel));
        var v0 = Ms(vowel.Start);
        var v1 = Ms(vowel.End);
        var dur = Math.Max(Ms(duration), Math.Max(v1, c0));

        var offset = Math.Max(0, Round3(c0 - offsetMs));
        var preutter = Math.Max(0, Round3(v0 - offset));
        var overlap = Round3((c0 + (v0 - c0) * overlapAt) - offset);
        var consonant = Math.Max(0, Round3((v0 + consonantMs) - offset));
        var blankAtMs = Ms(BlankRelease.At(loudness, duration, spans, index, blankAt));
        var placed = PlaceBlank(blankAtMs, offset, consonant, dur);
        var blank = placed.Blank;
        consonant = placed.Consonant;

        return new OtoMark(
            alias,
            offset / 1000.0,
            (offset + overlap) / 1000.0,
            (offset + preutter) / 1000.0,
            (offset + consonant) / 1000.0,
            (offset - blank) / 1000.0);
    }

    static double ConsonantStart(List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        if (index <= 0)
            return vowel.Start;
        var prev = spans[index - 1];
        if (prev.Token != vowel.Token || string.IsNullOrEmpty(prev.Token))
            return vowel.Start;
        if (IsVowelOnset(prev.Label) || prev.Label is "無音" or "息")
            return vowel.Start;
        return prev.Start;
    }

    static (double Blank, double Consonant) PlaceBlank(
        double blankAt,
        double offset,
        double consonant,
        double durationMs)
    {
        var fixedEnd = Round3(offset + consonant);
        var end = blankAt;
        if (end < Round3(fixedEnd + MinGapMs))
            end = Round3(fixedEnd + MinGapMs);
        if (end > durationMs)
            end = durationMs;
        if (end < Round3(fixedEnd + MinGapMs))
        {
            consonant = Math.Max(0, Round3(end - MinGapMs - offset));
            fixedEnd = Round3(offset + consonant);
            if (end < Round3(fixedEnd + MinGapMs))
                end = Round3(fixedEnd + MinGapMs);
        }

        var blank = Round3(-1 * (end - offset));
        var gap = Round3(-blank - consonant);
        if (gap < MinGapMs)
            blank = Round3(-(consonant + MinGapMs));
        return (blank, consonant);
    }

    static bool IsVowelOnset(string label) =>
        label is "a" or "i" or "u" or "e" or "o" or "N" or "M";

    static double Ms(double seconds) => Round3(seconds * 1000.0);

    static double Round3(double value) => Math.Round(value, 3, MidpointRounding.ToEven);

    static double Bounded(double value, double fallback, double lo, double hi)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(value, lo, hi);
    }
}
