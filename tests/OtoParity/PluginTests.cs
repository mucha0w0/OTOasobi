using Otoasobi;

namespace OtoParity;

public class PluginTests
{
    [Fact]
    public void ResolveStaysInsideTheProject()
    {
        var root = Directory.CreateTempSubdirectory("oto-plugin-path");
        try
        {
            var file = PluginPath.Resolve(root.FullName, "sub/a.txt", allowRoot: false);
            Assert.Equal(Path.GetFullPath(Path.Combine(root.FullName, "sub", "a.txt")), file);
            Assert.Equal(Path.TrimEndingDirectorySeparator(root.FullName), PluginPath.Resolve(root.FullName, "", true));
            Assert.Null(PluginPath.Resolve(root.FullName, "", false));
            Assert.Null(PluginPath.Resolve(root.FullName, "..", false));
            Assert.Null(PluginPath.Resolve(root.FullName, "sub/../../outside.txt", false));
            Assert.Null(PluginPath.Resolve(root.FullName, @"C:\Windows\notepad.exe", false));
            Assert.Null(PluginPath.Resolve(root.FullName, "/etc/passwd", false));
            Assert.Null(PluginPath.Resolve("", "a.txt", false));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void IdsAndLayoutLeaves()
    {
        Assert.True(PluginPath.IsId("sample"));
        Assert.True(PluginPath.IsId("sample.checker"));
        Assert.False(PluginPath.IsId("Sample"));
        Assert.False(PluginPath.IsId(".sample"));
        Assert.False(PluginPath.IsId("sample."));
        Assert.Equal("sample-checker.plugins.otoasobi.assets", PluginPath.Host("sample.checker"));
        Assert.True(EditorLayout.IsValid(new LayoutNode { Id = "plugin.sample" }));
        Assert.True(EditorLayout.IsValid(new LayoutNode { Id = "plugin.sample.checker" }));
        Assert.False(EditorLayout.IsValid(new LayoutNode { Id = "plugin.Sample" }));
        Assert.False(EditorLayout.IsValid(new LayoutNode { Id = "missing" }));
    }

    [Fact]
    public void LoadSkipsManifestsThatLeaveTheFolder()
    {
        var root = Directory.CreateTempSubdirectory("oto-plugins");
        try
        {
            var good = Directory.CreateDirectory(Path.Combine(root.FullName, "sample"));
            File.WriteAllText(Path.Combine(good.FullName, "manifest.json"), """{"title":"見本","entry":"index.html"}""");
            File.WriteAllText(Path.Combine(good.FullName, "index.html"), "<!DOCTYPE html>");
            var mismatch = Directory.CreateDirectory(Path.Combine(root.FullName, "other"));
            File.WriteAllText(Path.Combine(mismatch.FullName, "manifest.json"), """{"id":"nope","title":"外","entry":"index.html"}""");
            File.WriteAllText(Path.Combine(mismatch.FullName, "index.html"), "<!DOCTYPE html>");
            var outside = Directory.CreateDirectory(Path.Combine(root.FullName, "escape"));
            File.WriteAllText(Path.Combine(outside.FullName, "manifest.json"), """{"title":"外","entry":"../index.html"}""");
            var catalog = PluginCatalog.Load(root.FullName);
            Assert.Equal("sample", Assert.Single(catalog.Items).Id);
            Assert.Equal("https://sample.plugins.otoasobi.assets", catalog.Items[0].Origin);
            Assert.Equal(["escape", "other"], catalog.Errors.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            root.Delete(true);
        }
    }
}
