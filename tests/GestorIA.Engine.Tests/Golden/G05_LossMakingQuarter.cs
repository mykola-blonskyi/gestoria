namespace GestorIA.Engine.Tests.Golden;

public class G05_LossMakingQuarter
{
    [Fact]
    public void LossMakingQ3PaysZeroAndQ4CatchesUpThroughTheCumulativeBase() => Modelo130Golden.Passes("G05");
}
