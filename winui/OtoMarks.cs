namespace Otoasobi;

// oto.ini の位置。計算は oto.py の mono_oto_line / renzoku_oto_line に合わせる。
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
    const double MaxOffsetShiftMs = 100;
    const double MaxBlankShiftS = 0.1;
    // 連続音のオフセットの 0。音素の開始から手前へこの幅。oto.py の RENZOKU_OFFSET_LEAD_MS。
    const double RenzokuOffsetLeadMs = 150;

    public static void Fill(WavFile file, OtoExport settings, List<OtoMark> dest, double[]? loudness)
    {
        dest.Clear();
        var offsetMs = Bounded(settings.OffsetMs, 0, -MaxOffsetShiftMs, MaxOffsetShiftMs);
        var consonantMs = Bounded(settings.ConsonantMs, 200, 0, MaxPadMs);
        var blankAt = Bounded(settings.Blank, 0, -MaxBlankShiftS, MaxBlankShiftS);
        var renzoku = settings.Mode == "renzoku";
        var overlapAt = Bounded(settings.OverlapAt, 0.5, 0, 1);
        var restOffsetMs = Bounded(settings.RestOffsetMs, 0, -MaxOffsetShiftMs, MaxOffsetShiftMs);
        var restConsonantMs = Bounded(settings.RestConsonantMs, 200, 0, MaxPadMs);
        var restBlank = Bounded(settings.RestBlank, 0, -MaxBlankShiftS, MaxBlankShiftS);
        var spans = file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (!IsVowelOnset(spans[i].Label))
                continue;
            dest.Add(renzoku
                ? BuildRenzoku(
                    file.Name,
                    spans,
                    i,
                    file.Duration,
                    offsetMs,
                    consonantMs,
                    blankAt,
                    overlapAt,
                    restOffsetMs,
                    restConsonantMs,
                    restBlank,
                    settings.RestOverlap,
                    loudness)
                : Build(
                    file.Name,
                    spans,
                    i,
                    file.Duration,
                    offsetMs,
                    consonantMs,
                    blankAt,
                    settings.Overlap,
                    loudness));
        }
    }

    static OtoMark BuildRenzoku(
        string filename,
        List<PhoneSpan> spans,
        int index,
        double duration,
        double offsetMs,
        double consonantMs,
        double blankAt,
        double overlapAt,
        double restOffsetMs,
        double restConsonantMs,
        double restBlank,
        IReadOnlyDictionary<string, double>? restOverlap,
        double[]? loudness)
    {
        var vowel = spans[index];
        var alias = RenzokuAlias(filename, spans, index, vowel);
        if (FollowsRest(spans, index, vowel))
        {
            var mono = Build(
                filename,
                spans,
                index,
                duration,
                restOffsetMs,
                restConsonantMs,
                restBlank,
                restOverlap ?? new Dictionary<string, double>(),
                loudness);
            return mono with { Alias = alias };
        }

        var start = Ms(ConsonantStart(spans, index, vowel));
        var v0 = Ms(vowel.Start);
        var v1 = Ms(vowel.End);
        var dur = Math.Max(Ms(duration), Math.Max(v1, start));

        var offset = Math.Max(0, Round3(start - RenzokuOffsetLeadMs - offsetMs));
        var preutter = Math.Max(0, Round3(v0 - offset));
        // 0% はオフセットの位置、100% は音素の開始。
        var overlapMs = Round3((start - offset) * overlapAt);
        var consonant = Math.Max(0, Round3((v0 + consonantMs) - offset));
        var blankAtMs = Ms(BlankRelease.At(loudness, duration, spans, index, blankAt));
        var placed = PlaceBlank(blankAtMs, offset, consonant, dur);
        var blank = placed.Blank;
        consonant = placed.Consonant;

        return new OtoMark(
            alias,
            offset / 1000.0,
            (offset + overlapMs) / 1000.0,
            (offset + preutter) / 1000.0,
            (offset + consonant) / 1000.0,
            (offset - blank) / 1000.0);
    }

    static string RenzokuAlias(string filename, List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        var alias = vowel.Token;
        if (string.IsNullOrEmpty(alias))
            alias = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(alias))
            alias = vowel.Label;
        return PreviousVowelLetter(spans, index) + " " + alias;
    }

    static string PreviousVowelLetter(List<PhoneSpan> spans, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var span = spans[i];
            if (IsVowelOnset(span.Label))
                return span.Label is "N" or "M" ? "n" : span.Label;
            if (span.Label is "無音" or "息" && IsRestToken(span.Token))
                return "-";
        }

        return "-";
    }

    static bool FollowsRest(List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        var cursor = index - 1;
        while (cursor >= 0)
        {
            var span = spans[cursor];
            if (span.Token == vowel.Token
                && !string.IsNullOrEmpty(span.Token)
                && !IsVowelOnset(span.Label)
                && span.Label is not ("無音" or "息"))
            {
                cursor--;
                continue;
            }

            break;
        }

        if (cursor < 0)
            return false;
        var prev = spans[cursor];
        return prev.Label == "無音" && IsRestToken(prev.Token);
    }

    static bool IsRestToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;
        foreach (var ch in token)
        {
            if (ch != '_')
                return false;
        }

        return true;
    }

    static OtoMark Build(
        string filename,
        List<PhoneSpan> spans,
        int index,
        double duration,
        double offsetMs,
        double consonantMs,
        double blankAt,
        IReadOnlyDictionary<string, double> overlap,
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
        var overlapMs = ConsonantOverlap.FromOffset(
            c0,
            v0,
            offset,
            ConsonantOverlap.EngineSetting(overlap, ConsonantLabel(spans, index, vowel)));
        var consonant = Math.Max(0, Round3((v0 + consonantMs) - offset));
        var blankAtMs = Ms(BlankRelease.At(loudness, duration, spans, index, blankAt));
        var placed = PlaceBlank(blankAtMs, offset, consonant, dur);
        var blank = placed.Blank;
        consonant = placed.Consonant;

        return new OtoMark(
            alias,
            offset / 1000.0,
            (offset + overlapMs) / 1000.0,
            (offset + preutter) / 1000.0,
            (offset + consonant) / 1000.0,
            (offset - blank) / 1000.0);
    }

    static string? ConsonantLabel(List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        if (index <= 0)
            return null;
        var prev = spans[index - 1];
        if (prev.Token != vowel.Token || string.IsNullOrEmpty(prev.Token))
            return null;
        if (IsVowelOnset(prev.Label) || prev.Label is "無音" or "息")
            return null;
        return prev.Label;
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
