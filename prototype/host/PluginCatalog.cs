using System.Text.Json;

namespace Otoasobi;

// %LocalAppData%\Otoasobi\plugins\<id>\manifest.json。フォルダ名が id。
public sealed class PluginCatalog
{
    public static PluginCatalog Empty { get; } = new([], []);

    PluginCatalog(List<Item> items, List<string> errors)
    {
        Items = items;
        Errors = errors;
    }

    public IReadOnlyList<Item> Items { get; }

    public IReadOnlyList<string> Errors { get; }

    public Item? Find(string id)
    {
        foreach (var item in Items)
        {
            if (item.Id == id)
                return item;
        }

        return null;
    }

    public static PluginCatalog Load(string? root)
    {
        var items = new List<Item>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new PluginCatalog(items, errors);
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PluginCatalog(items, errors);
        }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (TryLoad(dir, name, out var item))
                items.Add(item!);
            else
                errors.Add(name);
        }

        return new PluginCatalog(items, errors);
    }

    static bool TryLoad(string dir, string name, out Item? item)
    {
        item = null;
        if (!PluginPath.IsId(name))
            return false;
        JsonDocument doc;
        try
        {
            var path = Path.Combine(dir, "manifest.json");
            if (!File.Exists(path))
                return false;
            doc = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            var id = Optional(root, "id");
            if (!string.IsNullOrEmpty(id) && !string.Equals(id, name, StringComparison.Ordinal))
                return false;
            var title = (Optional(root, "title") ?? "").Trim();
            if (title.Length == 0 || title.Length > 80)
                return false;
            var entryText = Optional(root, "entry");
            var entry = string.IsNullOrWhiteSpace(entryText) ? "index.html" : entryText.Trim().Replace('\\', '/');
            if (!FileInside(dir, entry, out _))
                return false;
            string? script = null;
            var scriptText = Optional(root, "script");
            if (!string.IsNullOrWhiteSpace(scriptText))
            {
                var relative = scriptText.Trim().Replace('\\', '/');
                if (!relative.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || !FileInside(dir, relative, out var scriptPath))
                    return false;
                script = scriptPath;
            }

            item = new Item(name, title, PluginPath.Host(name), PluginPath.Origin(name), dir, entry, script);
            return true;
        }
    }

    static string? Optional(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static bool FileInside(string root, string relative, out string full)
    {
        full = "";
        var resolved = PluginPath.Resolve(root, relative, allowRoot: false);
        if (resolved == null || !File.Exists(resolved))
            return false;
        full = resolved;
        return true;
    }

    public sealed record Item(string Id, string Title, string Host, string Origin, string Directory, string Entry, string? Script);
}
