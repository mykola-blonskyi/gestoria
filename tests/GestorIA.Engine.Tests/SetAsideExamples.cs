using System.Text.Json;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class SetAsideExamples
{
    // G12's input: alta 15 January 2025, no employment, a new activity, 30,000 ingresos and 1,200 gastos projected at a base of
    // 1,274.51, as of Q1.
    private static SetAsideInput G12Input(
        Money? ingresos = null,
        Money? gastos = null,
        IReadOnlyList<QuarterToDate>? actuals = null,
        Retenciones? retenciones = null,
        DateOnly? alta = null,
        Quarter asOf = Quarter.Q1,
        EmploymentIncome? employment = null,
        NewActivity? newActivity = null,
        Money? baseCotizacion = null) =>
        new(
            new TaxpayerProfile(
                "VC",
                employment ?? new EmploymentIncome(Money.Zero, Money.Zero),
                new AutonomoRegistration(alta ?? new DateOnly(2025, 1, 15), new PreviousYear.NoActivity(), newActivity ?? new NewActivity.Started(NewActivityPeriod.First, Money.Zero))),
            new ActivityPicture(
                actuals ?? [],
                new ActivityProjection(ingresos ?? new Money(30000m), gastos ?? new Money(1200m), baseCotizacion ?? new Money(1274.51m)),
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
        Assert.Equal(0m, step.Euros());
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
        var skipsQ2 = new List<QuarterToDate> { new(Quarter.Q1, new Money(6000m), new Money(345.33m), new Money(205.33m)), new(Quarter.Q3, new Money(18000m), new Money(900m), new Money(685.33m)) };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(ingresos: new Money(9000m), gastos: new Money(300m), actuals: skipsQ2, asOf: Quarter.Q4)));
        Assert.Contains("in order from Q1", error.Message);
    }

    [Fact]
    public void ActualsForAQuarterBeforeTheAltaAreRejected()
    {
        var beforeAlta = new List<QuarterToDate> { new(Quarter.Q1, Money.Zero, Money.Zero, Money.Zero), new(Quarter.Q2, new Money(3000m), new Money(160m), new Money(160m)) };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(alta: new DateOnly(2025, 5, 1), actuals: beforeAlta, asOf: Quarter.Q2)));
        Assert.Contains("in order from Q2", error.Message);
    }

    [Fact]
    public void ActualsPastTheAsOfQuarterAreRejected()
    {
        var toQ2 = new List<QuarterToDate> { new(Quarter.Q1, new Money(6000m), new Money(345.33m), new Money(205.33m)), new(Quarter.Q2, new Money(13000m), new Money(700m), new Money(445.33m)) };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(actuals: toQ2, asOf: Quarter.Q1)));
        Assert.Contains("past the as-of quarter Q1", error.Message);
    }

    [Fact]
    public void AProjectionWithNoMonthsLeftToCoverIsRejected()
    {
        var wholeYear = new List<QuarterToDate>
        {
            new(Quarter.Q1, new Money(6000m), new Money(345.33m), new Money(205.33m)),
            new(Quarter.Q2, new Money(13000m), new Money(700m), new Money(445.33m)),
            new(Quarter.Q3, new Money(20000m), new Money(1100m), new Money(685.33m)),
            new(Quarter.Q4, new Money(28000m), new Money(1500m), new Money(925.33m)),
        };

        var error = Assert.Throws<ArgumentException>(() => SetAsideEstimator.Estimate(G12Input(ingresos: new Money(1000m), actuals: wholeYear, asOf: Quarter.Q4)));
        Assert.Contains("none left for the projection", error.Message);
    }

    [Fact]
    public void AWholeYearOfActualsNeedsNoProjection()
    {
        var wholeYear = new List<QuarterToDate>
        {
            new(Quarter.Q1, new Money(6000m), new Money(345.33m), new Money(205.33m)),
            new(Quarter.Q2, new Money(13000m), new Money(700m), new Money(445.33m)),
            new(Quarter.Q3, new Money(20000m), new Money(1100m), new Money(685.33m)),
            new(Quarter.Q4, new Money(28000m), new Money(1500m), new Money(925.33m)),
        };

        var result = SetAsideEstimator.Estimate(G12Input(ingresos: Money.Zero, gastos: Money.Zero, actuals: wholeYear, asOf: Quarter.Q4));

        Assert.Equal(28000m, result.Trace.Steps.Single(s => s.Id == "set-aside.annual-ingresos").Euros());
        Assert.Equal(1500m, result.Trace.Steps.Single(s => s.Id == "set-aside.annual-gastos").Euros());
        Assert.Equal(0, result.Trace.Steps.Single(s => s.Id == "set-aside.projected-months").Count());
    }

    // G14's input: Q1 actuals of 6,000 invoiced and 345.33 spent, the cuotas included; 27,000 and 900 projected for April to December.
    [Fact]
    public void TheTraceShowsWhatTheActualsAndTheProjectionEachContribute()
    {
        var actuals = new List<QuarterToDate> { new(Quarter.Q1, new Money(6000.00m), new Money(345.33m), new Money(205.33m)) };

        var result = SetAsideEstimator.Estimate(G12Input(ingresos: new Money(27000.00m), gastos: new Money(900.00m), actuals: actuals, asOf: Quarter.Q2));
        TraceStep Step(string id) => result.Trace.Steps.Single(s => s.Id == id);

        Assert.Equal(6000m, Step("set-aside.actuals").Euros());
        Assert.Equal(9, Step("set-aside.projected-months").Count());
        Assert.Equal("actuals as stated: ingresos 6000.00, gastos 345.33", Step("set-aside.Q1.to-date").Formula);
        Assert.Equal("ingresos 6000.00 real + 27000.00 × 3 / 9 = 15000.00; gastos 345.33 real + 900.00 × 3 / 9 + 240 = 885.33", Step("set-aside.Q2.to-date").Formula);
        Assert.Equal("6000.00 real + 27000.00 projected = 33000.00", Step("set-aside.annual-ingresos").Formula);
    }

    // Alta in 2020, Q1 and Q2 closed: 14,000 invoiced and 1,000 spent to June besides the six cuotas paid, which the gastos
    // also hold; 20,000 and 800 projected for July to December besides the six cuotas debited at the chosen base. Previo
    // 32,200 − 6 × paid − 6 × debit, less 5 % difícil justificación, plus the twelve cuotas back: a computable of
    // 30,590 + 0.3 × (paid + debit), each a month's, over twelve months of alta; × 0.93 / 12 a month, General 7 (2,330 to
    // 2,760) in every example below. General 7 prices 425.85 a month at its base mínima of 1,356.21 and 2,760.00 × 0.314 = 866.64
    // at its base máxima: TGSS keeps the year's debits between 12 × 425.85 = 5,110.20 and 12 × 866.64 = 10,399.68.
    private static SetAsideInput EstablishedInput(decimal cuotaPaid, decimal baseCotizacion, NewActivity? newActivity = null) => G12Input(
        ingresos: new Money(20000.00m),
        gastos: new Money(800.00m),
        actuals:
        [
            new(Quarter.Q1, new Money(7000.00m), new Money(500m + (3 * cuotaPaid)), new Money(3 * cuotaPaid)),
            new(Quarter.Q2, new Money(14000.00m), new Money(1000m + (6 * cuotaPaid)), new Money(6 * cuotaPaid)),
        ],
        alta: new DateOnly(2020, 3, 1),
        asOf: Quarter.Q3,
        newActivity: newActivity,
        baseCotizacion: new Money(baseCotizacion));

    private static TraceStep Step(SetAsideResult result, string id) => result.Trace.Steps.Single(s => s.Id == id);

    // Paid at General 7's base mínima, 425.85, and debited at a base of 1,600.00: 1,600.00 × 0.314 = 502.40 a month, 3,014.40
    // for the six. Previo 32,200 − 2,555.10 − 3,014.40 = 26,630.50; difícil justificación 1,331.525; casilla 0224 25,298.975;
    // computable 25,298.975 + 2,555.10 + 3,014.40 = 30,868.475, × 0.93 / 12 = 2,392.31 a month, General 7, whose base mínima
    // prices 425.85. TGSS debits and keeps 502.40, between 425.85 and 866.64; the closed 2,555.10 stand: 5,569.50 for the year.
    [Fact]
    public void TheProjectedMonthsAreDebitedAtTheChosenBaseWhateverTheTramo()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(425.85m, 1600.00m));

        Assert.Contains(new TraceInput("tramo", "General 7"), Step(result, "ss.tramo").Inputs);
        Assert.Equal(3014.40m, Step(result, "set-aside.cuotas-ss-projected").Euros());
        Assert.Equal(30868.475m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(new Money(502.40m), result.MonthlyCuotaSs);
        Assert.Equal(5569.50m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // #52: paid 450.00 a month to June, 2,700.00. Previo 32,200 − 2,700.00 − 2,555.10 = 26,944.90; difícil justificación
    // 1,347.245; casilla 0224 25,597.655; computable 25,597.655 + 2,700.00 paid + 2,555.10 debited = 30,852.755, General 7. The
    // year's 5,255.10 debited lies between 5,110.20 and 10,399.68, so TGSS keeps it (RD 2064/1995 art. 46.2 5.ª a).
    [Fact]
    public void TheClosedMonthsAddBackTheCuotasPaidAndTgssKeepsThemBetweenTheBases()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(450.00m, 1356.21m));

        Assert.Equal(2700.00m, Step(result, "set-aside.cuotas-ss-to-date").Euros());
        Assert.Equal("paid to the end of Q2, as stated: 2700.00", Step(result, "set-aside.cuotas-ss-to-date").Formula);
        Assert.Equal(30852.755m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(
            "debited 2700.00 paid + 2555.10 projected = 5255.10, between 5110.20 at the base mínima and 10399.68 at the base máxima → 5255.10 stands",
            Step(result, "set-aside.cuota-ss-year").Formula);
        Assert.Equal(5255.10m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // Paid 1,000.00 a month to June, 6,000.00, above the 5,199.84 General 7's base máxima prices for six months. Previo
    // 32,200 − 6,000.00 − 2,555.10 = 23,644.90; difícil justificación 1,182.245; casilla 0224 22,462.655; computable
    // 22,462.655 + 6,000.00 + 2,555.10 = 31,017.755, × 0.93 / 12 = 2,403.88 a month, General 7. RD 2064/1995 art. 46.2 3.ª
    // averages the year's bases, (6 × 1,000.00 / 0.314 + 6 × 1,356.21) / 12 ≈ 2,270.46, inside General 7's 1,356.21 to
    // 2,760.00, so 5.ª a regularises nothing: the year's 8,555.10 debited stands, where a clamp of the closed months alone
    // would have refunded them down to 5,199.84.
    [Fact]
    public void ClosedMonthsPaidAboveTheBaseMaximaStandWhenTheYearsAverageBaseIsWithinTheTramo()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(1000.00m, 1356.21m));

        Assert.Equal(6000.00m, Step(result, "set-aside.cuotas-ss-to-date").Euros());
        Assert.Equal(31017.755m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(
            "debited 6000.00 paid + 2555.10 projected = 8555.10, between 5110.20 at the base mínima and 10399.68 at the base máxima → 8555.10 stands",
            Step(result, "set-aside.cuota-ss-year").Formula);
        Assert.Equal(8555.10m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // Paid 400.00 a month to June, 2,400.00. Previo 32,200 − 2,400.00 − 2,555.10 = 27,244.90; difícil justificación 1,362.245;
    // casilla 0224 25,882.655; computable 25,882.655 + 2,400.00 + 2,555.10 = 30,837.755, 2,389.93 a month, General 7. The
    // year's 4,955.10 debited is below the 5,110.20 at General 7's base mínima, so TGSS tops it up (RD 2064/1995 art. 46.2 5.ª b).
    [Fact]
    public void AYearDebitedBelowTheBaseMinimaIsToppedUpToIt()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(400.00m, 1356.21m));

        Assert.Equal(2400.00m, Step(result, "set-aside.cuotas-ss-to-date").Euros());
        Assert.Equal(30837.755m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(
            "debited 2400.00 paid + 2555.10 projected = 4955.10, between 5110.20 at the base mínima and 10399.68 at the base máxima → topped up to 5110.20",
            Step(result, "set-aside.cuota-ss-year").Formula);
        Assert.Equal(5110.20m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // Paid 1,000.00 a month to June and a base of 3,000.00 after: 3,000.00 × 0.314 = 942.00 debited a month, 5,652.00 for six.
    // Previo 32,200 − 6,000.00 − 5,652.00 = 20,548.00; difícil justificación 1,027.40; casilla 0224 19,520.60; computable
    // 19,520.60 + 6,000.00 + 5,652.00 = 31,172.60, 2,415.88 a month, General 7. The year's 11,652.00 debited is above the
    // 12 × 866.64 = 10,399.68 at its base máxima, so TGSS refunds it down to that (RD 2064/1995 art. 46.2 5.ª c).
    [Fact]
    public void AYearDebitedAboveTheBaseMaximaIsRefundedDownToIt()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(1000.00m, 3000.00m));

        Assert.Equal(31172.60m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(
            "debited 6000.00 paid + 5652.00 projected = 11652.00, between 5110.20 at the base mínima and 10399.68 at the base máxima → refunded down to 10399.68",
            Step(result, "set-aside.cuota-ss-year").Formula);
        Assert.Equal(10399.68m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // A base of 1,274.51, General 6's base mínima: 1,274.51 × 0.314 = 400.20 debited a month, 2,401.20 for six. Previo 32,200 −
    // 2,555.10 − 2,401.20 = 27,243.70; difícil justificación 1,362.185; casilla 0224 25,881.515; computable 25,881.515 + 2,555.10
    // + 2,401.20 = 30,837.815, 2,389.93 a month, still General 7. The gastos hold what is debited, 3,555.10 real + 800.00 +
    // 2,401.20 = 6,756.30, and TGSS debits 400.20 a month, but the year's 4,956.30 is below the 5,110.20 at the base mínima,
    // so TGSS tops the year up to it.
    [Fact]
    public void AProjectedBaseBelowTheTramosBaseMinimaIsDebitedButTheYearIsToppedUpToTheFloor()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(425.85m, 1274.51m));

        Assert.Equal(2401.20m, Step(result, "set-aside.cuotas-ss-projected").Euros());
        Assert.Equal(30837.815m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(6756.30m, Step(result, "set-aside.annual-gastos").Euros());
        Assert.Equal(new Money(400.20m), result.MonthlyCuotaSs);
        Assert.Equal(5110.20m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // A base of 3,000.00: 942.00 debited a month, 5,652.00 for six. Previo 32,200 − 2,555.10 − 5,652.00 = 23,992.90; difícil
    // justificación 1,199.645; casilla 0224 22,793.255; computable 22,793.255 + 2,555.10 + 5,652.00 = 31,000.355, 2,402.53 a
    // month, General 7. The gastos hold 3,555.10 + 800.00 + 5,652.00 = 10,007.10. Each projected month is above General 7's
    // base máxima, but the year's average base, (6 × 1,356.21 + 6 × 3,000.00) / 12 = 2,178.11, is inside the tramo, so TGSS
    // keeps all 8,207.10 debited (RD 2064/1995 art. 46.2 5.ª a).
    [Fact]
    public void AProjectedBaseAboveTheTramosBaseMaximaStandsWhenTheYearsAverageBaseIsWithinTheTramo()
    {
        var result = SetAsideEstimator.Estimate(EstablishedInput(425.85m, 3000.00m));

        Assert.Equal(5652.00m, Step(result, "set-aside.cuotas-ss-projected").Euros());
        Assert.Equal(31000.355m, Step(result, "set-aside.rendimiento-computable").Euros());
        Assert.Equal(10007.10m, Step(result, "set-aside.annual-gastos").Euros());
        Assert.Equal(new Money(942.00m), result.MonthlyCuotaSs);
        Assert.Equal(8207.10m, Step(result, "set-aside.cuota-ss-year").Euros());
    }

    // TGSS takes casilla 0224, which comes before every LIRPF art. 32 reduction, so the art. 32.3 status cannot move the tramo.
    [Fact]
    public void TheNewActivityReductionDoesNotLowerTheRendimientoComputable()
    {
        decimal Computable(NewActivity newActivity) =>
            Step(SetAsideEstimator.Estimate(EstablishedInput(425.85m, 1356.21m, newActivity)), "set-aside.rendimiento-computable").Euros();

        Assert.Equal(
            Computable(new NewActivity.Started(NewActivityPeriod.First, Money.Zero)),
            Computable(new NewActivity.Established()));
    }

    [Fact]
    public void TheRendimientoComputableCitesRule1AndCasilla0224()
    {
        var reference = SetAsideEstimator.Estimate(G12Input()).Trace.Steps.Single(s => s.Id == "set-aside.rendimiento-computable").Reference;

        Assert.Contains("308.1.c 1.ª", reference, StringComparison.Ordinal);
        Assert.Contains("0224", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("over-reserves", reference, StringComparison.Ordinal);
    }

    // #2: where the estimator picks between two defensible figures it reserves the higher, and the step that picks says so.
    [Theory]
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
            actuals: [new(Quarter.Q1, new Money(8000m), new Money(440m), new Money(240m)), new(Quarter.Q2, new Money(17000m), new Money(880m), new Money(480m))],
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

        Assert.Equal([Quarter.Q2, Quarter.Q3, Quarter.Q4], result.Quarters.Select(q => q.Quarter));
        Assert.Equal(result.NextPayment.AIngresar, result.Quarters.Single(q => q.Quarter == Quarter.Q2).Resultado);
        Assert.Equal(result.Quarters.Single(q => q.Quarter == Quarter.Q2).Filing, result.NextPayment.Filing);
        Assert.Equal(result.AnnualTrueUpGap, result.TrueUp.Gap);
        Assert.Equal(result.AnnualTrueUpPayableIn, result.TrueUp.PayableIn);
        Assert.Equal(result.TrueUp.DueWindow, result.AnnualTrueUpDueWindow);
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
