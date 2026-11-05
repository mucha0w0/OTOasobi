namespace Otoasobi;

// 録音リストのテキスト。1 行が 1 テイク。# はメモ、タブの右はコメント。
// 文字コードは character.txt と同じ見分けで、CP932 と UTF-8 を受ける。
public sealed record RecLine(string Sing, string Comment, string? NameError);

public sealed class RecDisplay
{
    public required RecLine Line { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }
}

static class RecList
{
    public const string DefaultFileName = "録音リスト.txt";

    public static IReadOnlyList<RecLine> Parse(byte[] bytes)
    {
        var text = VoicebankText.Decode(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = new List<RecLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var tab = line.IndexOf('\t');
            var sing = (tab < 0 ? line : line[..tab]).Trim();
            var comment = tab < 0 ? "" : line[(tab + 1)..].Trim();
            if (sing.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                sing = sing[..^4].Trim();
            if (sing.Length == 0)
                continue;
            lines.Add(new RecLine(sing, comment, RecNames.StemError(sing)));
        }

        return lines;
    }
}

static class RecNames
{
    public const string Missing = "未録音";
    public const string Recorded = "録音済み";
    public const string Invalid = "使えない文字";

    public static string WavName(string sing, string? note, bool prefixPitch)
    {
        var stem = sing.Trim();
        if (prefixPitch && !string.IsNullOrEmpty(note))
            stem = note + stem;
        return stem + ".wav";
    }

    public static string Status(RecLine line, bool exists) =>
        line.NameError != null ? Invalid : exists ? Recorded : Missing;

    public static bool CanRecord(RecLine line, string wavName) =>
        line.NameError == null && WavError(wavName) == null;

    public static string? StemError(string sing)
    {
        if (string.IsNullOrWhiteSpace(sing))
            return "ファイル名が空です";
        var chars = sing.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (ch == '_')
                continue;
            if (!IsKana(ch))
                return $"使えない文字です: {ch}";
            if (!IsSmallYoon(ch))
                continue;
            if (i == 0 || !IsYoonBase(chars[i - 1]))
                return $"単独の小書き仮名は使えません: {ch}";
        }

        return null;
    }

    public static string? WavError(string wavName)
    {
        var stem = wavName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? wavName[..^4] : wavName;
        if (stem.Length == 0)
            return "ファイル名が空です";
        if (stem.Length > 120)
            return "ファイル名が長すぎます";
        if (stem[^1] is '.' or ' ')
            return "ファイル名の末尾に使えない文字があります";
        foreach (var ch in stem)
        {
            if (ch < 32 || "\\/:*?\"<>|".Contains(ch))
                return $"使えない文字です: {ch}";
        }

        return null;
    }

    static bool IsKana(char ch) =>
        ch is >= '\u3041' and <= '\u3096' or >= '\u30A1' and <= '\u30F6';

    static bool IsSmallYoon(char ch) => ch is 'ゃ' or 'ゅ' or 'ょ' or 'ャ' or 'ュ' or 'ョ' or 'ゎ' or 'ヮ';

    static bool IsYoonBase(char ch)
    {
        if (!IsKana(ch) || IsSmallYoon(ch) || IsSmallVowel(ch))
            return false;
        return ch is not ('ん' or 'ン' or 'っ' or 'ッ');
    }

    static bool IsSmallVowel(char ch) => ch is 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'ァ' or 'ィ' or 'ゥ' or 'ェ' or 'ォ';
}

sealed class RecFolderFile
{
    public string? List { get; set; }

    public string? Guide { get; set; }

    public bool PrefixPitch { get; set; }
}

static class RecFolderStore
{
    public static string FileName { get; } = "OTOasibi_録音.json";

    public static RecFolderFile Load(string folder) =>
        JsonFiles.Read<RecFolderFile>(Path.Combine(folder, FileName)) ?? new RecFolderFile();

    public static void Save(string folder, RecFolderFile file) =>
        JsonFiles.Write(Path.Combine(folder, FileName), file);
}
