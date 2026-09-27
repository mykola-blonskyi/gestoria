using System.Globalization;

namespace GestorIA.Engine.Tests;

// Tax year 2025, a taxpayer resident in the Comunitat Valenciana. Holidays are the national and VC días inhábiles of
// config/tax-years/2025.example.json.
public class FilingDeadlineExamples
{
    private static readonly TaxYearConfig Config = TaxYearConfigFiles.Year2025;

    // The renta window with its last day replaced by the one under test.
    private static DateOnly LastDay(int yearOffset, int month, int day)
    {
        var renta = Config.Calendar.Renta! with { End = new CalendarDay(yearOffset, month, day) };

        return FilingDeadline.Renta("VC", Config with { Calendar = Config.Calendar with { Renta = renta } }).Window.End;
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

    [Fact]
    public void ARegionTheConfigurationCannotServeFailsRatherThanFallingBackToNationalHolidays()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => FilingDeadline.Modelo130(Quarter.Q1, "MD", Config));

        Assert.Contains("Region MD is declared incomplete", error.Message, StringComparison.Ordinal);
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
