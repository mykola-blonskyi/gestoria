using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using GestorIA.Api.SetAside;
using GestorIA.Engine;
using DomainMoney = GestorIA.Domain.ValueObjects.Money;

namespace GestorIA.Api.Profiles;

// PaymentCalendar on the wire (SPEC-009 §2, #70): every obligation of the profile's tax year, in due-date order. Money is a
// string with two decimals, as SetAsideEstimate; an obligation the engine cannot price is NotYetKnownAmount, never a null.
// Modelo349QuarterlyFilingCap is the year's threshold above which Modelo 349 is monthly, a case the calendar does not model
// (it lists the quarterly dates), so the page can say when those dates stop applying.
public sealed record PaymentsCalendarView(
    int TaxYear,
    string ConfigHash,
    [property: RegularExpression(Amounts.Cents)] string Modelo349QuarterlyFilingCap,
    IReadOnlyList<ObligationView> Obligations)
{
    public static PaymentsCalendarView From(IReadOnlyList<PaymentObligation> obligations, TaxYearConfig config) =>
        new(config.TaxYear, config.ConfigHash, Euros(config.Modelo349.QuarterlyFilingCap), [.. obligations.Select(ObligationView.From)]);

    internal static string Euros(DomainMoney money) => money.Round2().Amount.ToString("0.00", CultureInfo.InvariantCulture);
}

public sealed record ObligationView(ObligationKind Kind, string Period, DateOnly DueFrom, DateOnly DueBy, ObligationAmountView Amount)
{
    public static ObligationView From(PaymentObligation obligation) =>
        new(obligation.Kind, obligation.Period, obligation.DueWindow.Start, obligation.DueWindow.End, ObligationAmountView.From(obligation.Amount));
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(KnownAmount), "known")]
[JsonDerivedType(typeof(NotYetKnownAmount), "notYetKnown")]
public abstract record ObligationAmountView
{
    public static ObligationAmountView From(ObligationAmount amount) => amount switch
    {
        ObligationAmount.Known known => new KnownAmount(PaymentsCalendarView.Euros(known.Value)),
        ObligationAmount.NotYetKnown notYetKnown => new NotYetKnownAmount(notYetKnown.Reason),
        _ => throw new UnreachableException("ObligationAmount is a closed union of the two kinds above."),
    };
}

public sealed record KnownAmount([property: RegularExpression(Amounts.Cents)] string Euros) : ObligationAmountView;

public sealed record NotYetKnownAmount(string Reason) : ObligationAmountView;
