using System.Text.Json;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class SetAsideExamples
{
    // G12's input: alta 15 January 2025, no employment, a new activity, 30,000 ingresos and 1,200 gastos projected, as of Q1.
    private static SetAsideInput G12Input(
        Money? ingresos = null,
        Money? gastos = null,
        IReadOnlyList<QuarterToDate>? actuals = null,
        Retenciones? retenciones = null,
        DateOnly? alta = null,
        Quarter asOf = Quarter.Q1,
        EmploymentIncome? employment = null) =>
        new(
            new TaxpayerProfile(
                "VC",
                employment ?? new EmploymentIncome(Money.Zero, Money.Zero),
                new AutonomoRegistration(alta ?? new DateOnly(2025, 1, 15), new PreviousYear.NoActivity(), new NewActivity.Started(NewActivityPeriod.First, Money.Zero))),
            new ActivityPicture(
                actuals ?? [],
                new ActivityProjection(ingresos ?? new Money(30000m), gastos ?? new Money(1200m)),
                retenciones ?? new Retenciones.ForeignPayersOnly()),
            TaxYearConfigFiles.Year2025,
            asOf);

    [Fact]
    public void NoIvaIsCollectedFromEuOrUsClients()
    {
        var result = SetAsideEstimator.Estimate(G12Input());

        Assert.Equal(Money.Zero, result.IvaToSetAside);
        Assert.Contains(result.Warnings, w => w.Code == WarningCodes.SetAsideEstimate && w.Text.Contains("reverse charge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ForeignPayersWithholdNoRetencion()
    {
        var result = SetAsideEstimator.Estimate(G12Input());

        var step = result.Trace.Steps.Single(s => s.Id == "Q1.m130.retenciones");
        Assert.Equal(0m, step.Output);
        Assert.Contains("SPEC-003 §0", step.Reference);
    }

    [Fact]
    public void ResultCarriesTheConfigHashAndAUniqueTrace()
    {
        var result = SetAsideEstimator.Estimate(G12Input());

        Assert.Equal(TaxYearConfigFiles.Year2025.ConfigHash, result.ConfigHash);
        Assert.NotEmpty(result.Trace.Steps);
        Assert.Equal(result.Trace.Steps.Count, result.Trace.Steps.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void ActualsMustBeTheClosedQuartersInOrderFromTheFirstQuarterOfActivity()
    {
        var skipsQ2 = new List<QuarterToDate> { new(Quarter.Q1, new Money(6000m), new Money(345.33m)), new(Quarter.Q3, new Money(18000m), new Money(900m)) };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(ingresos: new Money(9000m), gastos: new Money(300m), actuals: skipsQ2, asOf: Quarter.Q4)));
        Assert.Contains("in order from Q1", error.Message);
    }

    [Fact]
    public void ActualsForAQuarterBeforeTheAltaAreRejected()
    {
        var beforeAlta = new List<QuarterToDate> { new(Quarter.Q1, Money.Zero, Money.Zero), new(Quarter.Q2, new Money(3000m), new Money(160m)) };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(alta: new DateOnly(2025, 5, 1), actuals: beforeAlta, asOf: Quarter.Q2)));
        Assert.Contains("in order from Q2", error.Message);
    }

    [Fact]
    public void ActualsPastTheAsOfQuarterAreRejected()
    {
        var toQ2 = new List<QuarterToDate> { new(Quarter.Q1, new Money(6000m), new Money(345.33m)), new(Quarter.Q2, new Money(13000m), new Money(700m)) };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(actuals: toQ2, asOf: Quarter.Q1)));
        Assert.Contains("past the as-of quarter Q1", error.Message);
    }

    [Fact]
    public void AProjectionWithNoMonthsLeftToCoverIsRejected()
    {
        var wholeYear = new List<QuarterToDate>
        {
            new(Quarter.Q1, new Money(6000m), new Money(345.33m)),
            new(Quarter.Q2, new Money(13000m), new Money(700m)),
            new(Quarter.Q3, new Money(20000m), new Money(1100m)),
            new(Quarter.Q4, new Money(28000m), new Money(1500m)),
        };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(ingresos: new Money(1000m), actuals: wholeYear, asOf: Quarter.Q4)));
        Assert.Contains("none left for the projection", error.Message);
    }

    [Fact]
    public void AWholeYearOfActualsNeedsNoProjection()
    {
        var wholeYear = new List<QuarterToDate>
        {
            new(Quarter.Q1, new Money(6000m), new Money(345.33m)),
            new(Quarter.Q2, new Money(13000m), new Money(700m)),
            new(Quarter.Q3, new Money(20000m), new Money(1100m)),
            new(Quarter.Q4, new Money(28000m), new Money(1500m)),
        };

        var result = SetAsideEstimator.Estimate(G12Input(ingresos: Money.Zero, gastos: Money.Zero, actuals: wholeYear, asOf: Quarter.Q4));

        Assert.Equal(28000m, result.Trace.Steps.Single(s => s.Id == "set-aside.annual-ingresos").Output);
        Assert.Equal(1500m, result.Trace.Steps.Single(s => s.Id == "set-aside.annual-gastos").Output);
        Assert.Equal(0m, result.Trace.Steps.Single(s => s.Id == "set-aside.projected-months").Output);
    }

    // G14's input: Q1 actuals of 6,000 invoiced and 345.33 spent, the cuotas included; 27,000 and 900 projected for April to December.
    [Fact]
    public void TheTraceShowsWhatTheActualsAndTheProjectionEachContribute()
    {
        var actuals = new List<QuarterToDate> { new(Quarter.Q1, new Money(6000.00m), new Money(345.33m)) };

        var result = SetAsideEstimator.Estimate(G12Input(ingresos: new Money(27000.00m), gastos: new Money(900.00m), actuals: actuals, asOf: Quarter.Q2));
        TraceStep Step(string id) => result.Trace.Steps.Single(s => s.Id == id);

        Assert.Equal(6000m, Step("set-aside.actuals").Output);
        Assert.Equal(9m, Step("set-aside.projected-months").Output);
        Assert.Equal("actuals as stated: ingresos 6000.00, gastos 345.33", Step("set-aside.Q1.to-date").Formula);
        Assert.Equal("ingresos 6000.00 real + 27000.00 × 3 / 9 = 15000.00; gastos 345.33 real + 900.00 × 3 / 9 + 240 = 885.33", Step("set-aside.Q2.to-date").Formula);
        Assert.Equal("6000.00 real + 27000.00 projected = 33000.00", Step("set-aside.annual-ingresos").Formula);
    }

    // #2: where the estimator picks between two defensible figures it reserves the higher, and the step that picks says so.
    [Theory]
    [InlineData("set-aside.rendimiento-computable", "over-reserves")]
    [InlineData("set-aside.cuota-ss-month", "bias conservative")]
    [InlineData("set-aside.tarifa-plana-lapse", "over-reserves")]
    [InlineData("set-aside.hold-back-share", "conservative")]
    [InlineData("renta.gap", "Conservative")]
    public void EveryChoiceTowardOverReservingIsStatedInTheTrace(string id, string statement)
    {
        var result = SetAsideEstimator.Estimate(G12Input());

        Assert.Contains(statement, result.Trace.Steps.Single(s => s.Id == id).Reference, StringComparison.Ordinal);
    }

    // SPEC-002 §8, on G18's shape: actuals to Q2, tarifa plana lapsing in July, employment stacked on the activity.
    [Fact]
    public void SameInputTwiceGivesAnIdenticalResultAndTrace()
    {
        SetAsideInput Input() => G12Input(
            ingresos: new Money(18000m),
            gastos: new Money(400m),
            actuals: [new(Quarter.Q1, new Money(8000m), new Money(440m)), new(Quarter.Q2, new Money(17000m), new Money(880m))],
            alta: new DateOnly(2024, 6, 10),
            asOf: Quarter.Q3,
            employment: new EmploymentIncome(new Money(40000m), new Money(2600m)));

        var first = JsonSerializer.Serialize(SetAsideEstimator.Estimate(Input()));
        var second = JsonSerializer.Serialize(SetAsideEstimator.Estimate(Input()));

        Assert.Contains("set-aside.tarifa-plana-lapse", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    [Fact]
    public void WithheldRetencionesAreNotYetSupported()
    {
        var error = Assert.Throws<NotSupportedException>(() => SetAsideEstimator.Estimate(G12Input(retenciones: new Retenciones.Withheld(Money.Zero))));
        Assert.Contains("SPEC-003 §0", error.Message);
    }

    [Fact]
    public void AProjectionWithNoIncomeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(ingresos: Money.Zero)));
    }

    [Fact]
    public void AnAsOfQuarterBeforeTheAltaIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SetAsideEstimator.Estimate(G12Input(alta: new DateOnly(2025, 5, 1))));
    }

    [Fact]
    public void OutflowsAboveReceiptsCapTheHoldBackShareAtOne()
    {
        var result = SetAsideEstimator.Estimate(G12Input(ingresos: new Money(500m), gastos: Money.Zero));

        Assert.Equal(new Rate(1m), result.HoldBackShare);
    }

    // Alta 1 May: eight months of alta, two by the end of Q2. Ingresos 30,000 × 2 / 8 = 7,500; gastos 1,200 × 2 / 8 + 2 × 80 = 460;
    // difícil justificación 5 % of 7,040 = 352; casilla 03 7,500 − 812 = 6,688; 20 % = 1,337.60, less the 100 minoración.
    [Fact]
    public void AnAltaInMayPaysItsFirstModelo130InQ2WithNothingCarriedFromQ1()
    {
        var result = SetAsideEstimator.Estimate(G12Input(alta: new DateOnly(2025, 5, 1), asOf: Quarter.Q2));

        Assert.Equal(Quarter.Q2, result.NextPayment.Quarter);
        Assert.Equal(new Money(1237.60m), result.NextPayment.AIngresar);
        Assert.DoesNotContain(result.Trace.Steps, s => s.Id.StartsWith("Q1.", StringComparison.Ordinal));
    }

    // Alta 1 August: five months of alta, two by the end of Q3. Ingresos 12,000; gastos 480 + 160 = 640; difícil justificación
    // 5 % of 11,360 = 568; casilla 03 12,000 − 1,208 = 10,792; 20 % = 2,158.40, less the 100 minoración.
    [Fact]
    public void AnAltaInAugustPaysItsFirstModelo130InQ3WithNothingCarriedFromEarlierQuarters()
    {
        var result = SetAsideEstimator.Estimate(G12Input(alta: new DateOnly(2025, 8, 1), asOf: Quarter.Q3));

        Assert.Equal(Quarter.Q3, result.NextPayment.Quarter);
        Assert.Equal(new Money(2058.40m), result.NextPayment.AIngresar);
    }

    // Alta 20 March: TGSS charges 80 × 12 / 30 = 32.00 for March, then 80.00 every month from April.
    [Fact]
    public void AnAltaInTheQuartersLastMonthShowsTheFullMonthlyCuotaNotTheProratedOne()
    {
        var result = SetAsideEstimator.Estimate(G12Input(alta: new DateOnly(2025, 3, 20)));

        Assert.Equal(new Money(80m), result.MonthlyCuotaSs);
    }
}
