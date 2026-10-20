using Otoasobi;

namespace OtoParity;

public class SplitterDragTests
{
    const double Width = 1000;
    const double Height = 800;

    [Fact]
    public void MovingOneBoundaryLeavesTheOthersWhereTheyWere()
    {
        var inner = V(0.5, Leaf("a"), Leaf("b"));
        var mid = V(400.0 / 600.0, inner, Leaf("c"));
        var outer = V(0.75, mid, Leaf("d"));
        var frozen = EditorLayout.MeasureSplitters(outer, Width, Height);
        Assert.Equal(200, frozen[inner], 0.01);
        Assert.Equal(400, frozen[mid], 0.01);
        Assert.Equal(600, frozen[outer], 0.01);

        Move(outer, mid, 420, frozen);

        var now = EditorLayout.MeasureSplitters(outer, Width, Height);
        Assert.Equal(420, now[mid], 0.6);
        Assert.Equal(200, now[inner], 0.6);
        Assert.Equal(600, now[outer], 0.6);
    }

    [Fact]
    public void DragStopsBeforeAnotherBoundaryWouldHaveToMove()
    {
        var inner = V(0.5, Leaf("a"), Leaf("b"));
        var mid = V(400.0 / 600.0, inner, Leaf("c"));
        var outer = V(0.75, mid, Leaf("d"));
        var frozen = EditorLayout.MeasureSplitters(outer, Width, Height);

        Move(outer, mid, 100, frozen);
        var upward = EditorLayout.MeasureSplitters(outer, Width, Height);
        Assert.Equal(360, upward[mid], 1);
        Assert.Equal(200, upward[inner], 0.6);
        Assert.Equal(600, upward[outer], 0.6);

        Move(outer, mid, 900, frozen);
        var downward = EditorLayout.MeasureSplitters(outer, Width, Height);
        Assert.Equal(440, downward[mid], 1);
        Assert.Equal(200, downward[inner], 0.6);
        Assert.Equal(600, downward[outer], 0.6);
    }

    [Fact]
    public void DragDoesNotJumpAcrossAGapWhereAnotherBoundaryWouldMove()
    {
        var inner = V(0.5, Leaf("a"), Leaf("b"));
        var mid = V(400.0 / 600.0, inner, Leaf("c"));
        var outer = V(0.75, mid, Leaf("d"));
        var frozen = EditorLayout.MeasureSplitters(outer, Width, Height);

        Move(outer, mid, 250, frozen);

        var now = EditorLayout.MeasureSplitters(outer, Width, Height);
        Assert.Equal(360, now[mid], 1);
        Assert.Equal(200, now[inner], 0.6);
    }

    [Fact]
    public void NestedBoundaryBelowStaysPutWhenTheDividerAboveMoves()
    {
        var bottom = V(0.5, Leaf("c"), Leaf("d"));
        var mid = V(0.5, Leaf("b"), bottom);
        var top = V(0.25, Leaf("a"), mid);
        var frozen = EditorLayout.MeasureSplitters(top, Width, Height);
        Assert.Equal(200, frozen[top], 0.01);
        Assert.Equal(500, frozen[mid], 0.01);
        Assert.Equal(650, frozen[bottom], 0.01);

        Move(top, mid, 530, frozen);
        var near = EditorLayout.MeasureSplitters(top, Width, Height);
        Assert.Equal(530, near[mid], 0.6);
        Assert.Equal(200, near[top], 0.6);
        Assert.Equal(650, near[bottom], 0.6);

        Move(top, mid, 100, frozen);
        var far = EditorLayout.MeasureSplitters(top, Width, Height);
        Assert.Equal(360, far[mid], 1);
        Assert.Equal(200, far[top], 0.6);
        Assert.Equal(650, far[bottom], 0.6);
    }

    [Fact]
    public void HorizontalDragKeepsVerticalBoundariesAndNestedColumns()
    {
        var left = V(0.25, Leaf("a"), Leaf("b"));
        var right = H(0.5, Leaf("c"), Leaf("d"));
        var root = H(0.5, left, right);
        var frozen = EditorLayout.MeasureSplitters(root, Width, Height);
        Assert.Equal(500, frozen[root], 0.01);
        Assert.Equal(200, frozen[left], 0.01);
        Assert.Equal(750, frozen[right], 0.01);

        Move(root, left, 280, frozen);
        var vertical = EditorLayout.MeasureSplitters(root, Width, Height);
        Assert.Equal(280, vertical[left], 0.6);
        Assert.Equal(500, vertical[root], 0.6);
        Assert.Equal(750, vertical[right], 0.6);

        var again = EditorLayout.MeasureSplitters(root, Width, Height);
        Move(root, root, 700, again);
        var horizontal = EditorLayout.MeasureSplitters(root, Width, Height);
        Assert.Equal(590, horizontal[root], 1);
        Assert.Equal(280, horizontal[left], 0.6);
        Assert.Equal(750, horizontal[right], 0.6);
    }

