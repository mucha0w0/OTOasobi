namespace Otoasobi;

// 単独音のオーバーラップは子音ラベルごと。キーは parser.py の CV 子音と揃える。
static class ConsonantOverlap
{
    public const double Default = 0.5;
    // オフセットスライダーの手前幅。開始より前に置ける秒の上限。
    public const double MaxBeforeSeconds = 0.1;

    public static readonly (string Group, string Key, string Caption)[] Kinds =
    [
        ("か行", "k", "k  か"),
        ("か行", "ky", "ky  きゃ"),
        ("か行", "kw", "kw  くぁ"),
        ("か行", "g", "g  が"),
        ("か行", "gy", "gy  ぎゃ"),
        ("か行", "gw", "gw  ぐぁ"),
        ("さ行", "s", "s  さ"),
        ("さ行", "sh", "sh  し"),
        ("さ行", "z", "z  ざ"),
        ("さ行", "j", "j  じ"),
        ("た行", "t", "t  た"),
        ("た行", "ty", "ty  てゅ"),
        ("た行", "ch", "ch  ち"),
        ("た行", "ts", "ts  つ"),
        ("た行", "d", "d  だ"),
        ("た行", "dy", "dy  でゅ"),
        ("な行", "n", "n  な"),
        ("な行", "ny", "ny  にゃ"),
        ("は行", "h", "h  は"),
        ("は行", "hy", "hy  ひゃ"),
        ("は行", "f", "f  ふ"),
        ("は行", "fy", "fy  ふゃ"),
        ("は行", "b", "b  ば"),
        ("は行", "by", "by  びゃ"),
        ("は行", "p", "p  ぱ"),
        ("は行", "py", "py  ぴゃ"),
        ("ま行", "m", "m  ま"),
        ("ま行", "my", "my  みゃ"),
        ("や行", "y", "y  や"),
        ("ら行", "r", "r  ら"),
        ("ら行", "ry", "ry  りゃ"),
        ("わ行", "w", "w  わ"),
        ("ヴ行", "v", "v  ヴ"),
        ("ヴ行", "vy", "vy  ヴゃ"),
    ];

    // プリセットの値。0〜100 は割合。負の数は子音開始より前の秒。
    public static double Stored(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 50;
        if (value < 0)
        {
            var seconds = Math.Round(value, 3, MidpointRounding.AwayFromZero);
            if (seconds > -0.0005)
                return 0;
            return Math.Max(-MaxBeforeSeconds, seconds);
        }

        return Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
    }

    // 画面から oto 計算へ。0〜1 は比率、負の数は秒。
    public static double ToEngine(double stored)
    {
        if (stored < 0)
            return Math.Max(-MaxBeforeSeconds, stored);
        return Math.Clamp(stored, 0, 100) / 100.0;
    }

    public static double EngineSetting(IReadOnlyDictionary<string, double>? table, string? key)
    {
        if (table != null && key != null && table.TryGetValue(key, out var value)
            && !double.IsNaN(value) && !double.IsInfinity(value))
        {
            if (value < 0)
                return Math.Max(-MaxBeforeSeconds, value);
            return Math.Clamp(value, 0, 1);
        }

        return Default;
    }

    // オフセットからのミリ秒。負の setting は子音開始より前の秒で、オフセットより前には置かない。
    public static double FromOffset(double c0Ms, double v0Ms, double offsetMs, double setting)
    {
        double point;
        if (setting < 0)
        {
            point = c0Ms + setting * 1000.0;
            if (point < offsetMs)
                point = offsetMs;
        }
        else
        {
            point = c0Ms + (v0Ms - c0Ms) * Math.Clamp(setting, 0, 1);
        }

        return Math.Round(point - offsetMs, 3, MidpointRounding.ToEven);
    }
}
