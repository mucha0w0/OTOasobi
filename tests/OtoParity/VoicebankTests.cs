using System.Text;
using Otoasobi;

namespace OtoParity;

public class VoicebankTests
{
    [Fact]
    public void CharacterTextKeepsFieldsProfileAndLooseLines()
    {
        var parsed = VoicebankText.ParseCharacter("""
            name=闇音レンリ
            image=icon.bmp
            sample=
            author=ゆずり
            web=https://example.com/voice
            Spell ： Giga Lacan
            推奨音域 : F2～F4
            http://example.com/note
            foo:bar

            """);
        Assert.Equal("闇音レンリ", parsed.Name);
        Assert.Equal("icon.bmp", parsed.Image);
        Assert.Equal("", parsed.Sample);
        Assert.True(parsed.HasSample);
        Assert.Equal("ゆずり", parsed.Author);
        Assert.Equal("https://example.com/voice", parsed.Web);
        Assert.Equal(3, parsed.Profile.Count);
        Assert.Equal(("Spell", "Giga Lacan", true), (parsed.Profile[0].Label, parsed.Profile[0].Value, parsed.Profile[0].Fullwidth));
        Assert.Equal(("推奨音域", "F2～F4", false), (parsed.Profile[1].Label, parsed.Profile[1].Value, parsed.Profile[1].Fullwidth));
        Assert.Equal(("foo", "bar", false), (parsed.Profile[2].Label, parsed.Profile[2].Value, parsed.Profile[2].Fullwidth));
        Assert.Equal(["http://example.com/note"], parsed.Extra);

        var again = VoicebankText.ParseCharacter(VoicebankText.FormatCharacter(parsed));
        Assert.Equal(parsed.Name, again.Name);
        Assert.Equal("", again.Sample);
        Assert.Equal("Spell ： Giga Lacan", again.Profile[0].Label + " ： " + again.Profile[0].Value);
        Assert.Equal("foo : bar", again.Profile[2].Label + " : " + again.Profile[2].Value);
        Assert.Equal("http://example.com/note", again.Extra[0]);
    }

    [Fact]
    public void ShiftJisAndUtf8AreToldApart()
    {
        var sjis = VoicebankText.EncodeCharacter(new CharacterFile { Name = "名前" }, VoicebankText.ShiftJis);
        var utf8 = VoicebankText.EncodeCharacter(new CharacterFile { Name = "名前" }, VoicebankText.Utf8Name);
        var ascii = VoicebankText.EncodeCharacter(new CharacterFile { Name = "abc" }, VoicebankText.ShiftJis);
        Assert.Equal(VoicebankText.ShiftJis, VoicebankText.Classify(sjis));
        Assert.Equal(VoicebankText.Utf8Name, VoicebankText.Classify(utf8));
        Assert.Null(VoicebankText.Classify(ascii));
        Assert.Equal("名前", VoicebankText.ParseCharacter(VoicebankText.Decode(sjis)).Name);
        Assert.Equal("名前", VoicebankText.ParseCharacter(VoicebankText.Decode(utf8)).Name);
    }

    [Fact]
    public void EncodingFollowsTheExistingTextFiles()
    {
        Assert.Equal(VoicebankText.ShiftJis, VoicebankText.Resolve([], "名前"));
        Assert.Equal(VoicebankText.Utf8Name, VoicebankText.Resolve([("character.txt", VoicebankText.Utf8Name)], "名前"));
        Assert.Equal(VoicebankText.ShiftJis, VoicebankText.Resolve([("oto.ini", VoicebankText.ShiftJis)], "名前"));
        Assert.Equal(
            VoicebankText.ShiftJis,
            VoicebankText.Resolve(
                [("character.txt", VoicebankText.Utf8Name), ("oto.ini", VoicebankText.ShiftJis)],
                "名前"));
        Assert.Equal(VoicebankText.Utf8Name, VoicebankText.Resolve([], "a😀"));
        Assert.Equal(VoicebankText.ShiftJis, VoicebankText.Resolve([("prefix.map", VoicebankText.ShiftJis)], "a😀"));
        Assert.False(VoicebankText.CanEncode("a😀", VoicebankText.ShiftJis));
    }

