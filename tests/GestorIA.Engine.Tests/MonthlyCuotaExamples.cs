using System.Globalization;
using System.Text.Json;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class MonthlyCuotaExamples
{
    private static SeguridadSocialConfig SeguridadSocial2025 => TaxYearConfigFiles.Year2025.SeguridadSocial;

    private static TramoTable Tramos2025 => SeguridadSocial2025.Tramos;

    private static readonly DateOnly Alta = new(2027, 1, 15);

    // 32,000 × (1 − 0.07) / 12 = 2,480 a month, General 7; 1,356.21 is General 7's base mínima, 1,356.21 × 0.314 = 425.84994 → 425.85.
    private static MonthlyCuotaResult Run(YearMonth month, decimal annualComputable = 32000m, DateOnly? alta = null, decimal baseCotizacion = 1356.21m) =>
        MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta ?? Alta, annualComputable, month, new Money(baseCotizacion)), SeguridadSocial2025);

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
    public void MonthAfterTarifaPlanaLapses_IsTheCuotaAtTheChosenBase()
    {
        var result = Run(new YearMonth(2028, 2));

        Assert.Equal(425.85m, result.Cuota);
        Assert.Equal("months since alta 13 > 12 → cuota at the chosen base 425.85", result.Trace.Steps.Single(s => s.Id == "ss.tarifa-plana").Formula);
    }

    // 1,600.00 × 0.314 = 502.40, whatever tramo the computable falls in.
    [Fact]
    public void TheDebitIsTheChosenBaseTimesTheTipo()
    {
        var result = Run(new YearMonth(2028, 2), baseCotizacion: 1600.00m);
        var step = result.Trace.Steps.Single(s => s.Id == "ss.base-cotizacion");

        Assert.Equal(502.40m, result.Cuota);
        Assert.Equal(502.40m, result.FullMonthCuota);
        Assert.Equal("1600.00 × 0.314 = 502.40", step.Formula);
        Assert.Equal([new TraceInput("baseCotizacion", "1600.00"), new TraceInput("tipoCotizacion", "0.314")], step.Inputs);
        Assert.Equal(502.40m, step.Euros());
        Assert.Contains("308.1.b", step.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public void DebitAgreesWithTheCuota()
    {
        foreach (var (month, alta) in new[] { (new YearMonth(2027, 1), Alta), (new YearMonth(2027, 6), Alta), (new YearMonth(2028, 2), Alta), (new YearMonth(2027, 1), new DateOnly(2027, 1, 1)) })
        {
            Assert.Equal(Run(month, alta: alta).Cuota, MonthlyCuotaCalculator.Debit(alta, month, new Money(1356.21m), SeguridadSocial2025));
        }
    }

    // 30,000 × (1 − 0.07) / 12 = 2,325 a month, General 6: base mínima 1,274.51 → 400.20, base máxima 2,330.00 × 0.314 = 731.62.
    [Theory]
    [InlineData("1356.21", "425.85", "400.20 <= 425.85 <= 731.62 → 425.85, neither topped up nor refunded")]
    [InlineData("1200.00", "400.20", "376.80 < 400.20 → topped up to 400.20")]
    [InlineData("2500.00", "731.62", "785.00 > 731.62 → refunded down to 731.62")]
    public void TgssKeepsTheChosenCuotaClampedBetweenTheTramosBases(string baseCotizacion, string kept, string outcome)
    {
        var result = Run(new YearMonth(2028, 2), annualComputable: 30000m, baseCotizacion: decimal.Parse(baseCotizacion, CultureInfo.InvariantCulture));
        var step = result.Trace.Steps.Single(s => s.Id == "ss.regularizacion");

        Assert.Equal(400.20m, result.Floor);
        Assert.Equal(731.62m, result.Ceiling);
        Assert.Equal(decimal.Parse(kept, CultureInfo.InvariantCulture), result.Kept);
        Assert.Equal(result.Kept, step.Euros());
        Assert.Equal("base mínima 1274.51 → 400.20, base máxima 2330.00 × 0.314 = 731.62; " + outcome, step.Formula);
        Assert.Contains("308.1.c 3.ª–4.ª", step.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStepsRunFromTheTramoThroughTheChosenBaseToTheRegularizacion()
    {
        var ids = Run(new YearMonth(2027, 1)).Trace.Steps.Select(s => s.Id);

        Assert.Equal(["ss.rendimiento-neto-mensual", "ss.tramo", "ss.base-cotizacion", "ss.regularizacion", "ss.tarifa-plana", "ss.prorrateo-mes-alta"], ids);
    }

    // Ley 20/2007 art. 38 ter.6: the cuota reducida is not regularised, so TGSS keeps exactly what it debits, whatever the tramo.
    [Fact]
    public void UnderTarifaPlanaTgssKeepsWhatItDebits()
    {
        var result = Run(new YearMonth(2027, 2), baseCotizacion: 2500.00m);

        Assert.Equal(80m, result.Cuota);
        Assert.Equal(80m, result.Floor);
        Assert.Equal(80m, result.Ceiling);
        Assert.Equal(80m, result.Kept);
        Assert.Equal("months since alta 1 <= 12 → 80, not regularised", result.Trace.Steps.Single(s => s.Id == "ss.tarifa-plana").Formula);
        Assert.Contains("38 ter.6", result.Trace.Steps.Single(s => s.Id == "ss.tarifa-plana").Reference, StringComparison.Ordinal);
    }

    // Alta 15 January: 80 × 17 / 30 = 45.33 debited, and the bounds are prorated with it.
    [Fact]
    public void TheBoundsOfTheMonthOfAltaAreProratedAsTheDebitIs()
    {
        var result = Run(new YearMonth(2027, 1));

        Assert.Equal(45.33m, result.Floor);
        Assert.Equal(45.33m, result.Ceiling);
        Assert.Equal(45.33m, result.Kept);
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
        Assert.Equal(400.20m, result.Floor);
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
        new(name, new Money(netFrom), netUpTo is { } upTo ? new Money(upTo) : null, Money.Zero, Money.Zero, new Money(cuotaMin));
}