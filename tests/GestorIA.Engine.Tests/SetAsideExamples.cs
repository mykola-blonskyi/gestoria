using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class SetAsideExamples
{
    // The G12 golden scenario: alta 15 January 2025, no employment, no activity the year before, a projection of
    // 30,000 ingresos and 1,200 gastos, asked as of Q1.
    private static SetAsideInput G12Input(
        Money? ingresos = null,
        Money? gastos = null,
        IReadOnlyList<QuarterToDate>? actuals = null,
        Retenciones? retenciones = null,
        DateOnly? alta = null) =>
        new(
            new TaxpayerProfile(
                "VC",
                new EmploymentIncome(Money.Zero, Money.Zero),
                new AutonomoRegistration(alta ?? new DateOnly(2025, 1, 15), new PreviousYear.NoActivity())),
            new ActivityPicture(
                actuals ?? [],
                new ActivityProjection(ingresos ?? new Money(30000m), gastos ?? new Money(1200m)),
                retenciones ?? new Retenciones.ForeignPayersOnly()),
            TaxYearConfigFiles.Year2025,
            Quarter.Q1);

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
}
