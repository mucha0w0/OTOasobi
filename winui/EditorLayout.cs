using System.Text.Json.Serialization;

namespace Otoasobi;

public readonly struct PaneBox
{
    public PaneBox(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    public double Area => Math.Max(0, Width) * Math.Max(0, Height);
}

public enum DockEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

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

    public static readonly string[] PaneIds = ["file", "wave", "spec", "loud", "otoBand", "source", "oto", "otoParams", "otoIni", "frq", "bank"];

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

    public static LayoutNode Show(LayoutNode? root, string id, IReadOnlyDictionary<string, PaneBox> boxes)
    {
        if (!IsPane(id))
            return root ?? Leaf(id);
        if (Contains(root, id))
            return root!;
        var leaf = Leaf(id);
        if (root == null)
            return leaf;
        var target = Largest(root, boxes);
        var box = boxes.TryGetValue(target.Id!, out var known) ? known : new PaneBox(2, 1);
        var split = box.Width >= box.Height
            ? H(0.5, target, leaf)
            : V(0.5, target, leaf);
        return Replace(root, target, split);
    }

    public static LayoutNode? Hide(LayoutNode? root, string id)
    {
        if (root == null || !Contains(root, id))
            return root;
        return Remove(root, id);
    }

    // target は葉でも、葉をまとめた枝でもよい。ドラッグ中のパネルを外したあとに、残った範囲を割る。
    public static LayoutNode? Move(LayoutNode? root, string draggedId, LayoutNode target, DockEdge edge)
    {
        if (root == null || !Contains(root, draggedId))
            return root;
        var stay = new List<string>();
        Collect(target, stay);
        stay.Remove(draggedId);
        if (stay.Count == 0)
            return root;
        var dragged = Find(root, draggedId);
        if (dragged == null)
            return root;
        var without = Remove(root, draggedId);
        if (without == null)
            return dragged;
        var resolved = SmallestContaining(without, stay) ?? without;
        var split = edge switch
        {
            DockEdge.Left => H(0.5, dragged, resolved),
            DockEdge.Right => H(0.5, resolved, dragged),
            DockEdge.Top => V(0.5, dragged, resolved),
            _ => V(0.5, resolved, dragged),
        };
        return Replace(without, resolved, split);
    }

    static void Collect(LayoutNode node, List<string> ids)
    {
        if (node.IsLeaf)
        {
            if (node.Id != null)
                ids.Add(node.Id);
            return;
        }

        if (node.First != null)
            Collect(node.First, ids);
        if (node.Second != null)
            Collect(node.Second, ids);
    }

    static LayoutNode? SmallestContaining(LayoutNode? node, List<string> ids)
    {
        if (node == null || !ContainsAll(node, ids))
            return null;
        if (!node.IsLeaf)
        {
            if (node.First != null && ContainsAll(node.First, ids))
                return SmallestContaining(node.First, ids);
            if (node.Second != null && ContainsAll(node.Second, ids))
                return SmallestContaining(node.Second, ids);
        }

        return node;
    }

    static bool ContainsAll(LayoutNode node, List<string> ids)
    {
        foreach (var id in ids)
        {
            if (!Contains(node, id))
                return false;
        }

        return true;
    }

    public static LayoutNode? Find(LayoutNode? node, string id)
    {
        if (node == null)
            return null;
        if (node.IsLeaf)
            return node.Id == id ? node : null;
        return Find(node.First, id) ?? Find(node.Second, id);
    }

    public static bool IsValid(LayoutNode? root)
    {
        if (root == null)
            return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return ValidNode(root, seen, 0) && seen.Count > 0;
    }

    public static double ClampRatio(double ratio, double total)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio))
            ratio = 0.5;
        // 子を 0 にしない。160px へ引き上げる処理は、ドラッグの下限と FitDisplayedMinima が行う。
        if (total <= 2)
            return Math.Clamp(ratio, 0.08, 0.92);
        var edge = Math.Min(0.49, 1 / total);
        return Math.Clamp(ratio, edge, 1 - edge);
    }

    // 画面に出ていた下限。領域が 2 枚分より狭いときは 8%。広いときは 160px。
    public static double MinimumRatio(double ratio, double total)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio))
            ratio = 0.5;
        if (total <= MinPane * 2)
            return Math.Clamp(ratio, 0.08, 0.92);
        var min = MinPane / total;
        return Math.Clamp(ratio, min, 1 - min);
    }

    // 保存済みの比率を、いまの窓で下限まで広げた見た目に合わせる。ドラッグ中は呼ばない。
    public static void FitDisplayedMinima(LayoutNode node, double width, double height) =>
        FitMinima(node, width, height);

    // この比率のままだと、次のレイアウトで下限へ引き戻されて境目が動く。
    public static bool HoldsUndersizedPane(LayoutNode node, double width, double height) =>
        HoldsUndersized(node, width, height);

    // 各枝の境目の画面座標。軸に沿った位置だけを入れる。
    public static Dictionary<LayoutNode, double> MeasureSplitters(LayoutNode? root, double width, double height)
    {
        var found = new Dictionary<LayoutNode, double>();
        if (root == null || width < 1 || height < 1)
            return found;
        WalkSplitters(root, 0, 0, width, height, found);
        return found;
    }

    // 押し始めの座標にほかの境目を固定したまま、dragged だけを proposed へ進める。
    // 固定した境目が動く位置は採用しない。途中にその区間があっても飛び越さない。
    // 縮む面が下限を下回る位置も採用しない。領域が 320px を超えても、内側の境目は押し出さない。
    public static Dictionary<LayoutNode, PaneBox> MeasureFloors(LayoutNode root, double width, double height)
    {
        var floors = new Dictionary<LayoutNode, PaneBox>();
        if (width < 1 || height < 1)
            return floors;
        CollectFloors(root, width, height, width, height, floors);
        return floors;
    }

    public static bool MoveSplitter(
        LayoutNode root,
        LayoutNode dragged,
        double proposed,
        IReadOnlyDictionary<LayoutNode, double> frozen,
        double width,
        double height,
        IReadOnlyDictionary<LayoutNode, PaneBox>? floors = null)
    {
        if (width < 1 || height < 1 || double.IsNaN(proposed) || double.IsInfinity(proposed))
            return false;
        if (!frozen.ContainsKey(dragged))
            return false;
        if (!TrySplitterAt(root, dragged, width, height, out var current))
            return false;
        if (Math.Abs(proposed - current) < 0.5)
            return false;

        floors ??= MeasureFloors(root, width, height);
        var dir = Math.Sign(proposed - current);
        var accepted = current;
        var steps = (int)Math.Min(4000, Math.Ceiling(Math.Abs(proposed - current)));
        for (var i = 1; i <= steps; i++)
        {
            var next = i == steps ? proposed : current + dir * i;
            PlaceSplitter(root, dragged, next, frozen, width, height);
            if (!SplittersHeld(root, dragged, frozen, 0, 0, width, height)
                || !PanesAboveFloors(root, floors, width, height)
                || !TrySplitterAt(root, dragged, width, height, out var landed)
                || (landed - accepted) * dir < 0.25)
            {
                PlaceSplitter(root, dragged, accepted, frozen, width, height);
                break;
            }

            accepted = landed;
            if (Math.Abs(landed - proposed) < 0.5)
                break;
        }

        return Math.Abs(accepted - current) >= 0.5;
    }

    static void WalkSplitters(LayoutNode node, double x, double y, double width, double height, Dictionary<LayoutNode, double> found)
    {
        if (!TrySpan(node, width, height, out var horizontal, out _, out var size))
            return;
        found[node] = (horizontal ? x : y) + size;
        if (horizontal)
        {
            WalkSplitters(node.First!, x, y, size, height, found);
            WalkSplitters(node.Second!, x + size, y, Math.Max(0, width - size), height, found);
            return;
        }

        WalkSplitters(node.First!, x, y, width, size, found);
        WalkSplitters(node.Second!, x, y + size, width, Math.Max(0, height - size), found);
    }

    static bool TrySplitterAt(LayoutNode root, LayoutNode dragged, double width, double height, out double at)
    {
        at = 0;
        return TryFindSplitter(root, dragged, 0, 0, width, height, ref at);
    }

    static bool TryFindSplitter(LayoutNode node, LayoutNode dragged, double x, double y, double width, double height, ref double at)
    {
        if (!TrySpan(node, width, height, out var horizontal, out _, out var size))
            return false;
        if (ReferenceEquals(node, dragged))
        {
            at = (horizontal ? x : y) + size;
            return true;
        }

        if (horizontal)
        {
            return TryFindSplitter(node.First!, dragged, x, y, size, height, ref at)
                || TryFindSplitter(node.Second!, dragged, x + size, y, Math.Max(0, width - size), height, ref at);
        }

        return TryFindSplitter(node.First!, dragged, x, y, width, size, ref at)
            || TryFindSplitter(node.Second!, dragged, x, y + size, width, Math.Max(0, height - size), ref at);
    }

    static void PlaceSplitter(
        LayoutNode node,
        LayoutNode dragged,
        double dragAt,
        IReadOnlyDictionary<LayoutNode, double> frozen,
        double x,
        double y,
        double width,
        double height)
    {
        if (!TrySpan(node, width, height, out var horizontal, out var span, out _))
            return;
        var at = ReferenceEquals(node, dragged)
            ? dragAt
            : frozen.TryGetValue(node, out var fixedAt) ? fixedAt : (horizontal ? x : y) + span * node.Ratio;
        node.Ratio = ClampRatio((at - (horizontal ? x : y)) / span, span);
        var size = span * node.Ratio;
        if (horizontal)
        {
            PlaceSplitter(node.First!, dragged, dragAt, frozen, x, y, size, height);
            PlaceSplitter(node.Second!, dragged, dragAt, frozen, x + size, y, Math.Max(0, width - size), height);
            return;
        }

        PlaceSplitter(node.First!, dragged, dragAt, frozen, x, y, width, size);
        PlaceSplitter(node.Second!, dragged, dragAt, frozen, x, y + size, width, Math.Max(0, height - size));
    }

    static void PlaceSplitter(
        LayoutNode root,
        LayoutNode dragged,
        double dragAt,
        IReadOnlyDictionary<LayoutNode, double> frozen,
        double width,
        double height) =>
        PlaceSplitter(root, dragged, dragAt, frozen, 0, 0, width, height);

    static bool SplittersHeld(LayoutNode node, LayoutNode dragged, IReadOnlyDictionary<LayoutNode, double> frozen, double x, double y, double width, double height)
    {
        if (!TrySpan(node, width, height, out var horizontal, out _, out var size))
            return true;
        if (!ReferenceEquals(node, dragged))
        {
            var at = (horizontal ? x : y) + size;
            if (!frozen.TryGetValue(node, out var expected) || Math.Abs(at - expected) > 0.5)
                return false;
        }

        if (horizontal)
        {
            return SplittersHeld(node.First!, dragged, frozen, x, y, size, height)
                && SplittersHeld(node.Second!, dragged, frozen, x + size, y, Math.Max(0, width - size), height);
        }

        return SplittersHeld(node.First!, dragged, frozen, x, y, width, size)
            && SplittersHeld(node.Second!, dragged, frozen, x, y + size, width, Math.Max(0, height - size));
    }

    static void FitMinima(LayoutNode node, double width, double height)
    {
        if (node.IsLeaf || node.First == null || node.Second == null)
            return;
        var horizontal = node.Axis == "h";
        var span = horizontal ? width : height;
        if (span < 1)
            return;
        node.Ratio = MinimumRatio(node.Ratio, span);
        var size = span * node.Ratio;
        if (horizontal)
        {
            FitMinima(node.First, size, height);
            FitMinima(node.Second, Math.Max(0, width - size), height);
            return;
        }

        FitMinima(node.First, width, size);
        FitMinima(node.Second, width, Math.Max(0, height - size));
    }

    static bool HoldsUndersized(LayoutNode node, double width, double height)
    {
        if (node.IsLeaf || node.First == null || node.Second == null)
            return false;
        var horizontal = node.Axis == "h";
        var span = horizontal ? width : height;
        if (span < 1)
            return false;
        if (Math.Abs(MinimumRatio(node.Ratio, span) - node.Ratio) > 0.0001)
            return true;
        var size = span * node.Ratio;
        if (horizontal)
        {
            return HoldsUndersized(node.First, size, height)
                || HoldsUndersized(node.Second, Math.Max(0, width - size), height);
        }

        return HoldsUndersized(node.First, width, size)
            || HoldsUndersized(node.Second, width, Math.Max(0, height - size));
    }

    static void CollectFloors(
        LayoutNode node,
        double width,
        double height,
        double widthSpan,
        double heightSpan,
        Dictionary<LayoutNode, PaneBox> floors)
    {
        if (node.IsLeaf || node.First == null || node.Second == null)
        {
            if (node.IsLeaf)
                floors[node] = new PaneBox(ShrinkFloor(width, widthSpan), ShrinkFloor(height, heightSpan));
            return;
        }

        if (!TrySpan(node, width, height, out var horizontal, out var span, out var size))
            return;
        if (horizontal)
        {
            CollectFloors(node.First, size, height, span, heightSpan, floors);
            CollectFloors(node.Second, Math.Max(0, width - size), height, span, heightSpan, floors);
            return;
        }

        CollectFloors(node.First, width, size, widthSpan, span, floors);
        CollectFloors(node.Second, width, Math.Max(0, height - size), widthSpan, span, floors);
    }

    static bool PanesAboveFloors(
        LayoutNode node,
        IReadOnlyDictionary<LayoutNode, PaneBox> floors,
        double width,
        double height)
    {
        if (node.IsLeaf || node.First == null || node.Second == null)
        {
            if (!node.IsLeaf)
                return true;
            if (!floors.TryGetValue(node, out var floor))
                return false;
            return width + 0.5 >= floor.Width && height + 0.5 >= floor.Height;
        }

        if (!TrySpan(node, width, height, out var horizontal, out _, out var size))
            return false;
        if (horizontal)
        {
            return PanesAboveFloors(node.First, floors, size, height)
                && PanesAboveFloors(node.Second, floors, Math.Max(0, width - size), height);
        }

        return PanesAboveFloors(node.First, floors, width, size)
            && PanesAboveFloors(node.Second, floors, width, Math.Max(0, height - size));
    }

    // 160px 以上の面は 160px。もともと狭い面は、その分割で許されていた幅より縮めない。
    static double ShrinkFloor(double current, double axisSpan)
    {
        var limit = axisSpan <= MinPane * 2 ? Math.Max(1, axisSpan * 0.08) : MinPane;
        return Math.Min(Math.Max(0, current), limit);
    }

    static bool TrySpan(LayoutNode node, double width, double height, out bool horizontal, out double span, out double size)
    {
        horizontal = node.Axis == "h";
        span = 0;
        size = 0;
        if (node.IsLeaf || node.First == null || node.Second == null)
            return false;
        span = horizontal ? width : height;
        if (span < 1)
            return false;
        size = span * ClampRatio(node.Ratio, span);
        return true;
    }

    static bool ValidNode(LayoutNode node, HashSet<string> seen, int depth)
    {
        if (depth > 12 || seen.Count > 24)
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

    static LayoutNode Largest(LayoutNode root, IReadOnlyDictionary<string, PaneBox> boxes)
    {
        LayoutNode? best = null;
        var area = -1.0;
        void Walk(LayoutNode node)
        {
            if (node.IsLeaf)
            {
                var next = boxes.TryGetValue(node.Id!, out var box) ? box.Area : 0;
                if (best == null || next > area + 0.5)
                {
                    best = node;
                    area = next;
                }

                return;
            }

            if (node.First != null)
                Walk(node.First);
            if (node.Second != null)
                Walk(node.Second);
        }

        Walk(root);
        return best!;
    }

    static LayoutNode Replace(LayoutNode node, LayoutNode find, LayoutNode replacement)
    {
        if (ReferenceEquals(node, find))
            return replacement;
        if (node.IsLeaf)
            return node;
        node.First = node.First == null ? null : Replace(node.First, find, replacement);
        node.Second = node.Second == null ? null : Replace(node.Second, find, replacement);
        return node;
    }

    static LayoutNode? Remove(LayoutNode node, string id)
    {
        if (node.IsLeaf)
            return node.Id == id ? null : node;
        var first = node.First == null ? null : Remove(node.First, id);
        var second = node.Second == null ? null : Remove(node.Second, id);
        if (first == null)
            return second;
        if (second == null)
            return first;
        node.First = first;
        node.Second = second;
        return node;
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

        return false;
    }

    static LayoutNode Leaf(string id) => new() { Id = id };

    static LayoutNode H(double ratio, LayoutNode first, LayoutNode second) =>
        new() { Axis = "h", Ratio = ratio, First = first, Second = second };

    static LayoutNode V(double ratio, LayoutNode first, LayoutNode second) =>
        new() { Axis = "v", Ratio = ratio, First = first, Second = second };
}

