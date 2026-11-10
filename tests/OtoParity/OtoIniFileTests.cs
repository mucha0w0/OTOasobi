using Otoasobi;

namespace OtoParity;

public class OtoIniFileTests
{
    [Fact]
    public void ParseAndRenderRoundTrip()
    {
        const string line = "a.wav=あ,10.000,50.000,20.000,40.000,5.000";
        Assert.True(OtoIniFile.TryParseLine(line, out var file, out var alias, out var mark));
        Assert.Equal("a.wav", file);
        Assert.Equal("あ", alias);
        var again = OtoIniFile.RenderLine(file, alias, mark);
        Assert.Equal(line, again);
    }

    [Fact]
    public void MarksForFileFiltersByName()
    {
        var folder = Path.Combine(Path.GetTempPath(), "oto-ini-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "oto.ini");
            File.WriteAllText(path, "a.wav=あ,0,0,0,0,0\r\nb.wav=い,1,1,1,1,1\r\n", new System.Text.UTF8Encoding(false));
            var marks = OtoIniFile.MarksForFile(folder, "a.wav");
            Assert.Single(marks);
            Assert.Equal("あ", marks[0].Alias);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
