namespace GestorIA.Engine.Tests.Golden;

public class G21_PluriactividadInMadrid
{
    [Fact]
    public void TheMadridScaleAndMinimoLeaveASmallerGapThanValencia() => RentaTrueUpGolden.Passes("G21");
}
