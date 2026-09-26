using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class AnnualTrueUpExamples
{
    private static AnnualTrueUpResult Run(
        decimal salary,
        decimal ss,
        decimal ingresos,
        decimal gastos,
        decimal advances = 0m,
        string region = "VC",
        TaxYearConfig? config = null) =>
        AnnualTrueUpCalculator.Gap(
            new AnnualTrueUpInput(
                new EmploymentIncome(new Money(salary), new Money(ss)),
                new ActivityIncome(new Money(ingresos), new Money(gastos)),
                new Money(advances),
                region),
            config ?? TaxYearConfigFiles.Year2025);

    private static decimal Output(AnnualTrueUpResult result, string id) => result.Trace.Steps.Single(s => s.Id == id).Output;

    // Difícil justificación off, so the activity's rendimiento neto is exactly ingresos − gastos.
    private static TaxYearConfig WithoutDificilJustificacion()
    {
        var config = TaxYearConfigFiles.Year2025;
        return config with { Irpf = config.Irpf with { Actividad = new ActividadConfig(new DificilJustificacionConfig(Rate.Zero, Money.Zero)) } };
    }

    [Fact]
    public void EmploymentAloneReproducesGoldenG1AndAddsNothing()
    {
        var result = Run(salary: 30000m, ss: 1950m, ingresos: 0m, gastos: 0m);

        Assert.Equal(2463.00m, Output(result, "renta.solo.cuota-estatal"));
        Assert.Equal(2388.00m, Output(result, "renta.solo.cuota-autonomica"));
        Assert.Equal(Money.Zero, result.LiabilityOnActivity);
        Assert.Equal(Money.Zero, result.Gap);
        Assert.Empty(result.Warnings);
    }

    // LIRPF art. 20: the reducción is measured before the 2,000 of art. 19.2.f. SPEC-011 G2 measures it after and applies the full 7,302.
    [Fact]
    public void ReduccionIsMeasuredOnTheNetBeforeOtrosGastos()
    {
        var result = Run(salary: 18000m, ss: 1170m, ingresos: 0m, gastos: 0m);

        // 7,302 − 1.75 × (16,830 − 14,852)
        Assert.Equal(3840.50m, Output(result, "renta.trabajo.reduccion.solo"));
    }

    [Fact]
    public void ALowSalaryGetsTheWholeFixedReduccion()
    {
        var result = Run(salary: 14852m, ss: 0m, ingresos: 0m, gastos: 0m);

        Assert.Equal(7302m, Output(result, "renta.trabajo.reduccion.solo"));
    }

    [Fact]
    public void ActivityNetOfExactlyTheCapKeepsTheReduccion()
    {
        var result = Run(salary: 20000m, ss: 1300m, ingresos: 6500m, gastos: 0m, config: WithoutDificilJustificacion());

        Assert.Equal(1194.1528m, Output(result, "renta.trabajo.reduccion.stacked"));
        Assert.Equal(Money.Zero, result.ReduccionTrabajoLost);
        Assert.DoesNotContain(result.Warnings, w => w.Code == WarningCodes.ReduccionTrabajoLost);
    }

    [Fact]
    public void ActivityNetOneCentAboveTheCapLosesTheWholeReduccionAndSaysHowMuch()
    {
        var result = Run(salary: 20000m, ss: 1300m, ingresos: 6500.01m, gastos: 0m, config: WithoutDificilJustificacion());

        Assert.Equal(new Money(1194.15m), result.ReduccionTrabajoLost);
        Assert.Equal(0m, Output(result, "renta.trabajo.reduccion.stacked"));
        var warning = Assert.Single(result.Warnings, w => w.Code == WarningCodes.ReduccionTrabajoLost);
        Assert.Contains("the reducción por trabajo of 1194.15 € is lost entirely", warning.Text);
    }

    [Fact]
    public void TheLiabilityIsTheStateAndTheRegionalCuotaTogether()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 960m);

        var estatal = Output(result, "renta.stacked.cuota-estatal") - Output(result, "renta.solo.cuota-estatal");
        var autonomica = Output(result, "renta.stacked.cuota-autonomica") - Output(result, "renta.solo.cuota-autonomica");

        Assert.True(autonomica > 0m);
        Assert.Equal(new Money(estatal + autonomica).Round2(), result.LiabilityOnActivity);
    }

    [Fact]
    public void ARegionWithoutAScaleFailsRatherThanFallingBackToTheStateScale()
    {
        var unknown = Assert.Throws<ConfigNotFoundException>(() => Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 960m, region: "XX"));
        var incomplete = Assert.Throws<ConfigNotFoundException>(() => Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 960m, region: "MD"));

        Assert.Equal("Region XX is not in this configuration.", unknown.Message);
        Assert.StartsWith("Region MD is declared incomplete", incomplete.Message);
    }

    [Fact]
    public void AdvancesAboveTheLiabilityGiveNoGapBecauseARefundIsNotCountedOn()
    {
        // 10,000 net less 500 difícil justificación is taxed 730.75, below the 2,000 advanced.
        var result = Run(salary: 0m, ss: 0m, ingresos: 10000m, gastos: 0m, advances: 2000m);

        Assert.Equal(new Money(730.75m), result.LiabilityOnActivity);
        Assert.Equal(-1269.25m, Output(result, "renta.liability-on-activity") - Output(result, "renta.modelo130-advances"));
        Assert.Equal(Money.Zero, result.Gap);
        Assert.DoesNotContain(result.Warnings, w => w.Code == WarningCodes.MarginalVsEffective);
    }

    [Fact]
    public void AnActivityLossLargerThanTheSalaryLeavesABaseOfZero()
    {
        var result = Run(salary: 5000m, ss: 300m, ingresos: 1000m, gastos: 9000m);

        // The 7,302 reducción is cut to the 2,700 of rendimiento neto, so the salary alone is not taxed.
        Assert.Equal(2700m, Output(result, "renta.trabajo.reduccion.solo"));
        Assert.Equal(0m, Output(result, "renta.stacked.base-liquidable"));
        Assert.Equal(Money.Zero, result.Gap);
    }

    [Fact]
    public void AnActivityLossLowersTheTaxOnTheSalaryAndLeavesNoGap()
    {
        // Base 35,400 falls to 27,400: cuota 7,748.00 falls to 5,256.00.
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 1000m, gastos: 9000m);

        Assert.Equal(new Money(-2492m), result.LiabilityOnActivity);
        Assert.Equal(Money.Zero, result.Gap);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void StepsKeepTheUnroundedAmountAndOnlyTheResultIsRounded()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 4802.40m, advances: 5039.52m);

        Assert.Equal(9217.12748m, Output(result, "renta.liability-on-activity"));
        Assert.Equal(new Money(9217.13m), result.LiabilityOnActivity);
    }

    [Fact]
    public void TheGapWarningNamesTheAmountAndTheDayItIsPayable()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 4802.40m, advances: 5039.52m);

        Assert.Equal(new YearMonth(2026, 6), result.PayableIn);
        var warning = Assert.Single(result.Warnings, w => w.Code == WarningCodes.MarginalVsEffective);
        Assert.Contains("Stacked on the employment income, the 23937.72 € of activity net income adds 9217.13 € of tax: an effective rate of 38.50 %, and 40.90 % on its last euro.", warning.Text);
        Assert.Contains("4177.61 € more, payable by 2026-06-30", warning.Text);
    }

    [Fact]
    public void WithNoSalaryTheWarningDoesNotSpeakOfStacking()
    {
        var result = Run(salary: 0m, ss: 0m, ingresos: 50000m, gastos: 7600m, advances: 8480m);

        var warning = Assert.Single(result.Warnings, w => w.Code == WarningCodes.MarginalVsEffective);
        Assert.DoesNotContain("Stacked", warning.Text);
        Assert.Contains("With no employment income, the 40400.00 € of activity net income is taxed 9548.00 €: an effective rate of 23.63 %, and 36.00 % on its last euro.", warning.Text);
    }
}
