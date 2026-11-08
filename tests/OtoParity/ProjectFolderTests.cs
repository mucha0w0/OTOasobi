using Otoasobi;

namespace OtoParity;

public class ProjectFolderTests
{
    [Fact]
    public void CheckNameRejectsEmptyReservedAndIllegalCharacters()
    {
        Assert.Equal("名前を入力してください。", ProjectFolder.CheckName("  ", out _));
        Assert.Equal("この名前は使えません。", ProjectFolder.CheckName("CON", out _));
        Assert.Equal("この名前は使えません。", ProjectFolder.CheckName("..", out _));
        Assert.Equal("フォルダ名に使えない文字があります。", ProjectFolder.CheckName("a/b", out _));
        Assert.Equal("名前の末尾に空白やピリオドは使えません。", ProjectFolder.CheckName("音源.", out _));
        Assert.Null(ProjectFolder.CheckName(" 試し ", out var name));
        Assert.Equal("試し", name);
    }

    [Fact]
    public void CombineStaysInsideTheParent()
    {
        var root = Directory.CreateTempSubdirectory("oto-project-combine");
        try
        {
            var path = ProjectFolder.Combine(root.FullName, "音源");
            Assert.Equal(Path.Combine(root.FullName, "音源"), path);
            Assert.Null(ProjectFolder.Combine(root.FullName, ".."));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void WriteStoresTheNameInCharacterFiles()
    {
        var root = Directory.CreateTempSubdirectory("oto-project-write");
        try
        {
            ProjectFolder.Write(root.FullName, "試し");
            var character = VoicebankText.ParseCharacter(VoicebankText.Decode(File.ReadAllBytes(Path.Combine(root.FullName, "character.txt"))));
            var yaml = VoicebankText.ReadYaml(File.ReadAllText(Path.Combine(root.FullName, "character.yaml")));
            Assert.Equal("試し", character.Name);
            Assert.Equal("試し", yaml.Name);
            Assert.Equal(VoicebankText.ShiftJis, yaml.TextFileEncoding);
        }
        finally
        {
            root.Delete(true);
        }
    }
}
