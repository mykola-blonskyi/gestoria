namespace GestorIA.Engine.Tests.Golden;

public class G11_Pluriactividad40000Plus30000
{
    [Fact]
    public void FirstYearModelo130PaysTwentyPercentLessTheMinoracion() => Modelo130Golden.Passes("G11");

    [Fact]
    public void StackedOnTheSalaryTheActivityLeavesAGapPayableInJune() => RentaTrueUpGolden.Passes("G11");
}
