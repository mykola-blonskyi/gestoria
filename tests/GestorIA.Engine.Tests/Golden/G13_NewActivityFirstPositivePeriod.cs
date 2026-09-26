namespace GestorIA.Engine.Tests.Golden;

public class G13_NewActivityFirstPositivePeriod
{
    [Fact]
    public void Modelo130PaysTwentyPercentOfTheUnreducedNetLessTheMinoracion() => Modelo130Golden.Passes("G13");

    [Fact]
    public void TheAnnualReturnTakesTwentyPercentOffTheFirstPositiveNet() => RentaTrueUpGolden.Passes("G13");
}