    [Fact]
    public void YamlKeepsUnknownBlocksAndSyncsCharacterFields()
    {
        var existing = """
            # keep
            singer_type: enunu
            text_file_encoding: utf-8
            default_phonemizer: OpenUtau.Plugin.Builtin.JapaneseCVVCPhonemizer
            subbanks:
            - color: ""
              prefix: ""
              suffix: CV
            localized_names:
              en-US: Old Name
            name: old
            portrait: portrait.png
            portrait_opacity: 0.5
            portrait_height: 800

            """;
        var read = VoicebankText.ReadYaml(existing);
        Assert.Equal("old", read.Name);
        Assert.True(read.HasName);
        Assert.Equal([("en-US", "Old Name")], read.LocalizedNames);
        Assert.Equal(0.5, read.PortraitOpacity, 3);
        Assert.Equal(800, read.PortraitHeight);

        var update = new VoicebankYaml
        {
            TextFileEncoding = VoicebankText.ShiftJis,
            Name = "A \"音\"",
            Image = "icon.bmp",
            Author = "ゆずり",
            Voice = "ゆずり",
            Version = "1.5",
            Web = "https://example.com/a",
            Portrait = "images/portrait.png",
            PortraitOpacity = 0.67,
            PortraitHeight = 0,
        };
        update.LocalizedNames.Add(("ja-JP", "レンリ"));
        var written = VoicebankText.ApplyYaml(existing, update);
        Assert.Contains("# keep", written, StringComparison.Ordinal);
        Assert.Contains("suffix: CV", written, StringComparison.Ordinal);
        Assert.Contains("default_phonemizer: OpenUtau.Plugin.Builtin.JapaneseCVVCPhonemizer", written, StringComparison.Ordinal);
        Assert.Contains("singer_type: \"utau\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("enunu", written, StringComparison.Ordinal);
        var again = VoicebankText.ReadYaml(written);
        Assert.Equal("A \"音\"", again.Name);
        Assert.Equal("https://example.com/a", again.Web);
        Assert.Equal("ゆずり", again.Voice);
        Assert.Equal("1.5", again.Version);
        Assert.Equal("images/portrait.png", again.Portrait);
        Assert.Equal(0.67, again.PortraitOpacity, 3);
        Assert.Equal(0, again.PortraitHeight);
        Assert.Equal([("ja-JP", "レンリ")], again.LocalizedNames);
    }

    [Fact]
    public void ClearingPortraitDropsItsKeysAndEmptyVoice()
    {
        var written = VoicebankText.ApplyYaml(
            """
            voice: someone
            portrait: portrait.png
            portrait_opacity: 0.4
            portrait_height: 10
            subbanks:
            - suffix: CV

            """,
            new VoicebankYaml { Name = "音" });
        Assert.DoesNotContain("portrait", written, StringComparison.Ordinal);
        Assert.DoesNotContain("voice:", written, StringComparison.Ordinal);
        Assert.Contains("suffix: CV", written, StringComparison.Ordinal);
        Assert.Contains("name: \"音\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void IconIs100By100BmpWithLetterbox()
    {
        var fitted = VoicebankImage.FitIcon(Solid(10, 4, 0, 0, 255), 10, 4);
        Assert.Equal(100 * 100 * 4, fitted.Length);
        Assert.Equal((byte)255, At(fitted, 0, 0)[0]);
        Assert.Equal((byte)255, At(fitted, 0, 0)[1]);
        Assert.Equal((byte)255, At(fitted, 0, 0)[2]);
        var center = At(fitted, 50, 50);
        Assert.Equal((byte)0, center[0]);
        Assert.Equal((byte)0, center[1]);
        Assert.Equal((byte)255, center[2]);

        var clear = VoicebankImage.FitIcon(Solid(8, 8, 0, 0, 255, 0), 8, 8);
        Assert.Equal((byte)255, At(clear, 50, 50)[0]);
        Assert.Equal((byte)255, At(clear, 50, 50)[2]);

        var bmp = VoicebankImage.EncodeBmp24(fitted, 100, 100);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(100, BitConverter.ToInt32(bmp, 18));
        Assert.Equal(100, BitConverter.ToInt32(bmp, 22));
        Assert.Equal(24, BitConverter.ToInt16(bmp, 28));
        Assert.Equal(54 + 100 * 100 * 3, bmp.Length);
    }

    [Fact]
    public void PortraitPathStaysInsideTheVoicebank()
    {
        var folder = Path.Combine(Path.GetTempPath(), "otoasobi-bank-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Equal("portrait.png", VoicebankText.PortraitFileName(""));
            Assert.Equal("images/portrait.png", VoicebankText.PortraitFileName("images\\portrait.png"));
            Assert.Equal("images/hero.png", VoicebankText.PortraitFileName("images\\hero.gif"));
            Assert.True(VoicebankText.TryResolveInside(folder, "images/portrait.png", out var full));
            Assert.StartsWith(Path.GetFullPath(folder), full, StringComparison.OrdinalIgnoreCase);
            Assert.False(VoicebankText.TryResolveInside(folder, "../secret.png", out _));
            Assert.False(VoicebankText.TryResolveInside(folder, "C:/outside.png", out _));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void VotesComeFromCharacterPrefixAndOto()
    {
        var folder = Path.Combine(Path.GetTempPath(), "otoasobi-bank-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(folder);
        try
        {
            var sjis = VoicebankText.EncodeCharacter(new CharacterFile { Name = "あ" }, VoicebankText.ShiftJis);
            File.WriteAllBytes(Path.Combine(folder, "oto.ini"), sjis);
            File.WriteAllBytes(Path.Combine(folder, "character.txt"), Encoding.ASCII.GetBytes("name=a\r\n"));
            var votes = VoicebankText.CollectVotes(folder);
            Assert.Equal(VoicebankText.ShiftJis, VoicebankText.Resolve(votes, "あ"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    static byte[] Solid(int width, int height, byte b, byte g, byte r, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        return pixels;
    }

    static byte[] At(byte[] bgra, int x, int y)
    {
        var i = (y * 100 + x) * 4;
        return [bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]];
    }
}
