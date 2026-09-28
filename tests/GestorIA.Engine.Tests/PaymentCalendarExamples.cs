using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

// #70: the payments calendar built from the SetAsideResult the set-aside estimate answers, for G12's shape (alta 15 January
// 2025, a new activity, 30,000 ingresos and 1,200 gastos projected at a base of 1,274.51).
public class PaymentCalendarExamples
{
    private static SetAsideInput G12Input(DateOnly? alta = null) => new(
        new TaxpayerProfile(
            "VC",
            new EmploymentIncome(Money.Zero, Money.Zero),
            new AutonomoRegistration(alta ?? new DateOnly(2025, 1, 15), new PreviousYear.NoActivity(), new NewActivity.Started(NewActivityPeriod.First, Money.Zero))),
        new ActivityPicture([], new ActivityProjection(new Money(30000m), new Money(1200m), new Money(1274.51m)), new Retenciones.ForeignPayersOnly()),
        TaxYearConfigFiles.Year2025,
        Quarter.Q4);

    [Fact]
    public void EveryQuarterCarriesModelo130Modelo303AndModelo349()
    {
        var obligations = PaymentCalendar.Build(G12Input());

        foreach (var quarter in new[] { Quarter.Q1, Quarter.Q2, Quarter.Q3, Quarter.Q4 })
        {
            var period = quarter.ToString();
            Assert.Single(obligations, o => o.Kind == ObligationKind.Modelo130 && o.Period == period);
            Assert.Single(obligations, o => o.Kind == ObligationKind.Modelo303 && o.Period == period);
            Assert.Single(obligations, o => o.Kind == ObligationKind.Modelo349 && o.Period == period);
        }
    }

    [Fact]
    public void Modelo130IsKnownAndSharesTheEstimatorsOwnFigure()
    {
        var estimate = SetAsideEstimator.Estimate(G12Input() with { AsOf = Quarter.Q1 });
        var obligations = PaymentCalendar.Build(G12Input());

        var q1 = Assert.Single(obligations, o => o.Kind == ObligationKind.Modelo130 && o.Period == "Q1");

        var known = Assert.IsType<ObligationAmount.Known>(q1.Amount);
        Assert.Equal(estimate.NextPayment.AIngresar, known.Value);
        Assert.Equal(estimate.NextPayment.DueWindow, q1.DueWindow);
    }

    [Theory]
    [InlineData(ObligationKind.Modelo303)]
    [InlineData(ObligationKind.Modelo349)]
    public void AFormWithNoCalculatorIsNotYetKnownRatherThanGuessed(ObligationKind kind)
    {
        var obligations = PaymentCalendar.Build(G12Input());

        var q1 = Assert.Single(obligations, o => o.Kind == kind && o.Period == "Q1");

        Assert.IsType<ObligationAmount.NotYetKnown>(q1.Amount);
    }

    // Modelo 303 and 349 share the exact quarterly window as Modelo 130 (Reglamento del IVA, RD 1624/1992 art. 71.4; Orden
    // EHA/769/2010 art. 10.2).
    [Fact]
    public void Modelo303And349ShareModelo130sWindow()
    {
        var obligations = PaymentCalendar.Build(G12Input());
        var byKindAndPeriod = obligations.ToLookup(o => (o.Kind, o.Period));

        var modelo130 = byKindAndPeriod[(ObligationKind.Modelo130, "Q2")].Single();
        var modelo303 = byKindAndPeriod[(ObligationKind.Modelo303, "Q2")].Single();
        var modelo349 = byKindAndPeriod[(ObligationKind.Modelo349, "Q2")].Single();

        Assert.Equal(modelo130.DueWindow, modelo303.DueWindow);
        Assert.Equal(modelo130.DueWindow, modelo349.DueWindow);
    }

    [Fact]
    public void EveryMonthOfAltaCarriesAKnownTgssCuota()
    {
        var obligations = PaymentCalendar.Build(G12Input());

        var cuotas = obligations.Where(o => o.Kind == ObligationKind.SeguridadSocial).ToList();

        Assert.Equal(12, cuotas.Count);
        Assert.All(cuotas, o => Assert.IsType<ObligationAmount.Known>(o.Amount));
        Assert.Equal(["2025-01", "2025-02", "2025-03"], cuotas.Take(3).Select(o => o.Period));
    }

    // A March alta has no obligation before it: SetAsideResult.MonthlyCuotas starts at the month of alta.
    [Fact]
    public void AMonthBeforeTheAltaHasNoTgssCuota()
    {
        var obligations = PaymentCalendar.Build(G12Input(alta: new DateOnly(2025, 3, 20)));

        Assert.DoesNotContain(obligations, o => o.Kind == ObligationKind.SeguridadSocial && (o.Period == "2025-01" || o.Period == "2025-02"));
    }

    [Fact]
    public void TheRentaTrueUpIsOneKnownObligationForTheWholeYear()
    {
        var obligations = PaymentCalendar.Build(G12Input());

        var renta = Assert.Single(obligations, o => o.Kind == ObligationKind.RentaTrueUp);

        Assert.Equal("2025", renta.Period);
        Assert.IsType<ObligationAmount.Known>(renta.Amount);
    }

    [Fact]
    public void ObligationsComeBackInDueDateOrder()
    {
        var obligations = PaymentCalendar.Build(G12Input());

        Assert.Equal([.. obligations.OrderBy(o => o.DueWindow.End)], obligations);
    }
}
