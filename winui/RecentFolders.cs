using System.Text.Json;

namespace Otoasobi;

public sealed class RecentFolder
{
    public RecentFolder(string path)
    {
        Path = path;
        var name = System.IO.Path.GetFileName(path);
        Name = name.Length == 0 ? path : name;
    }

    public string Path { get; }
    public string Name { get; }
}

static class RecentFolders
{
    const int MaxCount = 10;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "recent-folders.json");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath), JsonOptions);
            if (list == null)
                return [];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var raw in list)
            {
                if (!TryNormalize(raw, out var path) || !seen.Add(path))
                    continue;
                result.Add(path);
                if (result.Count == MaxCount)
                    break;
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Remember(string folder)
    {
        if (!TryNormalize(folder, out var path))
            return;
        var next = new List<string> { path };
        foreach (var existing in Load())
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                continue;
            next.Add(existing);
            if (next.Count == MaxCount)
                break;
        }

        Save(next);
    }

    public static void Forget(string folder)
    {
        if (!TryNormalize(folder, out var path))
            return;
        var next = Load()
            .Where(existing => !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Save(next);
    }

    static void Save(IReadOnlyList<string> folders)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(folders, JsonOptions);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static bool TryNormalize(string? folder, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        try
        {
            var full = Path.GetFullPath(folder);
            if (full.Length > 1024)
                return false;
            path = IsDriveRoot(full)
                ? full
                : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path.Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    static bool IsDriveRoot(string path)
    {
        return path.Length == 3 && path[1] == ':' && path[2] == Path.DirectorySeparatorChar;
    }
}
