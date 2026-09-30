namespace Otoasobi;

// 単独音のオーバーラップは子音ラベルごと。キーは parser.py の CV 子音と揃える。
static class ConsonantOverlap
{
    public const double Default = 0.5;

    public static readonly (string Group, string Key, string Caption)[] Kinds =
    [
        ("か行", "k", "k  か"),
        ("か行", "ky", "ky  きゃ"),
        ("か行", "kw", "kw  くぁ"),
        ("さ行", "s", "s  さ"),
        ("さ行", "sh", "sh  し"),
        ("た行", "t", "t  た"),
        ("た行", "ty", "ty  てゅ"),
        ("た行", "ch", "ch  ち"),
        ("た行", "ts", "ts  つ"),
        ("な行", "n", "n  な"),
        ("な行", "ny", "ny  にゃ"),
        ("は行", "h", "h  は"),
        ("は行", "hy", "hy  ひゃ"),
        ("は行", "f", "f  ふ"),
        ("は行", "fy", "fy  ふゃ"),
        ("ま行", "m", "m  ま"),
        ("ま行", "my", "my  みゃ"),
        ("や行", "y", "y  や"),
        ("ら行", "r", "r  ら"),
        ("ら行", "ry", "ry  りゃ"),
        ("わ行", "w", "w  わ"),
        ("が行", "g", "g  が"),
        ("が行", "gy", "gy  ぎゃ"),
        ("が行", "gw", "gw  ぐぁ"),
        ("ざ行", "z", "z  ざ"),
        ("ざ行", "j", "j  じ"),
        ("だ行", "d", "d  だ"),
        ("だ行", "dy", "dy  でゅ"),
        ("ば行", "b", "b  ば"),
        ("ば行", "by", "by  びゃ"),
        ("ぱ行", "p", "p  ぱ"),
        ("ぱ行", "py", "py  ぴゃ"),
        ("ヴ行", "v", "v  ヴ"),
        ("ヴ行", "vy", "vy  ヴゃ"),
    ];

    public static double Ratio(IReadOnlyDictionary<string, double>? table, string? key)
    {
        if (table != null && key != null && table.TryGetValue(key, out var value)
            && !double.IsNaN(value) && !double.IsInfinity(value))
            return Math.Clamp(value, 0, 1);
        return Default;
    }
}
