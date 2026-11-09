namespace Otoasobi;

// oto.ini の位置。計算は oto.py の mono_oto_line / renzoku_oto_line / cvvc_oto_lines に合わせる。
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
    // CVVC の子音音素も同じ。
    const double RenzokuOffsetLeadMs = 150;
    // CVVC の子音音素。子音部とブランクの初期値。oto.py の VC_PART_AT。
    const double VcPartAt = 1;

    public static void Fill(WavFile file, OtoExport settings, List<OtoMark> dest, double[]? loudness)
    {
        dest.Clear();
        var offsetMs = Bounded(settings.OffsetMs, 0, -MaxOffsetShiftMs, MaxOffsetShiftMs);
        var consonantMs = Bounded(settings.ConsonantMs, 200, 0, MaxPadMs);
        var blankAt = Bounded(settings.Blank, 0, -MaxBlankShiftS, MaxBlankShiftS);
        var renzoku = settings.Mode == "renzoku";
        var cvvc = settings.Mode == "cvvc";
        var overlapAt = Bounded(settings.OverlapAt, 0.5, 0, 1);
        var restOffsetMs = Bounded(settings.RestOffsetMs, 0, -MaxOffsetShiftMs, MaxOffsetShiftMs);
        var restConsonantMs = Bounded(settings.RestConsonantMs, 200, 0, MaxPadMs);
        var restBlank = Bounded(settings.RestBlank, 0, -MaxBlankShiftS, MaxBlankShiftS);
        var leadOffsetMs = Bounded(settings.LeadOffsetMs, 0, -MaxOffsetShiftMs, MaxOffsetShiftMs);
        var leadConsonantMs = Bounded(settings.LeadConsonantMs, 200, 0, MaxPadMs);
        var leadBlank = Bounded(settings.LeadBlank, 0, -MaxBlankShiftS, MaxBlankShiftS);
        var vcOffsetMs = Bounded(settings.VcOffsetMs, 0, -MaxOffsetShiftMs, MaxOffsetShiftMs);
        var vcOverlapAt = Bounded(settings.VcOverlapAt, 0.5, 0, 1);
        var spans = file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (!IsVowelOnset(spans[i].Label))
                continue;
            if (cvvc)
            {
                AddCvvc(
                    dest,
                    file.Name,
                    spans,
                    i,
                    file.Duration,
                    consonantMs,
                    blankAt,
                    settings.Overlap,
                    leadOffsetMs,
                    leadConsonantMs,
                    leadBlank,
                    settings.LeadOverlap,
                    vcOffsetMs,
                    settings.VcConsonant,
                    settings.VcBlank,
                    vcOverlapAt,
                    loudness);
                continue;
            }

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

    static void AddCvvc(
        List<OtoMark> dest,
        string filename,
        List<PhoneSpan> spans,
        int index,
        double duration,
        double consonantMs,
        double blankAt,
        IReadOnlyDictionary<string, double> overlap,
        double leadOffsetMs,
        double leadConsonantMs,
        double leadBlank,
        IReadOnlyDictionary<string, double>? leadOverlap,
        double vcOffsetMs,
        IReadOnlyDictionary<string, double>? vcConsonant,
        IReadOnlyDictionary<string, double>? vcBlank,
        double vcOverlapAt,
        double[]? loudness)
    {
        var vowel = spans[index];
        var alias = MoraAlias(filename, vowel);
        if (IsVowelPhoneme(vowel.Token))
        {
            // あいうえおんは連続音と同じ位置。アンダーバーの直後だけ、先頭音素の単独音。
            var shown = PreviousVowelLetter(spans, index) == "-" ? "- " + alias : alias;
            if (FollowsRest(spans, index, vowel))
            {
                dest.Add(Build(
                    filename,
                    spans,
                    index,
                    duration,
                    leadOffsetMs,
                    leadConsonantMs,
                    leadBlank,
                    leadOverlap ?? new Dictionary<string, double>(),
                    loudness,
                    shown));
                return;
            }

            dest.Add(BuildRenzokuAt(
                spans,
                index,
                duration,
                vcOffsetMs,
                consonantMs,
                blankAt,
                vcOverlapAt,
                loudness,
                shown));
            return;
        }

        if (PreviousVowelLetter(spans, index) == "-")
        {
            dest.Add(Build(
                filename,
                spans,
                index,
                duration,
                leadOffsetMs,
                leadConsonantMs,
                leadBlank,
                leadOverlap ?? new Dictionary<string, double>(),
                loudness,
                "- " + alias));
            return;
        }

        var cons = ConsonantSpan(spans, index, vowel);
        if (cons != null)
        {
            dest.Add(BuildVc(
                spans,
                index,
                cons,
                duration,
                vcOffsetMs,
                vcConsonant,
                vcBlank,
                vcOverlapAt));
        }

        dest.Add(Build(
            filename,
            spans,
            index,
            duration,
            0,
            consonantMs,
            blankAt,
            overlap,
            loudness,
            alias));
    }

    static OtoMark BuildVc(
        List<PhoneSpan> spans,
        int index,
        PhoneSpan cons,
        double duration,
        double vcOffsetMs,
        IReadOnlyDictionary<string, double>? vcConsonant,
        IReadOnlyDictionary<string, double>? vcBlank,
        double vcOverlapAt)
    {
        var c0 = Ms(cons.Start);
        var c1 = Ms(cons.End);
        if (c1 < c0)
            c1 = c0;
        var dur = Math.Max(Ms(duration), Math.Max(c1, c0));
        var offset = Math.Max(0, Round3(c0 - RenzokuOffsetLeadMs - vcOffsetMs));
        var preutter = Math.Max(0, Round3(c0 - offset));
        var overlapMs = Round3((c0 - offset) * vcOverlapAt);
        var blankAt = Round3(c0 + (c1 - c0) * Ratio(vcBlank, cons.Label, VcPartAt));
        var fixedAt = Round3(c0 + (blankAt - c0) * Ratio(vcConsonant, cons.Label, VcPartAt));
        var consonant = Math.Max(0, Round3(fixedAt - offset));
        var placed = PlaceBlank(blankAt, offset, consonant, dur);
        var blank = placed.Blank;
        consonant = placed.Consonant;
        var alias = PreviousVowelLetter(spans, index) + " " + cons.Label;
        return new OtoMark(
            alias,
            offset / 1000.0,
            (offset + overlapMs) / 1000.0,
            (offset + preutter) / 1000.0,
            (offset + consonant) / 1000.0,
            (offset - blank) / 1000.0);
    }

    static double Ratio(IReadOnlyDictionary<string, double>? table, string key, double fallback)
    {
        if (table != null && table.TryGetValue(key, out var value)
            && !double.IsNaN(value) && !double.IsInfinity(value))
            return Math.Clamp(value, 0, 1);
        return fallback;
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
        var alias = RenzokuAlias(filename, spans, index, spans[index]);
        if (FollowsRest(spans, index, spans[index]))
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

        return BuildRenzokuAt(
            spans,
            index,
            duration,
            offsetMs,
            consonantMs,
            blankAt,
            overlapAt,
            loudness,
            alias);
    }

    static OtoMark BuildRenzokuAt(
        List<PhoneSpan> spans,
        int index,
        double duration,
        double offsetMs,
        double consonantMs,
        double blankAt,
        double overlapAt,
        double[]? loudness,
        string alias)
    {
        var vowel = spans[index];
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

    static string MoraAlias(string filename, PhoneSpan vowel)
    {
        var alias = vowel.Token;
        if (string.IsNullOrEmpty(alias))
            alias = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(alias))
            alias = vowel.Label;
        return alias;
    }

    static string RenzokuAlias(string filename, List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        return PreviousVowelLetter(spans, index) + " " + MoraAlias(filename, vowel);
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
        double[]? loudness,
        string? alias = null)
    {
        var vowel = spans[index];
        alias ??= MoraAlias(filename, vowel);

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

    static PhoneSpan? ConsonantSpan(List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        if (index <= 0)
            return null;
        var prev = spans[index - 1];
        if (prev.Token != vowel.Token || string.IsNullOrEmpty(prev.Token))
            return null;
        if (IsVowelOnset(prev.Label) || prev.Label is "無音" or "息")
            return null;
        return prev;
    }

    static string? ConsonantLabel(List<PhoneSpan> spans, int index, PhoneSpan vowel)
    {
        return ConsonantSpan(spans, index, vowel)?.Label;
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

    internal static (double Blank, double Consonant) PlaceBlank(
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

    // 子音のない母音。CVVC では連続音の計算にする。を と小書き母音も同じ。
    static bool IsVowelPhoneme(string token) =>
        token is "あ" or "い" or "う" or "え" or "お" or "ん" or "を"
            or "ぁ" or "ぃ" or "ぅ" or "ぇ" or "ぉ"
            or "ア" or "イ" or "ウ" or "エ" or "オ" or "ン" or "ヲ"
            or "ァ" or "ィ" or "ゥ" or "ェ" or "ォ";

    static double Ms(double seconds) => Round3(seconds * 1000.0);

    internal static double Round3(double value) => Math.Round(value, 3, MidpointRounding.ToEven);

    static double Bounded(double value, double fallback, double lo, double hi)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(value, lo, hi);
    }
}
