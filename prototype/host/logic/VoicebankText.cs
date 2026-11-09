using System.Globalization;
using System.Text;

namespace Otoasobi;

// UTAU の character.txt / readme.txt と、OpenUTAU の character.yaml。
// text_file_encoding は character.txt、prefix.map、oto.ini のバイトから決める。
// character.yaml は UTF-8 のまま、知らないキー（subbanks など）は残す。
static class VoicebankText
{
    public const string ShiftJis = "shift_jis";
    public const string Utf8Name = "utf-8";
    public const string RandomSample = "ランダム";

    public static readonly (string Code, string Label)[] Languages =
    [
        ("en-US", "英語（米国）"),
        ("de-DE", "ドイツ語"),
        ("es-ES", "スペイン語（スペイン）"),
        ("es-MX", "スペイン語（メキシコ）"),
        ("fi-FI", "フィンランド語"),
        ("fr-FR", "フランス語"),
        ("id-ID", "インドネシア語"),
        ("it-IT", "イタリア語"),
        ("ja-JP", "日本語"),
        ("ko-KR", "韓国語"),
        ("nl-NL", "オランダ語"),
        ("pl-PL", "ポーランド語"),
        ("pt-BR", "ポルトガル語（ブラジル）"),
        ("ru-RU", "ロシア語"),
        ("th-TH", "タイ語"),
        ("vi-VN", "ベトナム語"),
        ("zh-CN", "中国語（簡体）"),
        ("zh-TW", "中国語（繁体）"),
    ];

    static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    static readonly Encoding SjisRead;
    static readonly Encoding SjisWrite;

