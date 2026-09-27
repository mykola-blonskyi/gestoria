namespace GestorIA.Engine.Tests.Golden;

public class G20_RefundAboveTheBaseMaxima
{
    [Fact]
    public void AYearWhoseAverageBaseIsAboveTheBaseMaximaIsRefundedDownToIt() => SetAsideGolden.Passes("G20");
}
