namespace Otoasobi;

// 外部パネルの id と、音源フォルダの中だけに収まる相対パス。
public static class PluginPath
{
    public const int MaxBytes = 256 * 1024;

    public static bool IsId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        var dot = true;
        foreach (var c in id)
        {
            if (c == '.')
            {
                if (dot)
                    return false;
                dot = true;
                continue;
            }

            var lower = c is >= 'a' and <= 'z';
            var digit = c is >= '0' and <= '9';
            if (!lower && !digit)
                return false;
            dot = false;
        }

        return !dot;
    }

    public static string Host(string id) => id.Replace('.', '-') + ".plugins.otoasobi.assets";

    public static string Origin(string id) => "https://" + Host(id);

    public static string? Resolve(string? project, string? relative, bool allowRoot)
    {
        if (string.IsNullOrWhiteSpace(project))
            return null;
        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var text = (relative ?? "").Replace('/', '\\').Trim();
        if (text.Length == 0)
            return allowRoot ? root : null;
        if (text.Contains(':') || text[0] is '\\' or '/')
            return null;
        var parts = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return allowRoot ? root : null;
        foreach (var part in parts)
        {
            if (part is "." or "..")
                return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, string.Join('\\', parts)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return full;
    }
}
