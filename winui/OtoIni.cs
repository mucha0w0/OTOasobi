using System.Globalization;

namespace Otoasobi;

// oto.ini に書くミリ秒。左ブランクはファイル先頭から、ほかはオフセットから。
// 右ブランクは、オフセットから終端までの時間を負にした値。
static class OtoIni
{
    public const double MaxMs = 86_400_000;

    public static double Field(OtoMark mark, int point) => point switch
    {
        0 => Ms(mark.Offset),
        1 => Ms(mark.Overlap - mark.Offset),
        2 => Ms(mark.Preutter - mark.Offset),
        3 => Ms(mark.Consonant - mark.Offset),
        _ => Ms(mark.Offset - mark.Blank),
    };

    public static OtoMark FromFields(
        OtoMark basis,
        double offset,
        double overlap,
        double preutter,
        double consonant,
        double blank)
    {
        offset = Clamp(0, offset);
        overlap = Clamp(1, overlap);
        preutter = Clamp(2, preutter);
        consonant = Clamp(3, consonant);
        blank = Clamp(4, blank);
        return basis with
        {
            Offset = offset / 1000.0,
            Overlap = (offset + overlap) / 1000.0,
            Preutter = (offset + preutter) / 1000.0,
            Consonant = (offset + consonant) / 1000.0,
            Blank = (offset - blank) / 1000.0,
        };
    }

    // オフセット、先行発声、子音部は 0 未満にしない。オーバーラップとブランクは負も書く。
    public static double Clamp(int point, double ms)
    {
        var rounded = OtoMarks.Round3(ms);
        if (double.IsNaN(rounded) || double.IsInfinity(rounded))
            return 0;
        if (point is 0 or 2 or 3 && rounded < 0)
            return 0;
        return rounded;
    }

    public static string Format(double ms)
    {
        var rounded = OtoMarks.Round3(ms);
        if (double.IsNaN(rounded) || double.IsInfinity(rounded))
            rounded = 0;
        return rounded.ToString("0.000", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? text, out double ms)
    {
        ms = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var source = text.Trim();
        if (source.Contains(',') && source.Contains('.'))
            source = source.Replace(",", "");
        else
            source = source.Replace(',', '.');
        if (!double.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaxMs)
            return false;
        ms = value;
        return true;
    }

    // 画面の * は編集印なので外す。oto.ini の区切り文字も入れない。空や長すぎる名前は null。
    public static string? CleanAlias(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var source = text.Replace("*", "").Trim();
        if (source.Length == 0 || source.Length > 80)
            return null;
        foreach (var ch in source)
        {
            if (ch is '\r' or '\n' or '\t' or '=' or ',')
                return null;
        }

        return source;
    }

    // 表のメモ。改行は潰す。空なら null。oto.ini には出さない。
    public static string? CleanComment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var source = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (source.Length == 0)
            return null;
        if (source.Length > 200)
            source = source[..200];
        return source;
    }

    // overrides の入っている行は、その文字列のまま残す。ほかは重複へ番号を付ける。
    public static string[] ResolveAliases(IReadOnlyList<string> aliases, IReadOnlyList<string?>? overrides)
    {
        var raw = new string[aliases.Count];
        var pinned = new bool[aliases.Count];
        for (var i = 0; i < aliases.Count; i++)
        {
            string? over = null;
            if (overrides != null && i < overrides.Count)
                over = overrides[i];
            if (!string.IsNullOrEmpty(over))
            {
                raw[i] = over;
                pinned[i] = true;
            }
            else
            {
                raw[i] = aliases[i] ?? "";
            }
        }

        return NumberDuplicateAliases(raw, pinned);
    }

    public static bool[] DuplicateAliasFlags(IReadOnlyList<string> aliases)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var alias in aliases)
            counts[alias] = counts.GetValueOrDefault(alias) + 1;
        var flags = new bool[aliases.Count];
        for (var i = 0; i < aliases.Count; i++)
            flags[i] = counts.GetValueOrDefault(aliases[i]) >= 2;
        return flags;
    }

    // oto.py の number_duplicate_aliases と同じ。1 回目の名前は残し、2 回目以降へ 2、3、… を付ける。
    // 付けた名前が、別のエイリアスや、すでに付けた名前と重なるときは空いている番号まで進める。
    // pinned の行は手入力の名前なので、番号を付け足さない。
    public static string[] NumberDuplicateAliases(IReadOnlyList<string> aliases, IReadOnlyList<bool>? pinned = null)
    {
        var reserved = new HashSet<string>(aliases);
        var used = new HashSet<string>();
        var seen = new Dictionary<string, int>();
        var result = new string[aliases.Count];
        for (var i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            var count = seen.GetValueOrDefault(alias) + 1;
            seen[alias] = count;
            if (pinned != null && i < pinned.Count && pinned[i])
            {
                used.Add(alias);
                result[i] = alias;
                continue;
            }

            string chosen;
            if (count == 1)
            {
                chosen = alias;
            }
            else
            {
                var number = 2;
                while (true)
                {
                    var candidate = alias + number.ToString(CultureInfo.InvariantCulture);
                    if (!reserved.Contains(candidate) && !used.Contains(candidate))
                    {
                        chosen = candidate;
                        break;
                    }

                    number++;
                }
            }

            used.Add(chosen);
            result[i] = chosen;
        }

        return result;
    }

    static double Ms(double seconds) => OtoMarks.Round3(seconds * 1000.0);
}
