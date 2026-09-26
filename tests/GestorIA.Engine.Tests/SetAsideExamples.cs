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
        Quarter asOf = Quarter.Q1) =>
        new(
            new TaxpayerProfile(
                "VC",
                new EmploymentIncome(Money.Zero, Money.Zero),
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
    public void ActualsAreNotYetSupported()
    {
        var actuals = new List<QuarterToDate> { new(Quarter.Q1, new Money(1000m), new Money(400m)) };

        var error = Assert.Throws<NotSupportedException>(() => SetAsideEstimator.Estimate(G12Input(actuals: actuals)));
        Assert.Contains("#15", error.Message);
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