    static VoicebankText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = (Encoding)Encoding.GetEncoding(932).Clone();
        sjis.DecoderFallback = DecoderFallback.ReplacementFallback;
        sjis.EncoderFallback = EncoderFallback.ReplacementFallback;
        SjisRead = sjis;
        var write = (Encoding)Encoding.GetEncoding(932).Clone();
        write.EncoderFallback = EncoderFallback.ExceptionFallback;
        write.DecoderFallback = DecoderFallback.ExceptionFallback;
        SjisWrite = write;
    }

    public static string? Classify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Utf8Name;
        if (IsUtf8Multibyte(bytes))
            return Utf8Name;
        foreach (var b in bytes)
        {
            if (b >= 0x80)
                return ShiftJis;
        }

        return null;
    }

    public static List<(string File, string? Kind)> CollectVotes(string folder)
    {
        var votes = new List<(string, string?)>();
        foreach (var name in new[] { "character.txt", "prefix.map", "oto.ini" })
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
                continue;
            votes.Add((name, Classify(File.ReadAllBytes(path))));
        }

        return votes;
    }

    // 非 ASCII が無いファイルは票にしない。食い違うときは、oto.ini と prefix.map の
    // Shift_JIS を優先する。その2つが Shift_JIS でなければ、書けない文字は UTF-8 にする。
    public static string Resolve(IEnumerable<(string File, string? Kind)> votes, string characterText)
    {
        var known = new List<(string File, string Kind)>();
        foreach (var vote in votes)
        {
            if (vote.Kind is ShiftJis or Utf8Name)
                known.Add((vote.File, vote.Kind));
        }

        string chosen;
        if (known.Count == 0)
            chosen = ShiftJis;
        else if (known.All(vote => vote.Kind == Utf8Name))
            chosen = Utf8Name;
        else if (known.All(vote => vote.Kind == ShiftJis))
            chosen = ShiftJis;
        else
            chosen = known.Any(vote => vote.File != "character.txt" && vote.Kind == ShiftJis) ? ShiftJis : Utf8Name;

        if (chosen == ShiftJis && !CanEncode(characterText, ShiftJis))
        {
            var forced = known.Any(vote => vote.File != "character.txt" && vote.Kind == ShiftJis);
            if (!forced)
                chosen = Utf8Name;
        }

        return chosen;
    }

    public static bool CanEncode(string text, string encoding)
    {
        try
        {
            _ = encoding == Utf8Name ? Utf8.GetBytes(text) : SjisWrite.GetBytes(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Utf8.GetString(bytes, 3, bytes.Length - 3);
        return Classify(bytes) == Utf8Name ? Utf8.GetString(bytes) : SjisRead.GetString(bytes);
    }

    public static CharacterFile ParseCharacter(string text)
    {
        var file = new CharacterFile();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var raw in normalized.Split('\n'))
        {
            if (raw.Trim().Length == 0)
                continue;
            if (TryField(raw, out var key, out var value))
            {
                switch (key)
                {
                    case "name":
                        file.Name = value;
                        file.HasName = true;
                        break;
                    case "image":
                        file.Image = value;
                        file.HasImage = true;
                        break;
                    case "sample":
                        file.Sample = value;
                        file.HasSample = true;
                        break;
                    case "author":
                        file.Author = value;
                        file.HasAuthor = true;
                        break;
                    case "web":
                        file.Web = value;
                        file.HasWeb = true;
                        break;
                }

                continue;
            }

            if (TryProfile(raw, out var label, out var profile, out var fullwidth))
            {
                file.Profile.Add(new ProfileLine(label, profile, fullwidth));
                continue;
            }

            file.Extra.Add(raw.TrimEnd());
        }

        return file;
    }

    public static string FormatCharacter(CharacterFile file)
    {
        var lines = new List<string>
        {
            "name=" + file.Name.Trim(),
            "image=" + file.Image.Trim(),
            "sample=" + file.Sample.Trim(),
            "author=" + file.Author.Trim(),
            "web=" + file.Web.Trim(),
        };
        foreach (var line in file.Profile)
        {
            if (line.Label.Trim().Length == 0 && line.Value.Trim().Length == 0)
                continue;
            var mark = line.Fullwidth ? " ： " : " : ";
            lines.Add(line.Label.Trim() + mark + line.Value.Trim());
        }

        foreach (var extra in file.Extra)
        {
            if (extra.Trim().Length == 0)
                continue;
            lines.Add(extra.TrimEnd());
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    public static byte[] EncodeCharacter(CharacterFile file, string encoding)
    {
        var text = FormatCharacter(file);
        return encoding == Utf8Name ? Utf8.GetBytes(text) : SjisWrite.GetBytes(text);
    }

    public static string ReadReadme(byte[] bytes)
    {
        var text = Decode(bytes);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    public static byte[] EncodeReadme(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length == 0)
            return [];
        return SjisWrite.GetBytes(normalized.Replace("\n", "\r\n", StringComparison.Ordinal));
    }

    public static VoicebankYaml ReadYaml(string? text)
    {
        var yaml = new VoicebankYaml();
        if (string.IsNullOrEmpty(text))
            return yaml;
        foreach (var piece in ParsePieces(text))
        {
            if (piece.Key.Length == 0)
                continue;
            if (piece.Key == "localized_names")
            {
                if (piece.Entries != null)
                    yaml.LocalizedNames.AddRange(piece.Entries);
                else if (piece.Kind == "block")
                    yaml.PreserveLocalizedBlock = true;
                continue;
            }

            if (piece.Entries != null)
                continue;

            if (piece.Kind != "scalar")
                continue;
            switch (piece.Key)
            {
                case "text_file_encoding":
                    yaml.TextFileEncoding = piece.Value;
                    break;
                case "name":
                    yaml.Name = piece.Value;
                    yaml.HasName = true;
                    break;
                case "image":
                    yaml.Image = piece.Value;
                    yaml.HasImage = true;
                    break;
                case "author":
                    yaml.Author = piece.Value;
                    yaml.HasAuthor = true;
                    break;
                case "voice":
                    yaml.Voice = piece.Value;
                    yaml.HasVoice = true;
                    break;
                case "version":
                    yaml.Version = piece.Value;
                    yaml.HasVersion = true;
                    break;
                case "web":
                    yaml.Web = piece.Value;
                    yaml.HasWeb = true;
                    break;
                case "portrait":
                    yaml.Portrait = piece.Value;
                    yaml.HasPortrait = true;
                    break;
                case "portrait_opacity":
                    if (double.TryParse(piece.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
                    {
                        yaml.PortraitOpacity = Math.Clamp(opacity, 0, 1);
                        yaml.HasOpacity = true;
                    }

                    break;
                case "portrait_height":
                    if (int.TryParse(piece.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
                    {
                        yaml.PortraitHeight = Math.Max(0, height);
                        yaml.HasHeight = true;
                    }

                    break;
            }
        }

        return yaml;
    }

    public static string ApplyYaml(string? existing, VoicebankYaml yaml)
    {
        var pieces = string.IsNullOrEmpty(existing) ? [] : ParsePieces(existing);
        SetScalar(pieces, "text_file_encoding", yaml.TextFileEncoding);
        SetScalar(pieces, "singer_type", "utau");
        SetScalar(pieces, "name", yaml.Name);
        SetScalar(pieces, "image", yaml.Image);
        SetScalar(pieces, "author", yaml.Author);
        SetOptional(pieces, "voice", yaml.Voice);
        SetOptional(pieces, "version", yaml.Version);
        SetScalar(pieces, "web", yaml.Web);
        if (!(yaml.PreserveLocalizedBlock && yaml.LocalizedNames.Count == 0))
            SetMap(pieces, "localized_names", yaml.LocalizedNames);
        if (yaml.Portrait.Trim().Length == 0)
        {
            RemoveKey(pieces, "portrait");
            RemoveKey(pieces, "portrait_opacity");
            RemoveKey(pieces, "portrait_height");
        }
        else
        {
            SetScalar(pieces, "portrait", yaml.Portrait.Trim().Replace('\\', '/'));
            SetPlain(pieces, "portrait_opacity", FormatOpacity(yaml.PortraitOpacity));
            SetPlain(pieces, "portrait_height", Math.Max(0, yaml.PortraitHeight).ToString(CultureInfo.InvariantCulture));
        }

        return Emit(pieces);
    }

    public static string FormatOpacity(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string PortraitFileName(string? relative)
    {
        var rel = NormalizeRelative(relative);
        if (rel.Length == 0)
            return "portrait.png";
        var ext = Path.GetExtension(rel);
        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return rel;
        if (ext.Length == 0)
            return rel + ".png";
        return Path.ChangeExtension(rel, ".png").Replace('\\', '/');
    }

    public static string NormalizeRelative(string? relative)
    {
        var rel = (relative ?? "").Trim().Replace('\\', '/');
        while (rel.StartsWith("./", StringComparison.Ordinal))
            rel = rel[2..];
        return rel.Trim('/');
    }

    public static bool TryResolveInside(string folder, string relative, out string full)
    {
        full = "";
        var rel = NormalizeRelative(relative).Replace('/', Path.DirectorySeparatorChar);
        if (rel.Length == 0 || Path.IsPathRooted(rel))
            return false;
        var parts = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
            return false;
        foreach (var part in parts)
        {
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
        }

        var root = Path.GetFullPath(folder);
        full = Path.GetFullPath(Path.Combine([root, .. parts]));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    static bool TryField(string raw, out string key, out string value)
    {
        key = "";
        value = "";
        var line = raw.Trim();
        var eq = line.IndexOf('=');
        if (eq <= 0)
            return false;
        var name = line[..eq].Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            return false;
        var lower = name.ToLowerInvariant();
        if (lower is not ("name" or "image" or "sample" or "author" or "web"))
            return false;
        key = lower;
        value = line[(eq + 1)..].Trim();
        return true;
    }

    // 「：」と、スキーム（://）ではない「:」を、プロフィールの区切りにする。
    static bool TryProfile(string raw, out string label, out string value, out bool fullwidth)
    {
        label = "";
        value = "";
        fullwidth = true;
        var line = raw.Trim();
        var full = line.IndexOf('：');
        var half = IndexOfProfileColon(line);
        if (full < 0 && half < 0)
            return false;
        var useFull = full >= 0 && (half < 0 || full <= half);
        var index = useFull ? full : half;
        label = line[..index].Trim();
        value = line[(index + 1)..].Trim();
        fullwidth = useFull;
        return label.Length > 0 || value.Length > 0;
    }

    static int IndexOfProfileColon(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != ':')
                continue;
            if (i + 1 < line.Length && line[i + 1] == '/')
                continue;
            return i;
        }

        return -1;
    }

    static bool IsUtf8Multibyte(ReadOnlySpan<byte> bytes)
    {
        var multibyte = false;
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b <= 0x7F)
            {
                i++;
                continue;
            }

            int need;
            if (b >= 0xC2 && b <= 0xDF)
                need = 2;
            else if (b >= 0xE0 && b <= 0xEF)
                need = 3;
            else if (b >= 0xF0 && b <= 0xF4)
                need = 4;
            else
                return false;
            if (i + need > bytes.Length)
                return false;
            for (var j = 1; j < need; j++)
            {
                if ((bytes[i + j] & 0xC0) != 0x80)
                    return false;
            }

            if (need == 3)
            {
                var second = bytes[i + 1];
                if (b == 0xE0 && second < 0xA0)
                    return false;
                if (b == 0xED && second >= 0xA0)
                    return false;
            }

            if (need == 4 && b == 0xF0 && bytes[i + 1] < 0x90)
                return false;
            if (need == 4 && b == 0xF4 && bytes[i + 1] >= 0x90)
                return false;
            multibyte = true;
            i += need;
        }

        return multibyte;
    }

    sealed class YamlPiece
    {
        public string Kind = "";
        public string Key = "";
        public string Value = "";
        public bool Plain;
        public List<(string Key, string Value)>? Entries;
        public string Raw = "";
    }

    static List<YamlPiece> ParsePieces(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        var pieces = new List<YamlPiece>();
        var i = 0;
        while (i < lines.Count)
        {
            if (!TryTopKey(lines[i], out var key, out var value))
            {
                var start = i;
                i++;
                while (i < lines.Count && !TryTopKey(lines[i], out _, out _))
                    i++;
                pieces.Add(new YamlPiece { Kind = "raw", Raw = Join(lines, start, i) });
                continue;
            }

            if (OpensBlock(value) && BlockEnd(lines, i) is int end)
            {
                var piece = new YamlPiece
                {
                    Kind = "block",
                    Key = key,
                    Raw = Join(lines, i, end),
                };
                if (key == "localized_names" && value.Length == 0)
                    piece.Entries = ParseMap(lines, i + 1, end);
                pieces.Add(piece);
                i = end;
                continue;
            }

            pieces.Add(new YamlPiece
            {
                Kind = "scalar",
                Key = key,
                Value = ParseScalar(value),
                Raw = lines[i] + "\n",
            });
            i++;
        }

        return pieces;
    }

    static bool TryTopKey(string line, out string key, out string value)
    {
        key = "";
        value = "";
        if (line.Length == 0 || line[0] is ' ' or '\t' or '#' or '-')
            return false;
        var colon = line.IndexOf(':');
        if (colon <= 0)
            return false;
        var name = line[..colon].Trim();
        if (name.Length == 0)
            return false;
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                return false;
        }

        key = name;
        value = line[(colon + 1)..].Trim();
        return true;
    }

    static bool OpensBlock(string value) =>
        value.Length == 0 || value is "|" or "|-" or "|+" or ">" or ">-" or ">+";

    static int? BlockEnd(List<string> lines, int keyIndex)
    {
        var next = keyIndex + 1;
        while (next < lines.Count && lines[next].Length == 0)
            next++;
        if (next >= lines.Count || !ContinuesBlock(lines[next]))
            return null;
        var end = next + 1;
        while (end < lines.Count)
        {
            if (lines[end].Length == 0)
            {
                var look = end + 1;
                while (look < lines.Count && lines[look].Length == 0)
                    look++;
                if (look < lines.Count && ContinuesBlock(lines[look]))
                {
                    end = look;
                    continue;
                }

                break;
            }

            if (!ContinuesBlock(lines[end]))
                break;
            end++;
        }

        return end;
    }

    static bool ContinuesBlock(string line) =>
        line.Length > 0 && (line[0] is ' ' or '\t' || line[0] == '-');

    static List<(string Key, string Value)>? ParseMap(List<string> lines, int start, int end)
    {
        var entries = new List<(string, string)>();
        for (var i = start; i < end; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
                continue;
            if (trimmed.StartsWith('-'))
                return null;
            var colon = IndexOfProfileColon(trimmed);
            if (colon <= 0)
                return null;
            var key = trimmed[..colon].Trim();
            if (key.Length == 0 || key.Any(char.IsWhiteSpace))
                return null;
            entries.Add((key, ParseScalar(trimmed[(colon + 1)..])));
        }

        return entries;
    }

    static string ParseScalar(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
            return "";
        if (text[0] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\' || i + 1 >= text.Length)
                {
                    sb.Append(c);
                    continue;
                }

                var next = text[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    'u' when i + 4 < text.Length && int.TryParse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) => ConsumeHex(ref i, code),
                    _ => next,
                });
            }

            return sb.ToString();
        }

        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            return text[1..^1].Replace("''", "'", StringComparison.Ordinal);
        var hash = text.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
            text = text[..hash];
        return text.Trim();
    }

    static char ConsumeHex(ref int i, int code)
    {
        i += 4;
        return (char)code;
    }

    static void SetScalar(List<YamlPiece> pieces, string key, string value) =>
        Replace(pieces, key, new YamlPiece { Kind = "scalar", Key = key, Value = value });

    static void SetPlain(List<YamlPiece> pieces, string key, string value) =>
        Replace(pieces, key, new YamlPiece { Kind = "scalar", Key = key, Value = value, Plain = true });

    static void SetOptional(List<YamlPiece> pieces, string key, string value)
    {
        if (value.Trim().Length == 0)
            RemoveKey(pieces, key);
        else
            SetScalar(pieces, key, value.Trim());
    }

    static void SetMap(List<YamlPiece> pieces, string key, List<(string Key, string Value)> entries)
    {
        if (entries.Count == 0)
        {
            RemoveKey(pieces, key);
            return;
        }

        Replace(pieces, key, new YamlPiece { Kind = "map", Key = key, Entries = entries });
    }

    static void Replace(List<YamlPiece> pieces, string key, YamlPiece piece)
    {
        var index = pieces.FindIndex(item => item.Key == key);
        if (index >= 0)
            pieces[index] = piece;
        else
            pieces.Add(piece);
    }

    static void RemoveKey(List<YamlPiece> pieces, string key) =>
        pieces.RemoveAll(item => item.Key == key);

    static string Emit(List<YamlPiece> pieces)
    {
        var sb = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (piece.Kind is "raw" or "block")
            {
                if (piece.Entries != null)
                {
                    AppendMap(sb, piece.Key, piece.Entries);
                    continue;
                }

                if (piece.Raw.Length == 0)
                    continue;
                sb.Append(piece.Raw);
                if (!piece.Raw.EndsWith('\n'))
                    sb.Append('\n');
                continue;
            }

            if (piece.Kind == "map" && piece.Entries != null)
            {
                AppendMap(sb, piece.Key, piece.Entries);
                continue;
            }

            // 触らないキーは、引用やコメントをそのまま残す。
            if (piece.Raw.Length > 0)
            {
                sb.Append(piece.Raw);
                if (!piece.Raw.EndsWith('\n'))
                    sb.Append('\n');
                continue;
            }

            sb.Append(piece.Key).Append(": ");
            sb.Append(piece.Plain ? piece.Value : Quote(piece.Value));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    static void AppendMap(StringBuilder sb, string key, List<(string Key, string Value)> entries)
    {
        sb.Append(key).Append(":\n");
        foreach (var (entryKey, entryValue) in entries)
            sb.Append("  ").Append(entryKey).Append(": ").Append(Quote(entryValue)).Append('\n');
    }

    static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 32)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    static string Join(List<string> lines, int start, int end)
    {
        if (start >= end)
            return "";
        return string.Join('\n', lines.GetRange(start, end - start)) + "\n";
    }
}

