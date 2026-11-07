using System.Text;
using Otoasobi;

namespace OtoParity;

public class RecTests
{
    [Fact]
    public void ParseKeepsCommentsAndSkipsNotes()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(932).GetBytes("あ\t息を多めに\r\n# メモ\r\n\r\nか.wav\r\n");
        var lines = RecList.Parse(bytes);

        Assert.Equal(2, lines.Count);
        Assert.Equal("あ", lines[0].Sing);
        Assert.Equal("息を多めに", lines[0].Comment);
        Assert.Null(lines[0].NameError);
        Assert.Equal("か", lines[1].Sing);
        Assert.Equal("", lines[1].Comment);
    }

    [Fact]
    public void StemAllowsKanaAndRejectsLooseSmallKana()
    {
        Assert.Null(RecNames.StemError("しゃ"));
        Assert.Null(RecNames.StemError("_あ"));
        Assert.Null(RecNames.StemError("ヴァ"));
        Assert.Equal("使えない文字です: a", RecNames.StemError("a"));
        Assert.Contains("小書き", RecNames.StemError("ゃ"));
        Assert.Contains("小書き", RecNames.StemError("んゃ"));
    }

    [Fact]
    public void WavNameIsThePhonemeFile()
    {
        var wav = RecNames.WavName("あ");
        Assert.Equal("あ.wav", wav);
        Assert.Null(RecNames.WavError(wav));
        Assert.True(RecNames.CanRecord(new RecLine("あ", "", null), wav));
        Assert.Equal(RecNames.Recorded, RecNames.Status(new RecLine("あ", "", null), exists: true));
        Assert.Equal(RecNames.Invalid, RecNames.Status(new RecLine("a", "", "使えない文字です: a"), exists: false));
    }

    [Fact]
    public void OutsideWavsAreFolderFilesMissingFromTheList()
    {
        var lines = new[] { new RecLine("あ", "", null), new RecLine("か", "", null) };
        var extra = RecNames.Outside(["か.WAV", "さ.wav", "あ.wav", "C4あ.wav"], lines);
        Assert.Equal(["C4あ.wav", "さ.wav"], extra);
    }

    [Fact]
    public void NotesUseA440()
    {
        Assert.True(RecTone.TryHertz("A4", out var a4));
        Assert.Equal(440, a4, 5);
        Assert.True(RecTone.TryHertz("C4", out var c4));
        Assert.Equal(261.6256, c4, 3);
        Assert.False(RecTone.TryHertz("H4", out _));
    }

    [Fact]
    public void ToneAndCountInStayInsideOne()
    {
        var tone = RecTone.Tone(440, 44100, 0.5);
        Assert.Equal(22050, tone.Length);
        Assert.All(tone, sample => Assert.InRange(sample, -1f, 1f));
        var clicks = RecTone.CountIn(120, 44100);
        Assert.Equal(44100 * 2, clicks.Length);
        Assert.Contains(clicks, sample => sample > 0.1f);
    }

    [Fact]
    public void DownmixAndResample()
    {
        var mono = RecTone.Downmix([1f, -1f, 0.5f, 0.5f], 2);
        Assert.Equal([0f, 0.5f], mono);
        var stretched = RecTone.Resample([0f, 1f], 22050, 44100);
        Assert.Equal(4, stretched.Length);
        var same = RecTone.ToMono44100([0.25f, 0.75f], 1, 44100);
        Assert.Equal([0.25f, 0.75f], same);
    }

    [Fact]
    public void ClearedListIsAnEmptyString()
    {
        Assert.True(RecFolderStore.ListIsCleared(""));
        Assert.True(RecFolderStore.ListIsCleared("  "));
        Assert.False(RecFolderStore.ListIsCleared(null));
        Assert.False(RecFolderStore.ListIsCleared("録音リスト.txt"));
    }

    [Fact]
    public void RecentListsKeepTheNewestAndDropDuplicates()
    {
        var older = RecListHistory.Merge(@"C:\voice\あ.txt", null);
        var newer = RecListHistory.Merge(@"D:\lists\い.txt", older);
        Assert.Equal(2, newer.Count);
        Assert.EndsWith("い.txt", newer[0]);
        Assert.EndsWith("あ.txt", newer[1]);
        var again = RecListHistory.Merge(@"C:\voice\あ.txt", newer);
        Assert.Equal(2, again.Count);
        Assert.EndsWith("あ.txt", again[0]);
        Assert.EndsWith("い.txt", again[1]);
        Assert.Empty(RecListHistory.Merge("  ", ["", "   "]));
    }

    [Fact]
    public void RecentListsStopAtTwenty()
    {
        IReadOnlyList<string> current = [];
        foreach (var index in Enumerable.Range(0, 25))
            current = RecListHistory.Merge($@"C:\lists\list{index}.txt", current);
        Assert.Equal(RecListHistory.MaxCount, current.Count);
        Assert.EndsWith("list24.txt", current[0]);
        Assert.EndsWith("list5.txt", current[19]);
    }

    [Fact]
    public void SameFileNameShowsTheFolder()
    {
        var paths = new[] { @"C:\a\リスト.txt", @"C:\b\リスト.txt", @"C:\a\別.txt" };
        Assert.Equal("リスト.txt (a)", RecListHistory.Caption(paths[0], paths));
        Assert.Equal("リスト.txt (b)", RecListHistory.Caption(paths[1], paths));
        Assert.Equal("別.txt", RecListHistory.Caption(paths[2], paths));
    }

    [Fact]
    public void RecordingLayoutIsSeparateFromTheDefault()
    {
        var recording = EditorLayout.CreateRecording();
        Assert.True(EditorLayout.IsValid(recording));
        Assert.True(EditorLayout.Contains(recording, "rec"));
        Assert.True(EditorLayout.Contains(recording, "recList"));
        Assert.False(EditorLayout.Contains(EditorLayout.CreateDefault(), "rec"));
        Assert.False(EditorLayout.Contains(recording, "oto"));
        var template = LayoutTemplate.CreateRecording();
        Assert.Equal("録音", template.Name);
        Assert.True(template.BuiltIn);
        Assert.True(template.Matches(recording, false));
    }
}
