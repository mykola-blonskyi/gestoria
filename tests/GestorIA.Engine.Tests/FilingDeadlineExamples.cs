using System.Globalization;

namespace GestorIA.Engine.Tests;

// Tax year 2025, a taxpayer resident in the Comunitat Valenciana unless a test names Madrid. Holidays are the national,
// VC and MD días inhábiles of config/tax-years/2025.example.json.
public class FilingDeadlineExamples
{
    private static readonly TaxYearConfig Config = TaxYearConfigFiles.Year2025;

    // The renta window with its last day replaced by the one under test.
    private static DateOnly LastDay(int yearOffset, int month, int day, string region = "VC")
    {
        var renta = Config.Calendar.Renta! with { End = new CalendarDay(yearOffset, month, day) };

        return FilingDeadline.Renta(region, Config with { Calendar = Config.Calendar with { Renta = renta } }).Window.End;
    }

    [Theory]
    [InlineData(1, 6, 30, "2026-06-30")] // a Tuesday, the Renta 2025 deadline itself
    [InlineData(1, 6, 27, "2026-06-29")] // a Saturday
    [InlineData(0, 7, 20, "2025-07-21")] // a Sunday
    [InlineData(0, 12, 8, "2025-12-09")] // a Monday, Inmaculada Concepción, national
    [InlineData(0, 10, 9, "2025-10-10")] // a Thursday, Día de la Comunitat Valenciana, regional
    [InlineData(1, 4, 3, "2026-04-07")] // Good Friday, then Saturday, Sunday and Easter Monday, a VC holiday
    [InlineData(0, 4, 3, "2025-04-03")] // a Thursday: Good Friday is 3 April in 2026, not in 2025
    public void ALastDayThatIsNotAWorkingDayMovesToTheNextOne(int yearOffset, int month, int day, string expected)
    {
        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd", CultureInfo.InvariantCulture), LastDay(yearOffset, month, day));
    }

    // Each day is a holiday in one of the two regions only, so it moves for that region's taxpayer and stays for the other.
    [Theory]
    [InlineData(0, 4, 17, "2025-04-21", "2025-04-17")] // Jueves Santo in Madrid, then Good Friday, the weekend, and Easter Monday, a VC holiday only
    [InlineData(0, 5, 2, "2025-05-05", "2025-05-02")] // a Friday, Fiesta de la Comunidad de Madrid
    [InlineData(0, 7, 25, "2025-07-28", "2025-07-25")] // a Friday, Santiago Apóstol in Madrid
    [InlineData(1, 11, 2, "2026-11-03", "2026-11-02")] // a Monday, All Saints moved from Sunday 1 November in Madrid
    [InlineData(0, 10, 9, "2025-10-09", "2025-10-10")] // a Thursday, Día de la Comunitat Valenciana
    public void ARegionalHolidayMovesTheLastDayOnlyForThatRegion(int yearOffset, int month, int day, string madrid, string valencia)
    {
        Assert.Equal(DateOnly.ParseExact(madrid, "yyyy-MM-dd", CultureInfo.InvariantCulture), LastDay(yearOffset, month, day, "MD"));
        Assert.Equal(DateOnly.ParseExact(valencia, "yyyy-MM-dd", CultureInfo.InvariantCulture), LastDay(yearOffset, month, day, "VC"));
    }

    // 20 April 2025 is a Sunday. Easter Monday the 21st is a holiday in Valencia and a working day in Madrid.
    [Fact]
    public void TheQ1Modelo130DeadlineOf2025IsADayEarlierInMadridThanInValencia()
    {
        var (madrid, step) = FilingDeadline.Modelo130(Quarter.Q1, "MD", Config);
        var (valencia, _) = FilingDeadline.Modelo130(Quarter.Q1, "VC", Config);

        Assert.Equal(new DateOnly(2025, 4, 21), madrid.End);
        Assert.Equal(new DateOnly(2025, 4, 22), valencia.End);
        Assert.Equal("2025-04-20 Sunday → 2025-04-21", step.Formula);
        Assert.Contains("regions.MD.holidays", step.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegionTheConfigurationCannotServeFailsRatherThanFallingBackToNationalHolidays()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => FilingDeadline.Modelo130(
            Quarter.Q1, TaxYearConfigFiles.DeclaredIncompleteRegion, TaxYearConfigFiles.Year2025WithDeclaredIncompleteRegion));

        Assert.Contains("Region GA is declared incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModelo130DueDateStepListsEveryDaySkippedOnTheWayToAWorkingDay()
    {
        var (_, step) = FilingDeadline.Modelo130(Quarter.Q1, "VC", Config);

        Assert.Equal(new TraceValue.Date(new DateOnly(2025, 4, 22)), step.Output);
        Assert.Equal("2025-04-20 Sunday; 2025-04-21 Monday, holiday → 2025-04-22", step.Formula);
        Assert.Contains(FilingDeadline.Rule, step.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRentaDueDateStepNamesTheConfiguredDayWhenItIsAlreadyAWorkingDay()
    {
        var (window, step) = FilingDeadline.Renta("VC", Config);

        Assert.Equal(new DateOnly(2026, 6, 30), window.End);
        Assert.Equal(DayOfWeek.Tuesday, window.End.DayOfWeek);
        Assert.Equal("2026-06-30 Tuesday, a working day → 2026-06-30", step.Formula);
    }
}