sealed class CharacterFile
{
    public string Name { get; set; } = "";

    public string Image { get; set; } = "";

    public string Sample { get; set; } = "";

    public string Author { get; set; } = "";

    public string Web { get; set; } = "";

    public bool HasName { get; set; }

    public bool HasImage { get; set; }

    public bool HasSample { get; set; }

    public bool HasAuthor { get; set; }

    public bool HasWeb { get; set; }

    public List<ProfileLine> Profile { get; } = [];

    public List<string> Extra { get; } = [];
}

sealed record ProfileLine(string Label, string Value, bool Fullwidth);

sealed class VoicebankYaml
{
    public string TextFileEncoding { get; set; } = VoicebankText.ShiftJis;

    public string Name { get; set; } = "";

    public string Image { get; set; } = "";

    public string Author { get; set; } = "";

    public string Voice { get; set; } = "";

    public string Version { get; set; } = "";

    public string Web { get; set; } = "";

    public bool HasName { get; set; }

    public bool HasImage { get; set; }

    public bool HasAuthor { get; set; }

    public bool HasWeb { get; set; }

    public bool HasVoice { get; set; }

    public bool HasVersion { get; set; }

    public List<(string Key, string Value)> LocalizedNames { get; } = [];

    public string Portrait { get; set; } = "";

    public double PortraitOpacity { get; set; } = 0.67;

    public int PortraitHeight { get; set; }

    public bool HasPortrait { get; set; }

    public bool HasOpacity { get; set; }

    public bool HasHeight { get; set; }

    public bool PreserveLocalizedBlock { get; set; }
}
