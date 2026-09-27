namespace GestorIA.Engine.Tests.Golden;

public class G20_RefundAboveTheBaseMaxima
{
    [Fact]
    public void TheClosedMonthsPaidAboveTheBaseMaximaCountOnlyWhatTgssKeeps() => SetAsideGolden.Passes("G20");
}
