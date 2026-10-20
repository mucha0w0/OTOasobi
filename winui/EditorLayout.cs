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
}

static class EditorLayout
{
    public const double MinPane = 160;

    public static readonly string[] PaneIds = ["file", "wave", "spec", "loud", "otoBand", "source", "oto"];

    // 1540px 幅でファイル約 280、oto調整約 320。グラフの高さ比は、以前の 1 枚キャンバスの帯に合わせる。
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
                Leaf("oto")));

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
        if (total <= MinPane * 2)
            return Math.Clamp(ratio, 0.08, 0.92);
        var min = MinPane / total;
        return Math.Clamp(ratio, min, 1 - min);
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

    public static bool TryLoad(out LayoutNode? root)
    {
        root = null;
        if (!File.Exists(FilePath))
            return false;
        var file = JsonFiles.Read<LayoutFile>(FilePath);
        if (file == null || !EditorLayout.IsValid(file.Root))
            return false;
        root = file.Root;
        return true;
    }

    public static void Save(LayoutNode? root)
    {
        try
        {
            JsonFiles.Write(FilePath, new LayoutFile { Root = root });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
