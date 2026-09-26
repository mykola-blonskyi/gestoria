namespace GestorIA.Engine.Tests.Golden;

public class G08_SalaryPlusActivityOverTheCap
{
    [Fact]
    public void ActivityNetAboveTheCapLosesTheWholeReduccionPorTrabajo() => RentaTrueUpGolden.Passes("G08");
}
