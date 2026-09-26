namespace GestorIA.Engine.Tests.Golden;

public class G05_LossMakingQuarter
{
    [Fact]
    public void LossMakingQ3PaysZeroAndItsNegativeResultIsDeductedInQ4() => Modelo130Golden.Passes("G05");
}
