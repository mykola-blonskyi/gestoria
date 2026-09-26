using System.Globalization;
using System.Text;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using static System.FormattableString;

namespace GestorIA.Cli;

// Prints a SetAsideResult as text: the trace first, then the estimate and the warnings last, where the terminal leaves them
// in view. Display only: amounts are formatted, never rounded or combined, and nothing depends on the current culture.
public static class SetAsideReport
{
    public static string Render(SetAsideResult result, int taxYear, string configFileName)
    {
        // StringBuilder appends to one growing buffer, like pushing lines to an array and joining them once at the end.
        var text = new StringBuilder();

        text.AppendLine(Invariant($"GestorIA set-aside estimate, tax year {taxYear}"));
        text.AppendLine(Invariant($"{result.Trace.Steps.Count} calculation steps first; the estimate and {result.Warnings.Count} warnings follow at the end."));
        text.AppendLine();
        text.AppendLine("Calculation (SPEC-002 §6 trace; figures as the engine computed them, unrounded)");

        TraceSection? section = null;

        // This Select overload also passes each element's index, like Array.prototype.map((step, index) => …).
        foreach (var (step, number) in result.Trace.Steps.Select((step, index) => (step, index + 1)))
        {
            if (step.Section != section)
            {
                section = step.Section;
                text.AppendLine();
                text.AppendLine(Invariant($"  [{section}]"));
            }

            text.AppendLine(Invariant($"  {number,3}. {step.Title}  ({step.Id})"));
            if (step.Inputs.Count > 0)
            {
                text.AppendLine("       inputs:  " + string.Join(", ", step.Inputs.Select(i => Invariant($"{i.Name} = {i.Value}"))));
            }

            text.AppendLine("       formula: " + step.Formula);
            text.AppendLine(Invariant($"       result:  {step.Output}"));
            text.AppendLine("       source:  " + step.Reference);
        }

        var next = result.NextPayment;
        text.AppendLine();
        text.AppendLine("Estimate");
        Row(text, "Hold back from every payment received", Percent(result.HoldBackShare));
        Row(text, Invariant($"Next Modelo 130, {next.Quarter}"), Invariant($"{Euros(next.AIngresar)}, due {Day(next.DueWindow.Start, taxYear)} to {Day(next.DueWindow.End, taxYear)}"));
        Row(text, "Cuota SS per month this quarter", Euros(result.MonthlyCuotaSs));
        Row(text, "Annual return (Renta) gap", Invariant($"{Euros(result.AnnualTrueUpGap)}, payable by the end of {result.AnnualTrueUpPayableIn}"));
        Row(text, "IVA to set aside", Euros(result.IvaToSetAside));
        Row(text, "Tax year", Invariant($"{taxYear}"));
        Row(text, "Configuration", Invariant($"{configFileName}, sha256 {result.ConfigHash}"));

        text.AppendLine();
        text.AppendLine(Invariant($"Warnings ({result.Warnings.Count}): read these before relying on the figures above"));

        // OrderByDescending is a stable sort: warnings of the same severity keep the engine's order.
        foreach (var warning in result.Warnings.OrderByDescending(w => w.Severity))
        {
            var marker = warning.Severity == WarningSeverity.Info ? "  " : "!!";
            text.AppendLine(Invariant($"  {marker} {warning.Severity.ToString().ToUpperInvariant()} {warning.Code}"));
            text.AppendLine("     " + warning.Text);
        }

        return text.ToString();
    }

    // SPEC-010 §5: two decimals and the euro sign.
    private static string Euros(Money money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";

    // The "%" in a custom format multiplies by 100 as it formats; the stored Rate is unchanged.
    private static string Percent(Rate rate) => rate.Value.ToString("0.00 %", CultureInfo.InvariantCulture);

    private static string Day(CalendarDay day, int taxYear) =>
        new DateOnly(taxYear + day.YearOffset, day.Month, day.Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Row(StringBuilder text, string label, string value) => text.AppendLine("  " + label.PadRight(40) + value);
}
