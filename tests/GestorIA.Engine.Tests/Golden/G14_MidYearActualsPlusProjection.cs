namespace GestorIA.Engine.Tests.Golden;

public class G14_MidYearActualsPlusProjection
{
    [Fact]
    public void ActualsToQ1AndTheProjectionForTheRestGiveTheQ2Estimate() => SetAsideGolden.Passes("G14");
}
