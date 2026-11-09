using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otoasobi;

// LocalAppData とフォルダ保存で同じ JSON の読み書き。値の検査は呼び出し側。
static class JsonFiles
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonSerializerOptions OptionsIgnoringNull { get; } = new(Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<T> ReadList<T>(string path, Func<IEnumerable<T>?, List<T>> normalize)
    {
        try
        {
            if (!File.Exists(path))
                return [];
            var list = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Options);
            return normalize(list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static T? Read<T>(string path, JsonSerializerOptions? options = null) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options ?? Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(value, options ?? Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
