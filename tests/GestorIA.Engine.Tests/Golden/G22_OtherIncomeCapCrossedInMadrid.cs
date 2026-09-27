namespace GestorIA.Engine.Tests.Golden;

public class G22_OtherIncomeCapCrossedInMadrid
{
    [Fact]
    public void TheQ1PaymentIsDueOnEasterMondayBecauseItIsNotAHolidayInMadrid() => SetAsideGolden.Passes("G22");
}
