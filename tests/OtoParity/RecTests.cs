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
    public void WavOfFindsTheListRowAndOutsideFiles()
    {
        var line = new RecLine("あ", "", null);
        Assert.Equal("あ.wav", RecNames.WavOf(line, "あ.wav　未録音"));
        Assert.Equal("さ.wav", RecNames.WavOf(null, "さ.wav"));
        Assert.Equal("", RecNames.WavOf(null, "メモ"));
        Assert.Equal(1, RecNames.IndexOfWav([line, new RecLine("か", "", null)], "か.WAV"));
        Assert.Equal(-1, RecNames.IndexOfWav([line], "さ.wav"));
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
    public void MoraGuideRepeatsHalfNotes()
    {
        const int rate = 44100;
        var guide = RecTone.MoraGuide(261.63, 120, 5, rate);
        Assert.Equal(rate * 5, guide.Length);
        Assert.All(guide, sample => Assert.InRange(sample, -1f, 1f));
        for (var mora = 0; mora < 5; mora++)
        {
            var start = mora * rate;
            Assert.True(guide[start + rate / 50] > 0.05f);
        }
    }

    [Fact]
    public void ManualGuidePutsCountInBeforeMoras()
    {
        const int rate = 44100;
        var plain = RecTone.ManualGuide(261.63, 120, 5, rate, preCount: false, 4, countWhileMoras: false);
        var withCount = RecTone.ManualGuide(261.63, 120, 5, rate, preCount: true, 4, countWhileMoras: false);
        Assert.Equal(rate * 5, plain.Length);
        Assert.Equal(rate * 2 + plain.Length, withCount.Length);
        Assert.True(withCount[rate / 50] > 0.1f);
        Assert.True(withCount[rate * 2 + rate / 50] > 0.05f);
    }

    [Fact]
    public void ManualGuideMixesClicksIntoMoras()
    {
        const int rate = 44100;
        var plain = RecTone.ManualGuide(261.63, 120, 2, rate, preCount: false, 4, countWhileMoras: false);
        var mixed = RecTone.ManualGuide(261.63, 120, 2, rate, preCount: false, 4, countWhileMoras: true);
        Assert.Equal(plain.Length, mixed.Length);
        var beat = rate / 2;
        Assert.True(Math.Abs(mixed[beat / 50]) > Math.Abs(plain[beat / 50]));
    }

    [Fact]
    public void CountInLengthFollowsBeatCount()
    {
        const int rate = 44100;
        Assert.Equal(rate, RecTone.CountIn(120, rate, 2).Length);
        Assert.Equal(rate * 2, RecTone.CountIn(120, rate, 4).Length);
    }

    [Fact]
    public void GuideSpeedAndPitchStayIndependent()
    {
        const int rate = 44100;
        var input = new float[(int)(rate * 0.4)];
        for (var i = 0; i < input.Length; i++)
            input[i] = (float)Math.Sin(2 * Math.PI * 440 * i / rate);
        var faster = RecTone.ShapeGuide(input, 1, rate, 2, 0);
        var higher = RecTone.ShapeGuide(input, 1, rate, 1, 12);
        var both = RecTone.ShapeGuide(input, 1, rate, 2, 12);

        Assert.InRange(faster.Length, input.Length / 2 - 800, input.Length / 2 + 800);
        Assert.InRange(higher.Length, input.Length - 1200, input.Length + 1200);
        Assert.InRange(both.Length, input.Length / 2 - 800, input.Length / 2 + 800);
        Assert.InRange(Fundamental(faster, rate), 400, 480);
        Assert.InRange(Fundamental(higher, rate), 800, 960);
        Assert.InRange(Fundamental(both, rate), 800, 960);
        Assert.Same(input, RecTone.ShapeGuide(input, 1, rate, 1, 0));
    }

    static double Fundamental(float[] samples, int rate)
    {
        var start = samples.Length / 5;
        var end = samples.Length * 4 / 5;
        var crossings = 0;
        for (var i = start + 1; i < end; i++)
        {
            if (samples[i - 1] <= 0 && samples[i] > 0)
                crossings++;
        }

        return crossings / ((end - start) / (double)rate);
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
    public void GuideBgmIsAWavAndTheSameNamedText()
    {
        Assert.False(RecFolderStore.IsGuideWav(null));
        Assert.False(RecFolderStore.IsGuideWav(@"C:\guide"));
        Assert.True(RecFolderStore.IsGuideWav(@"C:\guide\song.WAV"));
        Assert.Equal(@"C:\guide\song.txt", RecFolderStore.GuideTextPath(@"C:\guide\song.WAV"));
        Assert.True(RecFolderStore.UseManualGuide(null, null));
        Assert.False(RecFolderStore.UseManualGuide(null, @"C:\guide\song.wav"));
        Assert.False(RecFolderStore.UseManualGuide(false, null));
        Assert.True(RecFolderStore.UseManualGuide(true, @"C:\guide\song.wav"));
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
        Assert.True(EditorLayout.Contains(recording, "recScript"));
        Assert.True(EditorLayout.Contains(recording, "guide"));
        Assert.True(EditorLayout.Contains(recording, "mic"));
        Assert.False(EditorLayout.Contains(EditorLayout.CreateDefault(), "rec"));
        Assert.False(EditorLayout.Contains(EditorLayout.CreateDefault(), "recScript"));
        Assert.False(EditorLayout.Contains(EditorLayout.CreateDefault(), "guide"));
        Assert.False(EditorLayout.Contains(EditorLayout.CreateDefault(), "mic"));
        Assert.False(EditorLayout.Contains(recording, "oto"));
        var template = LayoutTemplate.CreateRecording();
        Assert.Equal("録音", template.Name);
        Assert.True(template.BuiltIn);
        Assert.True(template.Matches(recording, false));
    }

    [Fact]
    public void MeterUsesDecibels()
    {
        Assert.Equal(0, RecMeter.Normalize(0));
        Assert.Equal(1, RecMeter.Normalize(1));
        Assert.Equal(0.5, RecMeter.Normalize((float)Math.Pow(10, -30.0 / 20)), 2);
    }

    [Fact]
    public void LiveBufferKeepsTheNewestWindow()
    {
        var buffer = new RecLiveBuffer();
        buffer.Write([1, 2], 1);
        var (early, rate) = buffer.Snapshot();
        Assert.Equal(1, rate);
        Assert.Equal([0f, 1, 2], early);

        buffer.Write([3, 4], 1);
        var (full, _) = buffer.Snapshot();
        Assert.Equal([2f, 3, 4], full);
    }

    [Fact]
    public void PeakTrackFlushesEveryBucket()
    {
        var track = new RecPeakTrack();
        var samples = new float[RecPeakTrack.Bucket + 1];
        Array.Fill(samples, 0.25f);
        samples[^1] = -0.5f;
        track.Append(samples);
        var (min, max) = track.Snapshot();
        Assert.Equal(RecPeakTrack.Bucket + 1, track.Samples);
        Assert.Equal(2, min.Length);
        Assert.Equal(0.25f, min[0]);
        Assert.Equal(0.25f, max[0]);
        Assert.Equal(-0.5f, min[1]);
        Assert.Equal(-0.5f, max[1]);
    }

    [Fact]
    public void TakeHeaderIs44100Mono16WhileInputRateStaysSeparate()
    {
        const int inputRate = 48000;
        var interleaved = new float[inputRate * 2];
        for (var frame = 0; frame < inputRate; frame++)
        {
            interleaved[frame * 2] = 0.25f;
            interleaved[frame * 2 + 1] = -0.25f;
        }

        var mono = RecTone.ToMono44100(interleaved, 2, inputRate);
        var path = Path.Combine(Path.GetTempPath(), "otoasobi-take-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavAudio.Write(path, new WavAudio
            {
                SampleRate = RecTone.TargetRate,
                Channels = 1,
                BitsPerSample = 16,
                Samples = mono,
            });
            var read = WavAudio.Read(path);
            var seconds = read.Samples.Length / (double)read.Channels / read.SampleRate;
            Assert.Equal(44100, read.SampleRate);
            Assert.Equal(16, read.BitsPerSample);
            Assert.Equal(1, read.Channels);
            Assert.NotEqual(inputRate, read.SampleRate);
            Assert.InRange(seconds, 0.98, 1.02);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
