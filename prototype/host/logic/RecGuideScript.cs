namespace Otoasobi;

// ガイド WAV と対の txt。UTAU 系の時刻表から録音開始・停止を読む。
static class RecGuideScript
{
    public readonly record struct Offsets(double? StartAfter, double? EndBefore);

    public static Offsets Parse(string path, double guideSeconds)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || guideSeconds <= 0)
            return new Offsets(null, null);
        try
        {
            return ParseBytes(File.ReadAllBytes(path), guideSeconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Offsets(null, null);
        }
    }

    public static Offsets ParseBytes(byte[] bytes, double guideSeconds)
    {
        if (bytes.Length == 0 || guideSeconds <= 0)
            return new Offsets(null, null);
        var text = VoicebankText.Decode(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        double? startAfter = null;
        double? stopAt = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line.Equals("sec", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split(',');
            if (parts.Length < 2)
                continue;
            if (!TryAt(parts[1], out var time))
                continue;
            var comment = parts[^1].Trim();
            if (comment.Contains("録音開始", StringComparison.Ordinal))
                startAfter = Math.Max(0, time);
            else if (comment.Contains("録音停止", StringComparison.Ordinal))
                stopAt = Math.Max(0, time);
        }

        double? endBefore = null;
        if (stopAt is double stop)
            endBefore = Math.Max(0, guideSeconds - stop);
        return new Offsets(startAfter, endBefore);
    }

    static bool TryAt(string part, out double value)
    {
        value = 0;
        return double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
            || double.TryParse(part.Trim(), out value);
    }
}
