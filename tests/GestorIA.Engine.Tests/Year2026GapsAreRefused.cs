using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

// config/tax-years/2026.json declares what no norm had published by 2026-09-27 (#47). Each test fails once its gap closes,
// and the file's _todo note has to go with it (TaxYearGapsAreRegistered).
public class Year2026GapsAreRefused
{
    private static TaxYearConfig Config => TaxYearConfigFiles.Year2026;

    private static MonthlyCuotaResult Cuota(DateOnly alta, YearMonth month) =>
        MonthlyCuotaCalculator.Cuota(
            new MonthlyCuotaInput(alta, 30000m, month), Config.SeguridadSocial.Tramos, Config.SeguridadSocial.TarifaPlana);

    [Fact]
    public void AMonthUnderTarifaPlanaIsRefusedQuotingTheNote()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => Cuota(new DateOnly(2026, 3, 10), new YearMonth(2026, 5)));

        Assert.Contains("seguridadSocial.tarifaPlana.amount", error.Message, StringComparison.Ordinal);
        Assert.Contains(Config.SeguridadSocial.TarifaPlana.DeclaredIncomplete!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMonthAfterTarifaPlanaLapsedIsTheTramoCuota()
    {
        var result = Cuota(new DateOnly(2024, 6, 10), new YearMonth(2026, 2));

        Assert.Equal(427.21m, result.Cuota);
    }

    [Theory]
    [InlineData(Quarter.Q1, 2026, 4, 20)]
    [InlineData(Quarter.Q2, 2026, 7, 20)]
    [InlineData(Quarter.Q3, 2026, 10, 20)]
    public void Modelo130DeadlinesInsideTheYearCompute(Quarter quarter, int year, int month, int day)
    {
        var (window, _) = FilingDeadline.Modelo130(quarter, "VC", Config);

        Assert.Equal(new DateOnly(year, month, day), window.End);
    }

    [Fact]
    public void TheQ4Modelo130DeadlineIsRefusedQuotingTheNote()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => FilingDeadline.Modelo130(Quarter.Q4, "VC", Config));

        Assert.Contains("calendar.modelo130 Q4", error.Message, StringComparison.Ordinal);
        Assert.Contains(Config.Calendar.DeclaredIncomplete!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRentaDeadlineIsRefusedQuotingTheNote()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => FilingDeadline.Renta("VC", Config));

        Assert.Contains("calendar.renta", error.Message, StringComparison.Ordinal);
        Assert.Contains(Config.Calendar.DeclaredIncomplete!, error.Message, StringComparison.Ordinal);
    }
}
