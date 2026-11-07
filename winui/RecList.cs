namespace Otoasobi;

// 録音リストのテキスト。1 行が 1 テイク。# はメモ、タブの右はコメント。
// 文字コードは character.txt と同じ見分けで、CP932 と UTF-8 を受ける。
public sealed record RecLine(string Sing, string Comment, string? NameError);

public sealed class RecDisplay
{
    public RecLine? Line { get; init; }

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

    public static string WavName(string sing) => sing.Trim() + ".wav";

    public static IReadOnlyList<string> Outside(IEnumerable<string> wavNames, IEnumerable<RecLine> lines)
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
            listed.Add(WavName(line.Sing));
        var extra = new List<string>();
        foreach (var name in wavNames)
        {
            if (!listed.Contains(name))
                extra.Add(name);
        }

        extra.Sort(StringComparer.OrdinalIgnoreCase);
        return extra;
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

public sealed class RecListChoice
{
    public RecListChoice(string label, string? path, bool browse = false)
    {
        Label = label;
        Path = path;
        Browse = browse;
    }

    public string Label { get; }

    public string? Path { get; }

    public bool Browse { get; }
}

sealed class RecFolderFile
{
    // null は未設定。空文字は「選択しない」。
    public string? List { get; set; }

    public string? Guide { get; set; }

    // null は未選択。WAV が選ばれていればファイル、そうでなければ自分で設定する。
    public bool? GuideManual { get; set; }
}

static class RecFolderStore
{
    public static string FileName { get; } = "OTOasibi_録音.json";

    public static bool ListIsCleared(string? list) =>
        list != null && list.Trim().Length == 0;

    public static bool IsGuideWav(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

    public static string GuideTextPath(string wavPath) =>
        Path.ChangeExtension(wavPath, ".txt");

    public static bool UseManualGuide(bool? saved, string? guide) =>
        saved ?? !IsGuideWav(guide);

    public static RecFolderFile Load(string folder) =>
        JsonFiles.Read<RecFolderFile>(Path.Combine(folder, FileName)) ?? new RecFolderFile();

    public static void Save(string folder, RecFolderFile file) =>
        JsonFiles.Write(Path.Combine(folder, FileName), file);
}

// 一度選んだガイド BGM の WAV。音源フォルダではなく、この PC に残す。
static class RecGuideHistory
{
    public static string FilePath { get; } = Path.Combine(AppPaths.DataRoot, "recording-guides.json");

    public static IReadOnlyList<string> Load()
    {
        var wavs = new List<string>();
        foreach (var path in RecListHistory.Merge(null, JsonFiles.Read<List<string>>(FilePath)))
        {
            if (RecFolderStore.IsGuideWav(path))
                wavs.Add(path);
        }

        return wavs;
    }

    public static void Remember(string path)
    {
        var next = RecListHistory.Merge(path, Load());
        if (next.Count == 0)
            return;
        try
        {
            JsonFiles.Write(FilePath, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

// 一度選んだ録音リスト。音源フォルダではなく、この PC に残す。
static class RecListHistory
{
    public const int MaxCount = 20;

    public static string FilePath { get; } = Path.Combine(AppPaths.DataRoot, "recording-lists.json");

    public static IReadOnlyList<string> Load() => Merge(null, JsonFiles.Read<List<string>>(FilePath));

    public static void Remember(string path)
    {
        var next = Merge(path, Load());
        if (next.Count == 0)
            return;
        try
        {
            JsonFiles.Write(FilePath, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static IReadOnlyList<string> Merge(string? added, IEnumerable<string>? existing)
    {
        var next = new List<string>();
        if (TryNormalize(added, out var path))
            next.Add(path);
        if (existing == null)
            return next;
        foreach (var raw in existing)
        {
            if (!TryNormalize(raw, out var full))
                continue;
            var known = false;
            foreach (var item in next)
            {
                if (!string.Equals(item, full, StringComparison.OrdinalIgnoreCase))
                    continue;
                known = true;
                break;
            }

            if (known)
                continue;
            next.Add(full);
            if (next.Count == MaxCount)
                break;
        }

        return next;
    }

    public static string Caption(string path, IReadOnlyList<string> paths)
    {
        var name = Path.GetFileName(path);
        if (name.Length == 0)
            return path;
        var same = 0;
        foreach (var other in paths)
        {
            if (string.Equals(Path.GetFileName(other), name, StringComparison.OrdinalIgnoreCase))
                same++;
        }

        if (same <= 1)
            return name;
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return parent.Length == 0 ? path : $"{name} ({parent})";
    }

    static bool TryNormalize(string? raw, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        try
        {
            path = Path.GetFullPath(raw.Trim());
            return path.Length > 0 && path.Length <= 1024;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
