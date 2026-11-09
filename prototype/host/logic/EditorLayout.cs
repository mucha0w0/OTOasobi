using System.Text.Json.Serialization;

namespace Otoasobi;

// 葉は Id、枝は Axis（h = 左右、v = 上下）と Ratio（最初の子の割合）。
public sealed class LayoutNode
{
    public string? Id { get; set; }

    public string? Axis { get; set; }

    public double Ratio { get; set; } = 0.5;

    public LayoutNode? First { get; set; }

    public LayoutNode? Second { get; set; }

    [JsonIgnore]
    public bool IsLeaf => !string.IsNullOrEmpty(Id);
}

public sealed class LayoutFile
{
    public LayoutNode? Root { get; set; }

    // true のときは、160px 未満の面を残した比率をレイアウトのたびに広げない。
    public bool KeepRatios { get; set; }
}

static class EditorLayout
{
    public const double MinPane = 160;

    public static readonly string[] PaneIds = ["file", "aliasList", "wave", "spec", "loud", "otoBand", "source", "oto", "otoParams", "otoIni", "frq", "frqEdit", "bank", "analyze", "recList", "recScript", "rec", "guide", "mic"];

    // 1540px 幅でファイル約 280、oto推定設定約 320。グラフの高さ比は、以前の 1 枚キャンバスの帯に合わせる。
    public static LayoutNode CreateDefault() =>
        H(0.182,
            Leaf("file"),
            H(0.746,
                V(0.30,
                    Leaf("wave"),
                    V(0.457,
                        Leaf("spec"),
                        V(0.368,
                            Leaf("loud"),
                            Leaf("otoBand")))),
                V(0.78,
                    Leaf("oto"),
                    Leaf("otoIni"))));

    // 録音の作業配置。初期配置には入れない。左がリスト。中央上が読み上げと録音コントローラ、右がガイド。下にマイク、ファイル、音素推定。
    public static LayoutNode CreateRecording() =>
        H(0.22,
            Leaf("recList"),
            V(0.52,
                H(0.58,
                    V(0.58,
                        Leaf("recScript"),
                        Leaf("rec")),
                    Leaf("guide")),
                V(0.50,
                    Leaf("mic"),
                    H(0.36,
                        Leaf("file"),
                        Leaf("wave")))));

    public static LayoutNode? Clone(LayoutNode? node)
    {
        if (node == null)
            return null;
        if (node.IsLeaf)
            return new LayoutNode { Id = node.Id };
        return new LayoutNode
        {
            Axis = node.Axis,
            Ratio = node.Ratio,
            First = Clone(node.First),
            Second = Clone(node.Second),
        };
    }

    public static bool Same(LayoutNode? left, bool leftKeepRatios, LayoutNode? right, bool rightKeepRatios)
    {
        if (leftKeepRatios != rightKeepRatios)
            return false;
        return SameTree(left, right);
    }

    static bool SameTree(LayoutNode? left, LayoutNode? right)
    {
        if (left == null || right == null)
            return left == null && right == null;
        if (left.IsLeaf || right.IsLeaf)
            return left.IsLeaf && right.IsLeaf && left.Id == right.Id;
        if (left.Axis != right.Axis || Math.Abs(left.Ratio - right.Ratio) > 0.0001)
            return false;
        return SameTree(left.First, right.First) && SameTree(left.Second, right.Second);
    }

    public static bool Contains(LayoutNode? node, string id)
    {
        if (node == null)
            return false;
        if (node.IsLeaf)
            return node.Id == id;
        return Contains(node.First, id) || Contains(node.Second, id);
    }

    public static bool IsValid(LayoutNode? root)
    {
        if (root == null)
            return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return ValidNode(root, seen, 0) && seen.Count > 0;
    }

