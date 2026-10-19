using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Otoasobi;

namespace OtoParity;

// tests/oto_parity.json は Python が計算したミリ秒。画面側の秒と突き合わせる。
public class ParityTests
{
    [Fact]
    public void MarksBlankAndTuneMatchPython()
    {
        var doc = JsonNode.Parse(File.ReadAllText(CasesPath()))!;
        var expected = doc["expected"]!;

        Assert.Equal(Strings(expected["mono"]!), Lines(doc["mono"]!, "mono"));
        Assert.Equal(Strings(expected["renzoku"]!), Lines(doc["renzoku"]!, "renzoku"));
        Assert.Equal(expected["blank"]!.GetValue<double>(), Blank(doc["blank"]!), precision: 6);

        var tunes = doc["tunes"]!.AsArray();
        var tuneExpected = expected["tunes"]!.AsArray();
        Assert.Equal(tuneExpected.Count, tunes.Count);
        for (var i = 0; i < tunes.Count; i++)
            Assert.Equal(tuneExpected[i]!.GetValue<string>(), Tune(tunes[i]!));
    }

    static List<string> Lines(JsonNode node, string mode)
    {
        var name = node["file"]!.GetValue<string>();
        var file = new WavFile
        {
            Name = name,
            Duration = node["duration"]!.GetValue<double>(),
        };
        file.Spans.AddRange(Spans(node["spans"]!));
        var settings = new OtoExport(
            node["offsetMs"]!.GetValue<double>(),
            node["consonantMs"]!.GetValue<double>(),
            node["blank"]!.GetValue<double>(),
            Overlap(node["overlap"]),
            mode,
            node["overlapAt"]?.GetValue<double>() ?? 0.5);
        var dest = new List<OtoMark>();
        OtoMarks.Fill(file, settings, dest, Loudness(node["loudness"]));
        return dest.Select(mark => Format(name, mark)).ToList();
    }

    static double Blank(JsonNode node)
    {
        var spans = Spans(node["spans"]!);
        return BlankRelease.At(
            Loudness(node["loudness"]),
            node["duration"]!.GetValue<double>(),
            spans,
            node["index"]!.GetValue<int>(),
            node["shift"]!.GetValue<double>());
    }

    static string Tune(JsonNode node)
    {
        var parsed = Parse(node["line"]!.GetValue<string>());
        var locked = new bool[OtoTune.PointCount];
        var points = node["points"]!.AsObject();
        double Pick(string key, double auto, int index)
        {
            if (points[key] is JsonNode value)
            {
                locked[index] = true;
                return value.GetValue<double>();
            }

            return auto;
        }

        var offset = parsed.Offset / 1000.0;
        var consonant = (parsed.Offset + parsed.Consonant) / 1000.0;
        var blank = (parsed.Offset - parsed.Blank) / 1000.0;
        var preutter = (parsed.Offset + parsed.Preutter) / 1000.0;
        var overlap = (parsed.Offset + parsed.Overlap) / 1000.0;
        var placed = OtoTune.Place(
            new OtoMark(parsed.Alias, offset, overlap, preutter, consonant, blank),
            Pick("offset", offset, 0),
            Pick("overlap", overlap, 1),
            Pick("preutter", preutter, 2),
            Pick("consonant", consonant, 3),
            Pick("blank", blank, 4),
            locked,
            node["duration"]!.GetValue<double>());
        return Format(parsed.File, placed);
    }

    static string Format(string file, OtoMark mark) =>
        $"{file}={mark.Alias},{Ms(mark.Offset * 1000.0)},{Ms((mark.Consonant - mark.Offset) * 1000.0)},{Ms((mark.Offset - mark.Blank) * 1000.0)},{Ms((mark.Preutter - mark.Offset) * 1000.0)},{Ms((mark.Overlap - mark.Offset) * 1000.0)}";

    static string Ms(double value)
    {
        var rounded = Math.Round(value, 3, MidpointRounding.ToEven);
        if (Math.Abs(rounded) < 0.0005)
            rounded = 0;
        return rounded.ToString("0.000", CultureInfo.InvariantCulture);
    }

    static (string File, string Alias, double Offset, double Consonant, double Blank, double Preutter, double Overlap) Parse(string line)
    {
        var eq = line.IndexOf('=');
        var body = line[(eq + 1)..];
        var comma = body.IndexOf(',');
        var parts = body[comma..].Split(',');
        return (
            line[..eq],
            body[..comma],
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture),
            double.Parse(parts[4], CultureInfo.InvariantCulture),
            double.Parse(parts[5], CultureInfo.InvariantCulture));
    }

    static List<PhoneSpan> Spans(JsonNode node)
    {
        var spans = new List<PhoneSpan>();
        foreach (var item in node.AsArray())
        {
            spans.Add(new PhoneSpan
            {
                Start = item!["start"]!.GetValue<double>(),
                End = item["end"]!.GetValue<double>(),
                Label = item["label"]!.GetValue<string>(),
                Token = item["token"]!.GetValue<string>(),
            });
        }

        return spans;
    }

    static Dictionary<string, double> Overlap(JsonNode? node)
    {
        var overlap = new Dictionary<string, double>();
        if (node is not JsonObject table)
            return overlap;
        foreach (var pair in table)
            overlap[pair.Key] = pair.Value!.GetValue<double>();
        return overlap;
    }

    static double[]? Loudness(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
            return null;
        return node.AsArray().Select(item => item!.GetValue<double>()).ToArray();
    }

    static List<string> Strings(JsonNode node) =>
        node.AsArray().Select(item => item!.GetValue<string>()).ToList();

    static string CasesPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var beside = Path.Combine(dir.FullName, "oto_parity.json");
            if (File.Exists(beside))
                return beside;
            var tests = Path.Combine(dir.FullName, "tests", "oto_parity.json");
            if (File.Exists(tests))
                return tests;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("oto_parity.json");
    }
}
