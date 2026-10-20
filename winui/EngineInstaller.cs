using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Otoasobi;

sealed class EngineRelease
{
    public required EngineManifest Manifest { get; init; }

    public required string PythonLocation { get; init; }

    public required string ModelLocation { get; init; }

    public long PythonBytes { get; init; }

    public long ModelBytes { get; init; }

    public static async Task<EngineRelease?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var feed = AppPaths.FeedDirectory;
        if (feed != null)
            return LoadLocal(feed);

        using var response = await EngineInstaller.Api.GetAsync(AppPaths.LatestReleaseApi, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new IOException("公開されているリリースがありません。");
        if (!response.IsSuccessStatusCode)
            throw new IOException("更新情報を取得できませんでした。");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("assets", out var assets))
            return null;

        string? manifestUrl = null;
        var files = new List<(string Name, long Size, string Url)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            var size = asset.TryGetProperty("size", out var sizeValue) ? sizeValue.GetInt64() : 0;
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() ?? "" : "";
            if (name.Length == 0 || url.Length == 0)
                continue;
            files.Add((name, size, url));
            if (string.Equals(name, AppPaths.ManifestName, StringComparison.Ordinal))
                manifestUrl = url;
        }

        if (manifestUrl == null)
            return null;

        var json = await EngineInstaller.Files.GetStringAsync(manifestUrl, cancellationToken);
        var manifest = JsonSerializer.Deserialize<EngineManifest>(json, JsonFiles.Options)
            ?? throw new InvalidDataException("engine.json を読めませんでした。");
        manifest.EnsureValid();
        var python = Require(files, manifest.Python);
        var model = Require(files, manifest.Model);
        return new EngineRelease
        {
            Manifest = manifest,
            PythonLocation = python.Url,
            ModelLocation = model.Url,
            PythonBytes = manifest.PythonSize > 0 ? manifest.PythonSize : python.Size,
            ModelBytes = manifest.ModelSize > 0 ? manifest.ModelSize : model.Size,
        };
    }

    static EngineRelease? LoadLocal(string feed)
    {
        var manifestPath = Path.Combine(feed, AppPaths.ManifestName);
        if (!File.Exists(manifestPath))
            return null;
        var manifest = JsonFiles.Read<EngineManifest>(manifestPath)
            ?? throw new InvalidDataException("engine.json を読めませんでした。");
        manifest.EnsureValid();
        var python = RequireFile(feed, manifest.Python);
        var model = RequireFile(feed, manifest.Model);
        return new EngineRelease
        {
            Manifest = manifest,
            PythonLocation = python.Path,
            ModelLocation = model.Path,
            PythonBytes = manifest.PythonSize > 0 ? manifest.PythonSize : python.Size,
            ModelBytes = manifest.ModelSize > 0 ? manifest.ModelSize : model.Size,
        };
    }

    static (string Name, long Size, string Url) Require(List<(string Name, long Size, string Url)> files, string name)
    {
        foreach (var file in files)
        {
            if (string.Equals(file.Name, name, StringComparison.Ordinal))
                return file;
        }

        throw new IOException($"{name} がリリースにありません。");
    }

    static (long Size, string Path) RequireFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
            throw new IOException($"{name} がありません。");
        return (new FileInfo(path).Length, path);
    }
}

static class EngineInstaller
{
    internal static readonly HttpClient Api = CreateApi();
    internal static readonly HttpClient Files = CreateFiles();

    public static async Task InstallZipAsync(
        string location,
        string expectedSha256,
        string engineDirectory,
        long totalBytes,
        Action<long, long>? progress,
        Action? beforeExtract = null,
        CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(engineDirectory))
            ?? throw new IOException("エンジンの保存先がありません。");
        Directory.CreateDirectory(parent);
        var zipPath = Path.Combine(parent, "engine-download.zip");
        var staging = engineDirectory + "-new";
        try
        {
            await DownloadVerifiedAsync(location, zipPath, expectedSha256, totalBytes, progress, cancellationToken);
            beforeExtract?.Invoke();
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(zipPath, staging);
            PromotePython(staging);
            ReplaceDirectory(staging, engineDirectory);
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(staging);
            TryDeleteDirectory(staging + "-hoist");
        }
    }

    public static Task InstallFileAsync(
        string location,
        string expectedSha256,
        string destination,
        long totalBytes,
        Action<long, long>? progress,
        CancellationToken cancellationToken = default)
    {
        return DownloadVerifiedAsync(location, destination, expectedSha256, totalBytes, progress, cancellationToken);
    }

    public static async Task DownloadVerifiedAsync(
        string location,
        string destination,
        string expectedSha256,
        long totalBytes,
        Action<long, long>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new InvalidDataException("engine.json の検証情報がありません。");

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = destination + ".part";
        var total = totalBytes;
        if (total <= 0 && !IsHttp(location) && File.Exists(location))
            total = new FileInfo(location).Length;

        try
        {
            var actual = await CopyHashedAsync(location, temp, total, progress, cancellationToken);
            if (!actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ダウンロードしたファイルが壊れています。");
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
                File.Delete(temp);
            throw;
        }
    }

    static async Task<string> CopyHashedAsync(
        string location,
        string temp,
        long total,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        if (IsHttp(location))
        {
            using var response = await Files.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new IOException("ファイルをダウンロードできませんでした。");
            if (total <= 0 && response.Content.Headers.ContentLength is long length)
                total = length;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await WriteHashedAsync(input, temp, total, progress, cancellationToken);
        }

        await using var file = new FileStream(
            location,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (total <= 0)
            total = file.Length;
        return await WriteHashedAsync(file, temp, total, progress, cancellationToken);
    }

    static async Task<string> WriteHashedAsync(
        Stream input,
        string temp,
        long total,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            temp,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long done = 0;
        var lastReport = DateTime.UtcNow;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer.AsSpan(0, read));
            done += read;
            var now = DateTime.UtcNow;
            if (progress != null && now - lastReport >= TimeSpan.FromMilliseconds(100))
            {
                progress(done, total);
                lastReport = now;
            }
        }

        progress?.Invoke(done, total > 0 ? total : done);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    static void PromotePython(string staging)
    {
        if (File.Exists(Path.Combine(staging, "python.exe")))
            return;
        string? inner = null;
        foreach (var dir in Directory.GetDirectories(staging))
        {
            if (!File.Exists(Path.Combine(dir, "python.exe")))
                continue;
            if (inner != null)
                throw new InvalidDataException("エンジンの形が想定と違います。");
            inner = dir;
        }

        if (inner == null)
            throw new InvalidDataException("エンジンの形が想定と違います。");
        var hoist = staging + "-hoist";
        if (Directory.Exists(hoist))
            Directory.Delete(hoist, true);
        Directory.Move(inner, hoist);
        Directory.Delete(staging, true);
        Directory.Move(hoist, staging);
    }

    static void ReplaceDirectory(string staging, string destination)
    {
        var backup = destination + ".old";
        if (Directory.Exists(backup) && !Directory.Exists(destination))
            Directory.Move(backup, destination);
        if (Directory.Exists(backup))
            Directory.Delete(backup, true);
        if (Directory.Exists(destination))
            Directory.Move(destination, backup);
        try
        {
            Directory.Move(staging, destination);
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }

        if (Directory.Exists(backup))
            Directory.Delete(backup, true);
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static bool IsHttp(string location) =>
        location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || location.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    static HttpClient CreateApi()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OTOasobi");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    static HttpClient CreateFiles()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OTOasobi");
        return http;
    }
}
