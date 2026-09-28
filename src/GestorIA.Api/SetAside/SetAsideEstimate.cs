using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using GestorIA.Engine;
using GestorIA.Infrastructure.Transactions;
using DomainMoney = GestorIA.Domain.ValueObjects.Money;
using DomainRate = GestorIA.Domain.ValueObjects.Rate;

namespace GestorIA.Api.SetAside;

// SetAsideResult on the wire (SPEC-009 §1): money as strings with two decimals, rates as the exact decimal fraction
// ("0.1947" is 19.47 %), dates ISO-8601, months "yyyy-MM". The trace and the notices keep the engine's order; a stored
// profile's trace starts with the steps that explain its actuals. Ledger is null for an input file, which states its actuals.
public sealed record SetAsideEstimate(
    int TaxYear,
    string ConfigHash,
    string HoldBackShare,
    NextModelo130 NextPayment,
    [property: RegularExpression(Amounts.Cents)] string MonthlyCuotaSs,
    [property: RegularExpression(Amounts.Cents)] string AnnualTrueUpGap,
    string AnnualTrueUpPayableIn,
    DateOnly AnnualTrueUpDueFrom,
    DateOnly AnnualTrueUpDueBy,
    [property: RegularExpression(Amounts.Cents)] string IvaToSetAside,
    IReadOnlyList<TraceStepView> Trace,
    IReadOnlyList<NoticeView> Notices,
    LedgerView? Ledger)
{
    public static SetAsideEstimate From(SetAsideResult result, int taxYear, LedgerEstimate? ledger) => new(
        taxYear,
        result.ConfigHash,
        Share(result.HoldBackShare),
        new NextModelo130(result.NextPayment.Quarter, Euros(result.NextPayment.AIngresar), result.NextPayment.DueWindow.Start, result.NextPayment.DueWindow.End, Filing(result.NextPayment.Filing)),
        Euros(result.MonthlyCuotaSs),
        Euros(result.AnnualTrueUpGap),
        result.AnnualTrueUpPayableIn.ToString(),
        result.AnnualTrueUpDueWindow.Start,
        result.AnnualTrueUpDueWindow.End,
        Euros(result.IvaToSetAside),
        [.. (ledger?.Steps ?? []).Concat(result.Trace.Steps).Select(TraceStepView.From)],
        [.. result.Warnings.Select(w => new NoticeView(w.Code, w.Severity, w.Text))],
        ledger is null ? null : LedgerView.From(ledger.Counts));

    // Rounded half away from zero to the cent, as TraceValue.Money.Display() and the console show it (SPEC-002 §5).
    internal static string Euros(DomainMoney money) => money.Round2().Amount.ToString("0.00", CultureInfo.InvariantCulture);

    internal static string Share(DomainRate rate) => rate.Value.ToString(CultureInfo.InvariantCulture);

    // GestorIA.Engine.Modelo130Filing mapped by hand, following the RetencionesDocument precedent (SetAsideInputDocument.cs):
    // the engine stays free of JSON attributes, so the API mirrors its union with its own enum instead.
    internal static Modelo130FilingView Filing(Modelo130Filing filing) => filing switch
    {
        Modelo130Filing.Ingreso => Modelo130FilingView.Ingreso,
        Modelo130Filing.ADeducir => Modelo130FilingView.ADeducir,
        Modelo130Filing.Negativa => Modelo130FilingView.Negativa,
        _ => throw new UnreachableException("Modelo130Filing is a closed enum of the three casilla 19 sections."),
    };
}

public sealed record NextModelo130(Quarter Quarter, [property: RegularExpression(Amounts.Cents)] string AIngresar, DateOnly DueFrom, DateOnly DueBy, Modelo130FilingView Filing);

// AEAT, instrucciones del modelo 130, casilla 19: the three sections a quarter's result is filed under.
public enum Modelo130FilingView
{
    [JsonStringEnumMemberName("ingreso")]
    Ingreso,
    [JsonStringEnumMemberName("aDeducir")]
    ADeducir,
    [JsonStringEnumMemberName("negativa")]
    Negativa,
}

// What the profile's movements contribute: the last quarter they cover as actuals (null: none, the projection covers the
// year), how many of those quarters' movements entered the estimate, and how many wait for a review or for an invoice.
public sealed record LedgerView(Quarter? ActualsThrough, int Counted, int AwaitingReview, int AwaitingInvoice)
{
    public static LedgerView From(LedgerCounts counts) => new(counts.ActualsThrough, counts.Counted, counts.AwaitingReview, counts.AwaitingInvoice);
}

public sealed record NoticeView(string Code, WarningSeverity Severity, string Text);

public sealed record TraceStepView(
    string Id,
    TraceSection Section,
    string Title,
    IReadOnlyList<TraceInputView> Inputs,
    string Formula,
    TraceOutputView Output,
    string Reference)
{
    public static TraceStepView From(TraceStep step) => new(
        step.Id,
        step.Section,
        step.Title,
        [.. step.Inputs.Select(i => new TraceInputView(i.Name, i.Value))],
        step.Formula,
        TraceOutputView.From(step.Output),
        step.Reference);
}

public sealed record TraceInputView(string Name, string Value);

// Value is a string for every kind: euros with two decimals, a rate as its exact fraction, a count, or a yyyy-MM-dd date.
public sealed record TraceOutputView(TraceValueKind Kind, string Value)
{
    public static TraceOutputView From(TraceValue value) => value switch
    {
        TraceValue.Money money => new(TraceValueKind.Money, SetAsideEstimate.Euros(money.Value)),
        TraceValue.Rate rate => new(TraceValueKind.Rate, SetAsideEstimate.Share(rate.Value)),
        TraceValue.Count count => new(TraceValueKind.Count, count.Value.ToString(CultureInfo.InvariantCulture)),
        TraceValue.Date date => new(TraceValueKind.Date, date.Display()),
        _ => throw new UnreachableException("TraceValue is a closed union of the four kinds above."),
    };
}

public enum TraceValueKind
{
    [JsonStringEnumMemberName("money")]
    Money,
    [JsonStringEnumMemberName("rate")]
    Rate,
    [JsonStringEnumMemberName("count")]
    Count,
    [JsonStringEnumMemberName("date")]
    Date,
}