    [Fact]
    public void SuccessiveMovesKeepUsingThePositionsFromThePress()
    {
        var inner = V(0.5, Leaf("a"), Leaf("b"));
        var mid = V(400.0 / 600.0, inner, Leaf("c"));
        var outer = V(0.75, mid, Leaf("d"));
        var frozen = EditorLayout.MeasureSplitters(outer, Width, Height);

        Move(outer, mid, 410, frozen);
        Move(outer, mid, 430, frozen);
        Move(outer, mid, 200, frozen);

        var now = EditorLayout.MeasureSplitters(outer, Width, Height);
        Assert.Equal(360, now[mid], 1);
        Assert.Equal(200, now[inner], 0.6);
        Assert.Equal(600, now[outer], 0.6);
    }

    [Fact]
    public void DefaultGraphStackKeepsEveryBoundaryExceptTheOneBeingDragged()
    {
        var loud = V(0.368, Leaf("loud"), Leaf("band"));
        var spec = V(0.457, Leaf("spec"), loud);
        var wave = V(0.30, Leaf("wave"), spec);
        var graphs = H(0.746, wave, Leaf("oto"));
        var root = H(0.182, Leaf("file"), graphs);
        const double width = 1400;
        const double height = 720;
        var frozen = EditorLayout.MeasureSplitters(root, width, height);
        var floors = EditorLayout.MeasureFloors(root, width, height);

        EditorLayout.MoveSplitter(root, spec, frozen[spec] + 36, frozen, width, height, floors);
        var nudged = EditorLayout.MeasureSplitters(root, width, height);
        Assert.Equal(frozen[spec] + 36, nudged[spec], 0.6);
        AssertSameExcept(frozen, nudged, spec);

        EditorLayout.MoveSplitter(root, spec, 0, frozen, width, height, floors);
        var up = EditorLayout.MeasureSplitters(root, width, height);
        AssertSameExcept(frozen, up, spec);
        Assert.True(up[spec] < frozen[spec] - 1);

        EditorLayout.MoveSplitter(root, spec, height, frozen, width, height, floors);
        var down = EditorLayout.MeasureSplitters(root, width, height);
        AssertSameExcept(frozen, down, spec);
        Assert.True(down[spec] > frozen[spec] + 1);
    }

    [Fact]
    public void GrowingAShortBandDoesNotStopBeforeTheFarPaneHitsTheMinimum()
    {
        var inner = V(0.25, Leaf("loud"), Leaf("spec"));
        var mid = V(200.0 / 740.0, inner, Leaf("band"));
        var root = V(160.0 / 900.0, Leaf("wave"), mid);
        const double width = 1000;
        const double height = 900;
        var frozen = EditorLayout.MeasureSplitters(root, width, height);
        Assert.Equal(160, frozen[root], 0.1);
        Assert.Equal(360, frozen[mid], 0.1);
        Assert.Equal(210, frozen[inner], 0.1);

        EditorLayout.MoveSplitter(root, mid, height, frozen, width, height);

        var now = EditorLayout.MeasureSplitters(root, width, height);
        Assert.Equal(height - EditorLayout.MinPane, now[mid], 1);
        Assert.Equal(210, now[inner], 0.6);
        Assert.Equal(160, now[root], 0.6);
    }

    [Fact]
    public void FitDisplayedMinimaLiftsAShortPaneWhenTheColumnIsTall()
    {
        var root = V(0.12, Leaf("wave"), Leaf("rest"));
        EditorLayout.FitDisplayedMinima(root, 800, 900);
        var at = EditorLayout.MeasureSplitters(root, 800, 900);
        Assert.Equal(160, at[root], 0.1);
    }

    static void AssertSameExcept(Dictionary<LayoutNode, double> before, Dictionary<LayoutNode, double> after, LayoutNode dragged)
    {
        Assert.Equal(before.Count, after.Count);
        foreach (var pair in before)
        {
            if (ReferenceEquals(pair.Key, dragged))
                continue;
            Assert.Equal(pair.Value, after[pair.Key], 0.6);
        }
    }

    static void Move(LayoutNode root, LayoutNode dragged, double proposed, IReadOnlyDictionary<LayoutNode, double> frozen) =>
        EditorLayout.MoveSplitter(root, dragged, proposed, frozen, Width, Height);

    static LayoutNode Leaf(string id) => new() { Id = id };

    static LayoutNode H(double ratio, LayoutNode first, LayoutNode second) =>
        new() { Axis = "h", Ratio = ratio, First = first, Second = second };

    static LayoutNode V(double ratio, LayoutNode first, LayoutNode second) =>
        new() { Axis = "v", Ratio = ratio, First = first, Second = second };
}
