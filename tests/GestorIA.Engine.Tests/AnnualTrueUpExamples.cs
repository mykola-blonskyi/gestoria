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
        TaxYearConfig? config = null,
        NewActivity? newActivity = null) =>
        AnnualTrueUpCalculator.Gap(
            new AnnualTrueUpInput(
                new EmploymentIncome(new Money(salary), new Money(ss)),
                new ActivityIncome(new Money(ingresos), new Money(gastos), newActivity ?? new NewActivity.Established()),
                new Money(advances),
                region),
            config ?? TaxYearConfigFiles.Year2025);

    private static decimal Output(AnnualTrueUpResult result, string id) => result.Trace.Steps.Single(s => s.Id == id).Output;

    // Difícil justificación off, so the activity's rendimiento neto is exactly ingresos − gastos.
    private static TaxYearConfig WithoutDificilJustificacion()
    {
        var config = TaxYearConfigFiles.Year2025;
        return config with { Irpf = config.Irpf with { Actividad = config.Irpf.Actividad with { DificilJustificacion = new DificilJustificacionConfig(Rate.Zero, Money.Zero) } } };
    }

    [Fact]
    public void EmploymentAloneReproducesGoldenG1AndAddsNothing()
    {
        var result = Run(salary: 30000m, ss: 1950m, ingresos: 0m, gastos: 0m);

        Assert.Equal(2463.00m, Output(result, "renta.solo.cuota-estatal"));
        Assert.Equal(2338.05m, Output(result, "renta.solo.cuota-autonomica"));
        Assert.Equal(Money.Zero, result.LiabilityOnActivity);
        Assert.Equal(Money.Zero, result.Gap);
        Assert.Empty(result.Warnings);
    }

    // A base of 6,000 is above the state mínimo of 5,550 and below the Valencian 6,105.
    [Fact]
    public void ABaseBetweenTheStateAndTheRegionalMinimoPaysOnlyStateTax()
    {
        var result = Run(salary: 0m, ss: 0m, ingresos: 6000m, gastos: 0m, config: WithoutDificilJustificacion());

        Assert.Equal(42.75m, Output(result, "renta.stacked.cuota-estatal"));
        Assert.Equal(0m, Output(result, "renta.stacked.cuota-autonomica"));
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
        // 10,000 net less 500 difícil justificación is taxed 680.80, below the 2,000 advanced.
        var result = Run(salary: 0m, ss: 0m, ingresos: 10000m, gastos: 0m, advances: 2000m);

        Assert.Equal(new Money(680.80m), result.LiabilityOnActivity);
        Assert.Equal(-1319.20m, Output(result, "renta.liability-on-activity") - Output(result, "renta.modelo130-advances"));
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
        // Base 35,400 falls to 27,400: cuota 7,698.05 falls to 5,206.05.
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
        Assert.Contains("With no employment income, the 40400.00 € of activity net income is taxed 9498.05 €: an effective rate of 23.51 %, and 36.00 % on its last euro.", warning.Text);
    }

    private static NewActivity.Started FirstPeriod(decimal fromFormerEmployer = 0m) => new(NewActivityPeriod.First, new Money(fromFormerEmployer));

    private static TraceStep ReduccionInicio(AnnualTrueUpResult result) => result.Trace.Steps.Single(s => s.Id == "renta.actividad.reduccion-inicio");

    // G11 as a new activity: 20 % of the 23,937.72 net comes off the stacked base. The #9 verifier estimated about 1,958 less tax.
    [Fact]
    public void AFirstPositivePeriodTakesTwentyPercentOffTheActivityNet()
    {
        var established = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 4802.40m, advances: 5039.52m);
        var started = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 4802.40m, advances: 5039.52m, newActivity: FirstPeriod());

        Assert.Equal(4787.544m, ReduccionInicio(started).Output);
        Assert.Equal(54550.176m, Output(started, "renta.stacked.base-liquidable"));
        Assert.Equal(new Money(7259.02m), started.LiabilityOnActivity);
        Assert.Equal(1958.105496m, Output(established, "renta.liability-on-activity") - Output(started, "renta.liability-on-activity"));
    }

    [Fact]
    public void ThePeriodAfterTheFirstPositiveOneIsReducedToo()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 4802.40m, newActivity: new NewActivity.Started(NewActivityPeriod.Following, Money.Zero));

        Assert.Equal(4787.544m, ReduccionInicio(result).Output);
    }

    [Fact]
    public void AnEstablishedActivityIsNotReduced()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 4802.40m);

        Assert.Equal(0m, ReduccionInicio(result).Output);
        Assert.Equal(59337.72m, Output(result, "renta.stacked.base-liquidable"));
    }

    [Fact]
    public void ALossInTheFirstPeriodIsNotEnlarged()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 1000m, gastos: 9000m, newActivity: FirstPeriod());

        Assert.Equal(0m, ReduccionInicio(result).Output);
        Assert.Equal(27400m, Output(result, "renta.stacked.base-liquidable"));
    }

    public static TheoryData<decimal, decimal> ReduccionAroundTheCap() => new()
    {
        { 99999.99m, 19999.998m },
        { 100000m, 20000m },
        { 150000m, 20000m },
    };

    [Theory]
    [MemberData(nameof(ReduccionAroundTheCap))]
    public void OnlyTheFirst100000OfNetIsReduced(decimal net, decimal reduccion)
    {
        var result = Run(salary: 0m, ss: 0m, ingresos: net, gastos: 0m, config: WithoutDificilJustificacion(), newActivity: FirstPeriod());

        Assert.Equal(reduccion, ReduccionInicio(result).Output);
    }

    // LIRPF art. 32.3, last paragraph: "más del 50 por ciento", so exactly half still gets the reduction.
    [Fact]
    public void MoreThanHalfTheIngresosFromAFormerEmployerRemovesTheReduction()
    {
        var half = Run(salary: 0m, ss: 0m, ingresos: 30000m, gastos: 0m, config: WithoutDificilJustificacion(), newActivity: FirstPeriod(fromFormerEmployer: 15000m));
        var overHalf = Run(salary: 0m, ss: 0m, ingresos: 30000m, gastos: 0m, config: WithoutDificilJustificacion(), newActivity: FirstPeriod(fromFormerEmployer: 15000.01m));

        Assert.Equal(6000m, ReduccionInicio(half).Output);
        Assert.Equal(0m, ReduccionInicio(overHalf).Output);
        Assert.StartsWith("ingresos from a former employer 15000.01 > 0.50 × 30000 → 0", ReduccionInicio(overHalf).Formula);
    }

    // G08 as a new activity. Its 7,600 net is reduced to 6,080, under the 6,500 cap, but the cap is tested on the 7,600
    // (AEAT Manual práctico Renta 2025, cap. 3, fase 3), so the reducción por trabajo is still lost.
    [Fact]
    public void TheOtherIncomeCapIsTestedOnTheNetBeforeTheNewActivityReduction()
    {
        var result = Run(salary: 20000m, ss: 1300m, ingresos: 8000m, gastos: 0m, newActivity: FirstPeriod());

        Assert.Equal(1520m, ReduccionInicio(result).Output);
        Assert.Equal(0m, Output(result, "renta.trabajo.reduccion.stacked"));
        Assert.Equal(new Money(1194.15m), result.ReduccionTrabajoLost);
        Assert.Contains(result.Warnings, w => w.Code == WarningCodes.ReduccionTrabajoLost);
    }

    [Fact]
    public void TheTraceNamesTheArticleAndThePeriod()
    {
        var first = ReduccionInicio(Run(salary: 0m, ss: 0m, ingresos: 30000m, gastos: 0m, newActivity: FirstPeriod()));
        var following = ReduccionInicio(Run(salary: 0m, ss: 0m, ingresos: 30000m, gastos: 0m, newActivity: new NewActivity.Started(NewActivityPeriod.Following, Money.Zero)));
        var established = ReduccionInicio(Run(salary: 0m, ss: 0m, ingresos: 30000m, gastos: 0m));

        Assert.StartsWith("Ley 35/2006 (LIRPF) art. 32.3, first period with a positive net", first.Reference);
        Assert.StartsWith("Ley 35/2006 (LIRPF) art. 32.3, period after the first positive one", following.Reference);
        Assert.StartsWith("Ley 35/2006 (LIRPF) art. 32.3, not a newly started activity", established.Reference);
        Assert.Contains("Art. 32.2.1º is ruled out for this profile", first.Reference);
    }

    // Below the 100,000 cap one more euro of net adds only 0.80 € to the base, so the base's 40.90 % costs 32.72 % of that euro.
    [Fact]
    public void WhileTheReductionAppliesTheLastEuroOfActivityIsTaxedAtTheReducedRate()
    {
        var result = Run(salary: 40000m, ss: 2600m, ingresos: 30000m, gastos: 960m, advances: 5408m, newActivity: FirstPeriod());

        Assert.Equal(new Rate(0.409m), result.MarginalRate);
        var warning = Assert.Single(result.Warnings, w => w.Code == WarningCodes.MarginalVsEffective);
        Assert.Contains("and 32.72 % on its last euro (40.90 % on the base, which takes only 80.00 % of that euro after the LIRPF art. 32.3 reduction)", warning.Text);
    }

    [Fact]
    public void AboveTheCapTheLastEuroOfActivityIsTaxedAtTheFullRate()
    {
        var result = Run(salary: 0m, ss: 0m, ingresos: 150000m, gastos: 0m, config: WithoutDificilJustificacion(), newActivity: FirstPeriod());

        var warning = Assert.Single(result.Warnings, w => w.Code == WarningCodes.MarginalVsEffective);
        Assert.Contains(Percent(result.MarginalRate.Value) + " on its last euro.", warning.Text);
    }

    [Fact]
    public void ANegativeAmountFromAFormerEmployerIsRejected()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => new NewActivity.Started(NewActivityPeriod.First, new Money(-0.01m)));

        Assert.Equal("ingresosFromFormerEmployer", e.ParamName);
        Assert.StartsWith("Ingresos from a former employer are never negative.", e.Message);
    }

    [Fact]
    public void MoreFromAFormerEmployerThanTheActivityInvoicedIsRejected()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ActivityIncome(new Money(30000m), Money.Zero, FirstPeriod(fromFormerEmployer: 30000.01m)));

        Assert.Equal("newActivity", e.ParamName);
        Assert.StartsWith("Ingresos from a former employer cannot exceed the activity's ingresos.", e.Message);
    }

    [Fact]
    public void AnUndefinedPeriodIsRejected()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => new NewActivity.Started((NewActivityPeriod)2, Money.Zero));

        Assert.Equal("period", e.ParamName);
    }

    [Fact]
    public void AMissingNewActivityIsRejected()
    {
        var e = Assert.Throws<ArgumentNullException>(() => new ActivityIncome(new Money(30000m), Money.Zero, null!));

        Assert.Equal("newActivity", e.ParamName);
    }

    [Fact]
    public void AFirstPeriodWithoutAPositiveNetIsNotCalledThePositivePeriod()
    {
        var step = ReduccionInicio(Run(salary: 40000m, ss: 2600m, ingresos: 1000m, gastos: 9000m, newActivity: FirstPeriod()));

        Assert.StartsWith("Ley 35/2006 (LIRPF) art. 32.3, no positive period yet", step.Reference);
    }

    private static string Percent(decimal fraction) => (fraction * 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " %";
}
