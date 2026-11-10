using System.Globalization;
using System.Text;

namespace Otoasobi;

// フォルダの oto.ini を読み、1 ファイル分の OtoMark にする。書き戻しは 1 行ずつ。
static class OtoIniFile
{
    public static bool Exists(string? folder) =>
        !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "oto.ini"));

    public static List<OtoMark> MarksForFile(string? folder, string fileName)
    {
        var marks = new List<OtoMark>();
        if (string.IsNullOrEmpty(folder))
            return marks;
        foreach (var line in ReadLines(folder))
        {
            if (!TryParseLine(line, out var wav, out var alias, out var mark))
                continue;
            if (!wav.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                continue;
            marks.Add(mark with { Alias = alias });
        }

        return marks;
    }

    public static bool TryPatchLine(string? folder, string fileName, int index, OtoMark placed, string expectedAlias, string? nextAlias = null)
    {
        if (string.IsNullOrEmpty(folder))
            return false;
        var path = Path.Combine(folder, "oto.ini");
        if (!File.Exists(path))
            return false;
        var lines = ReadLines(folder);
        var fileLines = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (TryParseLine(lines[i], out var wav, out _, out _) && wav.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                fileLines.Add(i);
        }

        if (index < 0 || index >= fileLines.Count)
            return false;
        var at = fileLines[index];
        if (!TryParseLine(lines[at], out var filename, out var alias, out _))
            return false;
        var left = CleanAlias(alias);
        var right = CleanAlias(expectedAlias);
        if (left.Length == 0)
            left = alias.Trim();
        if (!left.Equals(right, StringComparison.Ordinal))
            return false;
        var written = string.IsNullOrEmpty(nextAlias) ? alias.TrimEnd('*').Trim() : nextAlias;
        lines[at] = RenderLine(filename, written, placed);
        try
        {
            WriteLines(path, lines);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static List<string> ReadLines(string folder)
    {
        var path = Path.Combine(folder, "oto.ini");
        if (!File.Exists(path))
            return [];
        var bytes = File.ReadAllBytes(path);
        var text = VoicebankText.Decode(bytes);
        return text.Split('\n').Select(raw => raw.TrimEnd('\r').Trim()).Where(line => line.Length > 0).ToList();
    }

    static void WriteLines(string path, IReadOnlyList<string> lines)
    {
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        var text = lines.Count == 0 ? "" : string.Join("\r\n", lines) + "\r\n";
        var utf8 = bytes.Length == 0 || VoicebankText.Classify(bytes) == VoicebankText.Utf8Name;
        if (utf8)
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        else
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            File.WriteAllText(path, text, Encoding.GetEncoding(932));
        }
    }

    public static bool TryParseLine(string line, out string filename, out string alias, out OtoMark mark)
    {
        filename = "";
        alias = "";
        mark = default;
        var eq = line.IndexOf('=');
        if (eq < 0)
            return false;
        filename = line[..eq];
        var body = line[(eq + 1)..];
        var comma = body.IndexOf(',');
        if (comma < 0)
            return false;
        alias = body[..comma];
        var tail = body[comma..].Split(',');
        if (tail.Length != 6)
            return false;
        if (!OtoIni.TryParse(tail[1], out var offsetMs))
            return false;
        if (!OtoIni.TryParse(tail[2], out var consonantMs))
            return false;
        if (!OtoIni.TryParse(tail[3], out var blankMs))
            return false;
        if (!OtoIni.TryParse(tail[4], out var preutterMs))
            return false;
        if (!OtoIni.TryParse(tail[5], out var overlapMs))
            return false;
        var basis = new OtoMark(alias, 0, 0, 0, 0, 0);
        mark = OtoIni.FromFields(basis, offsetMs, overlapMs, preutterMs, consonantMs, blankMs);
        return true;
    }

    public static string RenderLine(string filename, string alias, OtoMark mark)
    {
        var off = OtoIni.Field(mark, 0);
        var overlap = OtoIni.Field(mark, 1);
        var preutter = OtoIni.Field(mark, 2);
        var consonant = OtoIni.Field(mark, 3);
        var blank = OtoIni.Field(mark, 4);
        return FormattableString.Invariant(
            $"{filename}={alias},{OtoIni.Format(off)},{OtoIni.Format(consonant)},{OtoIni.Format(blank)},{OtoIni.Format(preutter)},{OtoIni.Format(overlap)}");
    }

    static string CleanAlias(string text) => text.Replace("*", "").Trim();
}
