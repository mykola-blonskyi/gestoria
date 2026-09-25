using System.Text.Json;

namespace GestorIA.Engine.Tests;

public class MonthlyCuotaExamples
{
    // config/tax-years/2025.example.json /seguridadSocial/tramos
    internal static readonly TramoTable Tramos2025 = new(
    [
        new Tramo("Reducida 1", 0m, 670m, 205.23m),
        new Tramo("Reducida 2", 670m, 900m, 225.75m),
        new Tramo("Reducida 3", 900m, 1166.70m, 266.80m),
        new Tramo("General 1", 1166.70m, 1300m, 298.61m),
        new Tramo("General 2", 1300m, 1500m, 301.68m),
        new Tramo("General 3", 1500m, 1700m, 301.68m),
        new Tramo("General 4", 1700m, 1850m, 359.15m),
        new Tramo("General 5", 1850m, 2030m, 379.67m),
        new Tramo("General 6", 2030m, 2330m, 400.20m),
        new Tramo("General 7", 2330m, 2760m, 425.85m),
        new Tramo("General 8", 2760m, 3190m, 451.50m),
        new Tramo("General 9", 3190m, 3620m, 477.16m),
        new Tramo("General 10", 3620m, 4050m, 502.81m),
        new Tramo("General 11", 4050m, 6000m, 543.86m),
        new Tramo("General 12", 6000m, null, 605.42m),
    ]);

    // config/tax-years/2025.example.json /seguridadSocial/tarifaPlana
    internal static readonly TarifaPlana TarifaPlana2025 = new(80m, 12);

    private static readonly DateOnly Alta = new(2027, 1, 15);

    private static MonthlyCuotaResult Run(YearMonth month, decimal annualNet = 30000m, DateOnly? alta = null) =>
        MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta ?? Alta, annualNet, month), Tramos2025, TarifaPlana2025);

    [Fact]
    public void MonthOfAlta_IsProratedByDaysOverThirty()
    {
        var result = Run(new YearMonth(2027, 1));

        Assert.Equal(45.33m, result.Cuota);
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
    public void TraceNamesTheTramoAndTheIncomeThatSelectedIt()
    {
        var step = Run(new YearMonth(2027, 2)).Trace.Steps.Single(s => s.Id == "ss.tramo");

        Assert.Contains(new TraceInput("tramo", "General 7"), step.Inputs);
        Assert.Contains(new TraceInput("rendimientoNetoMensual", "2500"), step.Inputs);
        Assert.Equal(425.85m, step.Output);
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
        Assert.Throws<ArgumentException>(() => new TramoTable([new Tramo("a", 0m, 100m, 1m), new Tramo("b", 150m, null, 2m)]));
    }

    [Fact]
    public void TramoTable_RejectsClosedLastTramo()
    {
        Assert.Throws<ArgumentException>(() => new TramoTable([new Tramo("a", 0m, 100m, 1m)]));
    }

    [Fact]
    public void TramoTable_RejectsFirstTramoNotStartingAtZero()
    {
        Assert.Throws<ArgumentException>(() => new TramoTable([new Tramo("a", 10m, null, 1m)]));
    }
}