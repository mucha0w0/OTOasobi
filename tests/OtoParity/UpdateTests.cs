using System.IO.Compression;
using System.Security.Cryptography;

namespace OtoParity;

public class UpdateTests
{
    [Fact]
    public void BetaInstallFollowsPrereleases()
    {
        Assert.True(Otoasobi.ReleaseTrack.IncludesPrerelease("0.1.0-beta.1"));
        Assert.True(Otoasobi.ReleaseTrack.IncludesPrerelease("0.1.0-beta.1+abcdef"));
        Assert.False(Otoasobi.ReleaseTrack.IncludesPrerelease("0.1.0"));
        Assert.False(Otoasobi.ReleaseTrack.IncludesPrerelease("0.1.0+abcdef"));
        Assert.False(Otoasobi.ReleaseTrack.IncludesPrerelease(null));
    }

    [Fact]
    public void EngineNeedSkipsPartsWhoseHashAndFileMatch()
    {
        var remote = new Otoasobi.EngineManifest
        {
            PythonSha256 = "aaa",
            ModelSha256 = "bbb",
        };
        var ready = Otoasobi.EngineNeed.Missing(
            remote,
            new Otoasobi.EngineState { PythonSha256 = "AAA", ModelSha256 = "bbb" },
            pythonExists: true,
            modelExists: true);
        Assert.False(ready.Python);
        Assert.False(ready.Model);

        var model = Otoasobi.EngineNeed.Missing(
            remote,
            new Otoasobi.EngineState { PythonSha256 = "aaa", ModelSha256 = "old" },
            pythonExists: true,
            modelExists: true);
        Assert.False(model.Python);
        Assert.True(model.Model);

        var missingFile = Otoasobi.EngineNeed.Missing(
            remote,
            new Otoasobi.EngineState { PythonSha256 = "aaa", ModelSha256 = "bbb" },
            pythonExists: false,
            modelExists: true);
        Assert.True(missingFile.Python);
        Assert.False(missingFile.Model);
    }

    [Fact]
    public void PromptListsOnlyThePartsThatWillDownload()
    {
        var prompt = Otoasobi.UpdatePromptBuilder.Build(
            "0.2.0",
            12 * 1024 * 1024,
            needPython: true,
            pythonBytes: 800L * 1024 * 1024,
            needModel: true,
            modelBytes: 1_262_047_414,
            firstEngine: true);
        Assert.Equal("バージョン 0.2.0 があります", prompt.Title);
        Assert.Equal("更新する", prompt.Confirm);
        Assert.Contains("更新しますか。", prompt.Body);
        Assert.Contains("音響エンジン", prompt.Body);
        Assert.Contains("アプリ（12 MB）", prompt.Body);
        Assert.Contains("高精度エンジン（800 MB）", prompt.Body);
        Assert.Contains("音声モデル（1.2 GB）", prompt.Body);

        var prepare = Otoasobi.UpdatePromptBuilder.Build(
            null,
            0,
            needPython: true,
            pythonBytes: 1000,
            needModel: false,
            modelBytes: 0,
            firstEngine: true);
        Assert.Equal("高精度エンジンを準備します", prepare.Title);
        Assert.Equal("準備する", prepare.Confirm);
        Assert.Contains("高精度エンジン（1000 B）", prepare.Body);
        Assert.DoesNotContain("音声モデル", prepare.Body);
    }

    [Fact]
    public async Task InstallZipKeepsPythonAndRejectsABadHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "otoasobi-update-test", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = await ZipWithPython(root, nested: false);
            var sha = await Sha256(zip);
            var engine = Path.Combine(root, "engine");
            await Otoasobi.EngineInstaller.InstallZipAsync(zip, sha, engine, new FileInfo(zip).Length, null);
            Assert.Equal("python", await File.ReadAllTextAsync(Path.Combine(engine, "python.exe")));

            var nestedZip = await ZipWithPython(root, nested: true);
            var nestedSha = await Sha256(nestedZip);
            var nestedEngine = Path.Combine(root, "nested");
            await Otoasobi.EngineInstaller.InstallZipAsync(nestedZip, nestedSha, nestedEngine, 0, null);
            Assert.True(File.Exists(Path.Combine(nestedEngine, "python.exe")));
            Assert.Equal("dll", await File.ReadAllTextAsync(Path.Combine(nestedEngine, "python312.dll")));

            var rejected = Path.Combine(root, "rejected");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                Otoasobi.EngineInstaller.InstallZipAsync(zip, "deadbeef", rejected, 0, null));
            Assert.False(Directory.Exists(rejected));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static async Task<string> ZipWithPython(string root, bool nested)
    {
        var source = Path.Combine(root, nested ? "nested-src" : "src");
        var folder = nested ? Path.Combine(source, "inner") : source;
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "python.exe"), "python");
        if (nested)
            await File.WriteAllTextAsync(Path.Combine(folder, "python312.dll"), "dll");
        var zip = Path.Combine(root, nested ? "nested.zip" : "engine.zip");
        ZipFile.CreateFromDirectory(source, zip);
        return zip;
    }

    static async Task<string> Sha256(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
