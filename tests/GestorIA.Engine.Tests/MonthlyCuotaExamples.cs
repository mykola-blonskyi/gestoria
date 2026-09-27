using System.Text.Json;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class MonthlyCuotaExamples
{
    private static SeguridadSocialConfig SeguridadSocial2025 => TaxYearConfigFiles.Year2025.SeguridadSocial;

    private static TramoTable Tramos2025 => SeguridadSocial2025.Tramos;

    private static readonly DateOnly Alta = new(2027, 1, 15);

    // 32,000 × (1 − 0.07) / 12 = 2,480 a month, General 7.
    private static MonthlyCuotaResult Run(YearMonth month, decimal annualComputable = 32000m, DateOnly? alta = null) =>
        MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta ?? Alta, annualComputable, month), SeguridadSocial2025);

    [Fact]
    public void MonthOfAlta_IsProratedByDaysOverThirty()
    {
        var result = Run(new YearMonth(2027, 1));

        Assert.Equal(45.33m, result.Cuota);
        Assert.Equal(80m, result.FullMonthCuota);
        Assert.Contains(result.Trace.Steps, s => s.Id == "ss.prorrateo-mes-alta" && s.Formula == "80 × 17 / 30 = 45.33");
    }

    [Fact]
    public void AltaOnTheFirst_IsNotProrated()
    {
        var result = Run(new YearMonth(2027, 1), alta: new DateOnly(2027, 1, 1));

        Assert.Equal(80m, result.Cuota);
        Assert.DoesNotContain(result.Trace.Steps, s => s.Id == "ss.prorrateo-mes-alta");
    }

    [Fact]
    public void FirstFullMonth_IsTarifaPlana()
    {
        Assert.Equal(80m, Run(new YearMonth(2027, 2)).Cuota);
    }

    [Fact]
    public void TwelfthFullMonthAfterAlta_IsStillTarifaPlana()
    {
        Assert.Equal(80m, Run(new YearMonth(2028, 1)).Cuota);
    }

    [Fact]
    public void MonthAfterTarifaPlanaLapses_IsTheTramoCuota()
    {
        var result = Run(new YearMonth(2028, 2));

        Assert.Equal(425.85m, result.Cuota);
    }

    [Fact]
    public void AltaOnTheFirst_TwelfthMonthIsStillTarifaPlana()
    {
        var result = Run(new YearMonth(2027, 12), alta: new DateOnly(2027, 1, 1));

        Assert.Equal(80m, result.Cuota);
    }

    [Fact]
    public void AltaOnTheFirst_ThirteenthMonthLapses()
    {
        var result = Run(new YearMonth(2028, 1), alta: new DateOnly(2027, 1, 1));

        Assert.Equal(425.85m, result.Cuota);
    }

    [Fact]
    public void AltaOnTheSecond_ThirteenthMonthIsStillTarifaPlana()
    {
        var result = Run(new YearMonth(2028, 1), alta: new DateOnly(2027, 1, 2));

        Assert.Equal(80m, result.Cuota);
    }

    [Fact]
    public void TraceNamesTheTramoAndTheIncomeThatSelectedIt()
    {
        var step = Run(new YearMonth(2027, 2)).Trace.Steps.Single(s => s.Id == "ss.tramo");

        Assert.Contains(new TraceInput("tramo", "General 7"), step.Inputs);
        Assert.Contains(new TraceInput("rendimientoNetoMensual", "2480.00"), step.Inputs);
        Assert.Equal(425.85m, step.Euros());
    }

    // LGSS art. 308.1.c 2.ª: 30,000 × (1 − 0.07) / 12 = 2,325, General 6. Without the 7 % it would be 2,500, General 7.
    [Fact]
    public void GastosGenericosAreDeductedBeforeTheTramoIsChosen()
    {
        var result = Run(new YearMonth(2028, 2), annualComputable: 30000m);

        Assert.Contains(new TraceInput("tramo", "General 6"), result.Trace.Steps.Single(s => s.Id == "ss.tramo").Inputs);
        Assert.Equal(400.20m, result.Cuota);
    }

    [Fact]
    public void TheMonthlyStepShowsTheGastosGenericosAndCitesRule2()
    {
        var step = Run(new YearMonth(2027, 2), annualComputable: 30000m).Trace.Steps.Single(s => s.Id == "ss.rendimiento-neto-mensual");

        Assert.Equal("30000 × (1 − 0.07) / 12 = 2325.00", step.Formula);
        Assert.Equal(2325m, step.Euros());
        Assert.Contains("308.1.c 2.ª", step.Reference, StringComparison.Ordinal);
        Assert.DoesNotContain("over-reserves", step.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceStatesTheMonthAsYearAndMonth()
    {
        var step = Run(new YearMonth(2027, 2)).Trace.Steps.Single(s => s.Id == "ss.tarifa-plana");

        Assert.Contains(new TraceInput("month", "2027-02"), step.Inputs);
    }

    [Fact]
    public void TramoUpperBoundIsInclusive()
    {
        Assert.Equal("Reducida 1", Tramos2025.For(670m).Name);
        Assert.Equal("Reducida 2", Tramos2025.For(670.01m).Name);
    }

    [Fact]
    public void LossProjection_FallsInTheLowestTramo()
    {
        Assert.Equal("Reducida 1", Tramos2025.For(-500m).Name);
    }

    [Fact]
    public void RegularizacionWarningIsAlwaysRaised()
    {
        var warning = Assert.Single(Run(new YearMonth(2028, 6)).Warnings);

        Assert.Equal(WarningCodes.SsRegularizacionAhead, warning.Code);
    }

    [Fact]
    public void MonthBeforeAlta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(new YearMonth(2026, 12)));
    }

    [Fact]
    public void SameInputTwice_GivesTheSameTrace()
    {
        var first = JsonSerializer.Serialize(Run(new YearMonth(2027, 1)));
        var second = JsonSerializer.Serialize(Run(new YearMonth(2027, 1)));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TramoTable_RejectsGaps()
    {
        Assert.Throws<ArgumentException>(() => new TramoTable([TramoOf("a", 0m, 100m, 1m), TramoOf("b", 150m, null, 2m)]));
    }

    [Fact]
    public void TramoTable_RejectsClosedLastTramo()
    {
        Assert.Throws<ArgumentException>(() => new TramoTable([TramoOf("a", 0m, 100m, 1m)]));
    }

    [Fact]
    public void TramoTable_RejectsFirstTramoNotStartingAtZero()
    {
        Assert.Throws<ArgumentException>(() => new TramoTable([TramoOf("a", 10m, null, 1m)]));
    }

    private static Tramo TramoOf(string name, decimal netFrom, decimal? netUpTo, decimal cuotaMin) =>
        new(name, new Money(netFrom), netUpTo is { } upTo ? new Money(upTo) : null, new Money(cuotaMin));
}