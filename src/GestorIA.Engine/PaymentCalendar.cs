using System.Globalization;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine;

public enum ObligationKind
{
    Modelo130,
    Modelo303,
    Modelo349,
    SeguridadSocial,
    RentaTrueUp,
}

// Money the engine has calculated, or a stated reason it has not (#70): never a null amount by accident.
public abstract record ObligationAmount
{
    private ObligationAmount() { }

    private protected abstract void CloseTheUnion();

    public sealed record Known(Money Value) : ObligationAmount
    {
        private protected override void CloseTheUnion() { }
    }

    public sealed record NotYetKnown(string Reason) : ObligationAmount
    {
        private protected override void CloseTheUnion() { }
    }
}

// Period is the label the engine already has for the obligation: "Q1".."Q4" for a quarterly filing, a YearMonth's own
// "yyyy-MM" for a TGSS cuota, the tax year itself for the Renta true-up.
public sealed record PaymentObligation(ObligationKind Kind, string Period, DueWindow DueWindow, ObligationAmount Amount);

// Every obligation of a tax year, from the set-aside estimate's own every-quarter and every-month results (#2, #70), so a
// payments calendar never recomputes the estimator's business rules. Modelo 303 has no calculator yet, so it and Modelo 349 carry a stated reason
// rather than a guessed figure.
public static class PaymentCalendar
{
    private const string NoModelo303Calculator = "GestorIA has no Modelo 303 calculator yet";

    // SPEC-003 §2.1: a quarter with no intra-EU operations files no 349 at all, a fact the engine cannot know without
    // classified transactions (#72, #73), so the reason names the condition rather than implying every quarter owes one.
    private const string NoModelo349Calculator =
        "GestorIA has no Modelo 349 calculator yet, and this filing is only due for a quarter with intra-EU operations";

    public static IReadOnlyList<PaymentObligation> Build(SetAsideInput input)
    {
        var estimate = SetAsideEstimator.Estimate(input);
        var config = input.Config;
        var region = input.Profile.Region;
        var obligations = new List<PaymentObligation>();

        foreach (var quarter in estimate.Quarters)
        {
            var period = quarter.Quarter.ToString();
            obligations.Add(new PaymentObligation(ObligationKind.Modelo130, period, quarter.DueWindow, new ObligationAmount.Known(quarter.AIngresar)));

            var (sharedWindow, _) = FilingDeadline.SharedQuarterlyWindow(quarter.Quarter, region, config);
            obligations.Add(new PaymentObligation(ObligationKind.Modelo303, period, sharedWindow, new ObligationAmount.NotYetKnown(NoModelo303Calculator)));
            obligations.Add(new PaymentObligation(ObligationKind.Modelo349, period, sharedWindow, new ObligationAmount.NotYetKnown(NoModelo349Calculator)));
        }

        foreach (var cuota in estimate.MonthlyCuotas)
        {
            var window = FilingDeadline.MonthlyCuotaSs(cuota.Month, region, config);
            obligations.Add(new PaymentObligation(ObligationKind.SeguridadSocial, cuota.Month.ToString(), window, new ObligationAmount.Known(new Money(cuota.Result.Cuota))));
        }

        obligations.Add(new PaymentObligation(
            ObligationKind.RentaTrueUp,
            config.TaxYear.ToString(CultureInfo.InvariantCulture),
            estimate.TrueUp.DueWindow,
            new ObligationAmount.Known(estimate.TrueUp.Gap)));

        return [.. obligations.OrderBy(o => o.DueWindow.End).ThenBy(o => o.Kind)];
    }
}
