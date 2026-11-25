using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Otoasobi;

// 音源フォルダの作業状態。作業中の原音設定、録音の選択、解析の区間を 1 ファイルに置く。
// 中身は JSON。拡張子だけ独自。
static class FolderState
{
    public const string FileName = "OTOasobi.otoasobi";
    public const int Version = 1;

    const string SessionKey = "session";
    const string RecordingKey = "recording";
    const string CacheKey = "cache";

    static readonly (string Key, string[] Names)[] Legacy =
    [
        (SessionKey, ["OTOasobi_作業中.json", "OTOasibi_作業中.json"]),
        (RecordingKey, ["OTOasobi_録音.json", "OTOasibi_録音.json"]),
        (CacheKey, ["OTOasobi_cache.json", "OTOasibi_cache.json"]),
    ];

    public static string PathFor(string folder) => Path.Combine(folder, FileName);

    public static bool HasSession(string folder)
    {
        try
        {
            return Section(folder, SessionKey) != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return true;
        }
    }

    public static JsonNode? Section(string folder, string key)
    {
        return WithDocument(folder, document => document[key]?.DeepClone());
    }

    public static void WriteSection(string folder, string key, JsonNode? value)
    {
        WithDocument(folder, document =>
        {
            document[key] = value?.DeepClone();
            return true;
        });
    }

    public static void ClearCache(string folder)
    {
        var path = PathFor(folder);
        if (!File.Exists(path))
            return;
        WithDocument(folder, document =>
        {
            document.Remove(CacheKey);
            return true;
        });
    }

    static T WithDocument<T>(string folder, Func<JsonObject, T> change)
    {
        Directory.CreateDirectory(folder);
        var path = PathFor(folder);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var document = Read(stream);
                var imported = ImportLegacy(folder, document);
                var result = change(document);
                if (imported || result is true)
                    Write(stream, document);
                if (imported)
                    DeleteLegacy(folder);
                return result;
            }
            catch (IOException) when (attempt < 49)
            {
                Thread.Sleep(20);
            }
        }

        throw new IOException("作業ファイルを開けませんでした。");
    }

    static JsonObject Read(FileStream stream)
    {
        if (stream.Length == 0)
            return NewDocument();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = reader.ReadToEnd();
        stream.Seek(0, SeekOrigin.Begin);
        if (string.IsNullOrWhiteSpace(text))
            return NewDocument();
        var node = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("作業ファイルの形式が違います。");
        if (node["version"] is JsonValue version && version.TryGetValue<int>(out var number) && number > Version)
            throw new InvalidDataException("この保存データは、いまの OTOasobi では読めません。");
        node["version"] = Version;
        return node;
    }

    static void Write(FileStream stream, JsonObject document)
    {
        var json = document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        stream.SetLength(0);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        writer.Write(json);
        writer.Write('\n');
        writer.Flush();
    }

    static bool ImportLegacy(string folder, JsonObject document)
    {
        var imported = false;
        foreach (var (key, names) in Legacy)
        {
            if (document[key] != null)
                continue;
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                if (!File.Exists(path))
                    continue;
                document[key] = JsonNode.Parse(File.ReadAllText(path));
                imported = true;
                break;
            }
        }

        return imported;
    }

    static void DeleteLegacy(string folder)
    {
        foreach (var (_, names) in Legacy)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    static JsonObject NewDocument() => new()
    {
        ["version"] = Version,
    };
}
