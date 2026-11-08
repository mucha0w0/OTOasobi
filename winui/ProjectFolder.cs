using System.Text;

namespace Otoasobi;

// トップ画面の「新しいプロジェクト」。空の音源フォルダと、その名前の character.txt / character.yaml。
static class ProjectFolder
{
    public static string? CheckName(string? raw, out string name)
    {
        name = (raw ?? "").Trim();
        if (name.Length == 0)
            return "名前を入力してください。";
        if (name.Length > 80)
            return "名前が長すぎます。";
        if (name is "." or "..")
            return "この名前は使えません。";
        if (name.EndsWith(' ') || name.EndsWith('.'))
            return "名前の末尾に空白やピリオドは使えません。";
        foreach (var c in name)
        {
            if (c < 32 || "<>:\"/\\|?*".Contains(c))
                return "フォルダ名に使えない文字があります。";
        }

        if (IsReserved(name))
            return "この名前は使えません。";
        return null;
    }

    public static string? Combine(string parent, string name)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var path = Path.GetFullPath(Path.Combine(root, name));
        var container = Path.GetDirectoryName(path);
        if (container == null)
            return null;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(container), root, StringComparison.OrdinalIgnoreCase))
            return null;
        return path;
    }

    public static void Write(string folder, string name)
    {
        var character = new CharacterFile { Name = name, HasName = true };
        var body = VoicebankText.FormatCharacter(character);
        var encoding = VoicebankText.Resolve([], body);
        File.WriteAllBytes(Path.Combine(folder, "character.txt"), VoicebankText.EncodeCharacter(character, encoding));
        var yaml = new VoicebankYaml
        {
            Name = name,
            HasName = true,
            TextFileEncoding = encoding,
        };
        File.WriteAllText(Path.Combine(folder, "character.yaml"), VoicebankText.ApplyYaml(null, yaml), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    static bool IsReserved(string name)
    {
        var stem = name;
        var dot = name.IndexOf('.');
        if (dot >= 0)
            stem = name[..dot];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        if (stem.Length != 4)
            return false;
        var head = stem[..3];
        var tail = stem[3];
        return (head.Equals("COM", StringComparison.OrdinalIgnoreCase) || head.Equals("LPT", StringComparison.OrdinalIgnoreCase))
            && tail is >= '1' and <= '9';
    }
}
