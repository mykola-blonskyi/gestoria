namespace GestorIA.Engine.Tests.Golden;

public class G18_TarifaPlanaLapsed
{
    [Fact]
    public void TheQuarterAfterTarifaPlanaLapsesReservesTheTramoCuota() => SetAsideGolden.Passes("G18");
}
