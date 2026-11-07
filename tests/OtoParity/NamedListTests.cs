using Otoasobi;

namespace OtoParity;

public class NamedListTests
{
    sealed class Item
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    [Fact]
    public void CollectDropsEmptyLongReservedAndDuplicateNames()
    {
        var list = NamedList.Collect(
            new List<Item>
            {
                new() { Name = "  ", Value = 1 },
                new() { Name = new string('あ', 41), Value = 2 },
                new() { Name = "初期値", Value = 3 },
                new() { Name = "  残す  ", Value = 4 },
                new() { Name = "残す", Value = 5 },
                new() { Name = "制御\n文字", Value = 6 },
            },
            item => item.Name,
            name => name == "初期値",
            (item, name) =>
            {
                item.Name = name;
                return item;
            });

        Assert.Equal(2, list.Count);
        Assert.Equal("残す", list[0].Name);
        Assert.Equal(4, list[0].Value);
        Assert.Equal("制御\n文字", list[1].Name);
    }

    [Fact]
    public void CollectCanRejectControlCharactersAndNullItems()
    {
        var withNull = new List<Item> { null!, new() { Name = "制御\n文字" }, new() { Name = "残す" } };
        var list = NamedList.Collect(
            withNull,
            item => item.Name,
            reserved: null,
            (item, name) =>
            {
                item.Name = name;
                return item;
            },
            skipNull: true,
            rejectControl: true);

        Assert.Single(list);
        Assert.Equal("残す", list[0].Name);
    }

    [Fact]
    public void RejectedKeepStillConsumesTheName()
    {
        var list = NamedList.Collect(
            new List<Item>
            {
                new() { Name = "同じ", Value = 1 },
                new() { Name = "同じ", Value = 2 },
            },
            item => item.Name,
            reserved: null,
            (item, name) =>
            {
                item.Name = name;
                return item.Value == 1 ? null : item;
            });

        Assert.Empty(list);
    }

    [Fact]
    public void NullListIsEmpty()
    {
        var list = NamedList.Collect<Item>(null, item => item.Name, null, (item, _) => item);
        Assert.Empty(list);
    }

    [Fact]
    public void DataFileSitsUnderTheAppDataRoot()
    {
        Assert.Equal(
            Path.Combine(AppPaths.DataRoot, "presets.json"),
            NamedList.DataFile("presets.json"));
    }
}
