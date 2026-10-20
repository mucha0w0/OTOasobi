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

    static double Ms(double seconds) => OtoMarks.Round3(seconds * 1000.0);
}