    static bool ValidNode(LayoutNode node, HashSet<string> seen, int depth)
    {
        // 本体の 20 枚に、外部パネルを足しても保存できる。
        if (depth > 12 || seen.Count > 48)
            return false;
        if (node.IsLeaf)
        {
            if (node.First != null || node.Second != null || node.Axis != null)
                return false;
            return IsPane(node.Id!) && seen.Add(node.Id!);
        }

        if (node.Id != null || node.First == null || node.Second == null)
            return false;
        if (node.Axis is not ("h" or "v"))
            return false;
        if (double.IsNaN(node.Ratio) || node.Ratio <= 0 || node.Ratio >= 1)
            return false;
        return ValidNode(node.First, seen, depth + 1) && ValidNode(node.Second, seen, depth + 1);
    }

    static bool IsPane(string? id)
    {
        if (id == null)
            return false;
        foreach (var pane in PaneIds)
        {
            if (pane == id)
                return true;
        }

        return id.StartsWith("plugin.", StringComparison.Ordinal) && PluginPath.IsId(id["plugin.".Length..]);
    }

    static LayoutNode Leaf(string id) => new() { Id = id };

    static LayoutNode H(double ratio, LayoutNode first, LayoutNode second) =>
        new() { Axis = "h", Ratio = ratio, First = first, Second = second };

    static LayoutNode V(double ratio, LayoutNode first, LayoutNode second) =>
        new() { Axis = "v", Ratio = ratio, First = first, Second = second };
}

static class EditorLayoutStore
{
    public static string FilePath { get; } = NamedList.DataFile("editor-layout.json");

    public static bool TryLoad(out LayoutNode? root, out bool keepRatios)
    {
        root = null;
        keepRatios = false;
        if (!File.Exists(FilePath))
            return false;
        var file = JsonFiles.Read<LayoutFile>(FilePath);
        if (file == null || !EditorLayout.IsValid(file.Root))
            return false;
        root = file.Root;
        keepRatios = file.KeepRatios;
        return true;
    }

    public static void Save(LayoutNode? root, bool keepRatios)
    {
        try
        {
            JsonFiles.Write(FilePath, new LayoutFile { Root = root, KeepRatios = keepRatios });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

// 表示中のパネルと分割位置。初期配置は常にあり、それ以外は LocalAppData に残す。
sealed class LayoutTemplate : INamedPreset
{
    public const string BuiltInName = "初期配置";
    public const string RecordingName = "録音";

    public string Name { get; set; } = "";

    public LayoutNode? Root { get; set; }

    public bool KeepRatios { get; set; }

    [JsonIgnore]
    public bool BuiltIn { get; set; }

    public static LayoutTemplate CreateBuiltIn() =>
        new()
        {
            Name = BuiltInName,
            BuiltIn = true,
            Root = EditorLayout.CreateDefault(),
            KeepRatios = false,
        };

    public static LayoutTemplate CreateRecording() =>
        new()
        {
            Name = RecordingName,
            BuiltIn = true,
            Root = EditorLayout.CreateRecording(),
            KeepRatios = false,
        };

    public bool Matches(LayoutNode? root, bool keepRatios) =>
        EditorLayout.Same(Root, KeepRatios, root, keepRatios);
}

static class LayoutTemplateStore
{
    public static string FilePath { get; } = NamedList.DataFile("layout-templates.json");

    public static IReadOnlyList<LayoutTemplate> Load() =>
        JsonFiles.ReadList<LayoutTemplate>(FilePath, Normalize);

    public static List<LayoutTemplate> Normalize(IEnumerable<LayoutTemplate>? list) =>
        NamedList.Collect(
            list,
            template => template.Name ?? "",
            name => name is LayoutTemplate.BuiltInName or LayoutTemplate.RecordingName,
            (template, name) =>
            {
                if (!EditorLayout.IsValid(template.Root))
                    return null;
                return new LayoutTemplate
                {
                    Name = name,
                    BuiltIn = false,
                    Root = EditorLayout.Clone(template.Root),
                    KeepRatios = template.KeepRatios,
                };
            },
            skipNull: true,
            rejectControl: true);

    public static void Save(IEnumerable<LayoutTemplate> templates) =>
        JsonFiles.Write(FilePath, templates.Where(template => !template.BuiltIn));
}
