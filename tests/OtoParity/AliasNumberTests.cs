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
}
