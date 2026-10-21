using System.Text.Json;
using Otoasobi;

namespace OtoParity;

public class LayoutTemplateTests
{
    [Fact]
    public void CloneDoesNotShareTheTree()
    {
        var original = EditorLayout.CreateDefault();
        var clone = EditorLayout.Clone(original);
        Assert.NotNull(clone);
        clone.Ratio = 0.9;
        Assert.Equal(0.182, original.Ratio, 5);
        Assert.False(EditorLayout.Same(original, false, clone, false));
    }

    [Fact]
    public void SameRequiresTheKeepRatiosFlag()
    {
        var root = EditorLayout.CreateDefault();
        Assert.True(EditorLayout.Same(root, false, EditorLayout.Clone(root), false));
        Assert.False(EditorLayout.Same(root, false, EditorLayout.Clone(root), true));
        Assert.False(EditorLayout.Same(null, false, root, false));
        Assert.True(EditorLayout.Same(null, true, null, true));
    }

    [Fact]
    public void NormalizeKeepsNamedArrangementsAndDropsTheRest()
    {
        var wave = new LayoutNode { Id = "wave" };
        var hidden = new LayoutNode { Id = "source" };
        var list = LayoutTemplateStore.Normalize(
        [
            new LayoutTemplate { Name = "初期配置", Root = wave },
            new LayoutTemplate { Name = "壊れた", Root = new LayoutNode { Id = "nope" } },
            new LayoutTemplate { Name = "  ", Root = wave },
            new LayoutTemplate { Name = " 空 ", Root = null, KeepRatios = true },
            new LayoutTemplate { Name = "空", Root = hidden },
            new LayoutTemplate { Name = "比率", Root = new LayoutNode { Axis = "h", Ratio = 0, First = wave, Second = hidden } },
            new LayoutTemplate { Name = "波形\nだけ", Root = wave },
            new LayoutTemplate { Name = " 波形だけ ", Root = wave, KeepRatios = true },
        ]);

        Assert.Equal(2, list.Count);
        Assert.Equal("空", list[0].Name);
        Assert.Null(list[0].Root);
        Assert.True(list[0].KeepRatios);
        Assert.False(list[0].BuiltIn);
        Assert.Equal("波形だけ", list[1].Name);
        Assert.True(list[1].Matches(wave, true));
        list[1].Root!.Id = "oto";
        Assert.Equal("wave", wave.Id);
    }

    [Fact]
    public void BuiltInMatchesTheDefaultArrangement()
    {
        var builtIn = LayoutTemplate.CreateBuiltIn();
        Assert.True(builtIn.Matches(EditorLayout.CreateDefault(), false));
        Assert.False(builtIn.Matches(new LayoutNode { Id = "frq" }, false));
    }

    [Fact]
    public void NormalizeRoundTripsThroughJson()
    {
        var stored = LayoutTemplateStore.Normalize(
        [
            new LayoutTemplate
            {
                Name = "原音",
                KeepRatios = true,
                Root = new LayoutNode
                {
                    Axis = "h",
                    Ratio = 0.4,
                    First = new LayoutNode { Id = "file" },
                    Second = new LayoutNode { Id = "oto" },
                },
            },
        ]);
        var json = JsonSerializer.Serialize(stored, JsonFiles.Options);
        var read = JsonSerializer.Deserialize<List<LayoutTemplate>>(json, JsonFiles.Options);
        var again = LayoutTemplateStore.Normalize(read);
        Assert.Single(again);
        Assert.True(again[0].Matches(stored[0].Root, true));
        Assert.False(again[0].BuiltIn);
    }
}
