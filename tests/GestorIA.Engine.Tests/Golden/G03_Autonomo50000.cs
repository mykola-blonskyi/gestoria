namespace GestorIA.Engine.Tests.Golden;

public class G03_Autonomo50000
{
    [Fact]
    public void FourQuartersOver42400NetSumTo8480() => Modelo130Golden.Passes("G03");

    [Fact]
    public void CuotaIntegraOf9498_05LeavesARentaOf1018_05BeyondTheAdvances() => RentaTrueUpGolden.Passes("G03");
}
