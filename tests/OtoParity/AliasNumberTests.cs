using Otoasobi;

namespace OtoParity;

public class AliasNumberTests
{
    [Fact]
    public void SecondAndLaterCopiesGainANumber()
    {
        Assert.Equal(["あ", "あ2", "あ3"], OtoIni.NumberDuplicateAliases(["あ", "あ", "あ"]));
    }

    [Fact]
    public void ANumberAlreadyUsedByAnotherAliasIsSkipped()
    {
        Assert.Equal(["あ", "あ2", "あ3"], OtoIni.NumberDuplicateAliases(["あ", "あ2", "あ"]));
        Assert.Equal(["あ", "か", "あ2", "か2"], OtoIni.NumberDuplicateAliases(["あ", "か", "あ", "か"]));
    }

    [Fact]
    public void ANumberAlreadyAssignedIsSkipped()
    {
        var many = new string[12];
        Array.Fill(many, "a");
        var after = OtoIni.NumberDuplicateAliases([.. many, "a1", "a1"]);
        Assert.Equal("a12", after[11]);
        Assert.Equal("a1", after[12]);
        Assert.Equal("a13", after[13]);
    }

    [Fact]
    public void APinnedAliasStaysWhileLaterCopiesAreNumbered()
    {
        var pinned = new[] { false, true, false };
        Assert.Equal(["あ", "あ", "あ2"], OtoIni.NumberDuplicateAliases(["あ", "あ", "あ"], pinned));
    }

    [Fact]
    public void ResolveKeepsAnOverrideAndNumbersTheRest()
    {
        var shown = OtoIni.ResolveAliases(["あ", "あ", "あ"], [null, "か", null]);
        Assert.Equal(["あ", "か", "あ2"], shown);
        Assert.Equal([false, false, false], OtoIni.DuplicateAliasFlags(shown));
    }

    [Fact]
    public void ResolveWarnsWhenAnOverrideRepeatsAName()
    {
        var shown = OtoIni.ResolveAliases(["あ", "あ"], [null, "あ"]);
        Assert.Equal(["あ", "あ"], shown);
        Assert.Equal([true, true], OtoIni.DuplicateAliasFlags(shown));
    }

    [Fact]
    public void CleanAliasDropsTheEditorMark()
    {
        Assert.Equal("あ", OtoIni.CleanAlias("あ *"));
        Assert.Equal("あ", OtoIni.CleanAlias("あ*"));
        Assert.Null(OtoIni.CleanAlias(" *"));
        Assert.Null(OtoIni.CleanAlias("あ,い"));
    }
}
