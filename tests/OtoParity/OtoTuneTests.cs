using Otoasobi;

namespace OtoParity;

public class OtoTuneTests
{
    [Fact]
    public void LockedOffsetSurvivesNeighborRepair()
    {
        var auto = new OtoMark("あ", 0.1, 0.12, 0.2, 0.3, 0.8);
        double?[] points = [0.25, null, null, null, null];
        var shown = OtoTune.Apply(auto, points, 1);
        Assert.Equal(0.25, shown.Offset, 3);
        Assert.True(shown.Preutter + 1e-9 >= shown.Offset);
    }
}
