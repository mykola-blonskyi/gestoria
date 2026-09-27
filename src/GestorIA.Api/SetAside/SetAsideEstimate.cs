using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using GestorIA.Engine;
using DomainMoney = GestorIA.Domain.ValueObjects.Money;
using DomainRate = GestorIA.Domain.ValueObjects.Rate;

namespace GestorIA.Api.SetAside;

// SetAsideResult on the wire (SPEC-009 §1): money as strings with two decimals, rates as the exact decimal fraction
// ("0.1947" is 19.47 %), dates ISO-8601, months "yyyy-MM". The trace and the notices keep the engine's order.
public sealed record SetAsideEstimate(
    int TaxYear,
    string ConfigHash,
    string HoldBackShare,
    NextModelo130 NextPayment,
    [property: RegularExpression(Amounts.Cents)] string MonthlyCuotaSs,
    [property: RegularExpression(Amounts.Cents)] string AnnualTrueUpGap,
    string AnnualTrueUpPayableIn,
    [property: RegularExpression(Amounts.Cents)] string IvaToSetAside,
    IReadOnlyList<TraceStepView> Trace,
    IReadOnlyList<NoticeView> Notices)
{
    public static SetAsideEstimate From(SetAsideResult result, int taxYear) => new(
        taxYear,
        result.ConfigHash,
        Share(result.HoldBackShare),
        new NextModelo130(result.NextPayment.Quarter, Euros(result.NextPayment.AIngresar), result.NextPayment.DueWindow.Start, result.NextPayment.DueWindow.End),
        Euros(result.MonthlyCuotaSs),
        Euros(result.AnnualTrueUpGap),
        result.AnnualTrueUpPayableIn.ToString(),
        Euros(result.IvaToSetAside),
        [.. result.Trace.Steps.Select(TraceStepView.From)],
        [.. result.Warnings.Select(w => new NoticeView(w.Code, w.Severity, w.Text))]);

    // Rounded half away from zero to the cent, as TraceValue.Money.Display() and the console show it (SPEC-002 §5).
    internal static string Euros(DomainMoney money) => money.Round2().Amount.ToString("0.00", CultureInfo.InvariantCulture);

    internal static string Share(DomainRate rate) => rate.Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record NextModelo130(Quarter Quarter, [property: RegularExpression(Amounts.Cents)] string AIngresar, DateOnly DueFrom, DateOnly DueBy);

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
