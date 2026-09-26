namespace GestorIA.Engine.Tests.Golden;

public class G12_FirstMonthProjectionOnly
{
    [Fact]
    public void JanuaryProjectionAloneGivesTheFirstSetAsideEstimate() => SetAsideGolden.Passes("G12");
}