static class EditorLayoutStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "editor-layout.json");

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
sealed class LayoutTemplate
{
    public const string BuiltInName = "初期配置";

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

    public bool Matches(LayoutNode? root, bool keepRatios) =>
        EditorLayout.Same(Root, KeepRatios, root, keepRatios);
}

static class LayoutTemplateStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Otoasobi",
        "layout-templates.json");

    public static IReadOnlyList<LayoutTemplate> Load() =>
        JsonFiles.ReadList<LayoutTemplate>(FilePath, Normalize);

    public static List<LayoutTemplate> Normalize(IEnumerable<LayoutTemplate>? list)
    {
        var seen = new HashSet<string>();
        var result = new List<LayoutTemplate>();
        if (list == null)
            return result;
        foreach (var template in list)
        {
            if (template == null)
                continue;
            var name = (template.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40 || name.Any(char.IsControl))
                continue;
            if (name == LayoutTemplate.BuiltInName || !seen.Add(name))
                continue;
            if (!EditorLayout.IsValid(template.Root))
                continue;
            result.Add(new LayoutTemplate
            {
                Name = name,
                BuiltIn = false,
                Root = EditorLayout.Clone(template.Root),
                KeepRatios = template.KeepRatios,
            });
        }

        return result;
    }

    public static void Save(IEnumerable<LayoutTemplate> templates) =>
        JsonFiles.Write(FilePath, templates.Where(template => !template.BuiltIn));
}
