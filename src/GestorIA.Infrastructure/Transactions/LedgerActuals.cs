using System.Globalization;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using GestorIA.Infrastructure.Profiles;
using static System.FormattableString;

namespace GestorIA.Infrastructure.Transactions;

// A movement as the actuals read it. No description: nothing here can put one in a trace (SPEC-013).
public sealed record ClassifiedLine(Guid Id, DateOnly BookingDate, Money Amount, Classification Classification);

// ActualsThrough is the last quarter the movements cover, null when they cover none; the three counts are of the movements
// in the covered quarters.
public sealed record LedgerCounts(Quarter? ActualsThrough, int Counted, int AwaitingReview, int AwaitingInvoice);

// The estimator's input from a stored profile and its movements, with the steps that explain the actuals, which go before
// the engine's own in the answer, and the counts.
public sealed record LedgerEstimate(SetAsideInput Input, IReadOnlyList<TraceStep> Steps, LedgerCounts Counts);

// Pure: the actuals of a stored profile's tax year from its classified movements (#73). Only confirmed activity income and
// RETA cuotas count (business rules 1 and 2), each in the quarter of its booking date.
public static class LedgerActuals
{
    // The amounts are cents; a sum of none keeps two decimals, so the engine's trace shows "0.00" as it does for an input file.
    private static readonly Money NoEuros = new(0.00m);

    // lines: the profile's movements of its tax year. today decides which quarters are closed.
    public static LedgerEstimate Of(Profile profile, IReadOnlyList<ClassifiedLine> lines, TaxYearConfig config, Quarter asOf, DateOnly today)
    {
        var year = config.TaxYear;
        var alta = profile.Taxpayer.Activity.Alta;
        var firstMonth = alta.Year == year ? alta.Month : 1;
        var monthsOfAlta = 13 - firstMonth;
        // A lookup is a dictionary whose values are lists: the lines of each quarter, none for a quarter no line is in.
        var byQuarter = lines.ToLookup(line => (Quarter)((line.BookingDate.Month + 2) / 3));
        bool Closed(Quarter quarter) => today > new DateOnly(year, (int)quarter * 3, 1).AddMonths(1).AddDays(-1);

        // A quarter with no imported line has no evidence and would read as zero income, so it and the rest stay projected.
        var firstQuarter = (Quarter)((firstMonth + 2) / 3);
        var actual = new List<Quarter>();
        for (var quarter = firstQuarter; quarter <= asOf && Closed(quarter) && byQuarter.Contains(quarter); quarter++)
        {
            actual.Add(quarter);
        }

        var next = actual.Count == 0 ? firstQuarter : actual[^1] + 1;
        // A closed quarter up to asOf after next means next is closed and up to asOf too, so the run stopped at next only
        // because it holds no line.
        var later = Enum.GetValues<Quarter>().Where(quarter => quarter > next && quarter <= asOf && Closed(quarter) && byQuarter.Contains(quarter)).ToList();
        TraceStep? coverage = later.Count == 0 ? null : new TraceStep(
            "ledger.coverage",
            TraceSection.Actividad,
            "Trimestre sin movimientos importados",
            [new("quarter", Invariant($"{next}")), new("laterWithMovements", string.Join(", ", later))],
            Invariant($"{next} is closed and holds no imported movement, so the actuals stop ")
                + (actual.Count == 0 ? "before it" : Invariant($"at {actual[^1]}"))
                + Invariant($"; the projection covers {next} and what follows, the movements of {string.Join(", ", later)} included"),
            new TraceValue.Count(later.Count),
            "#73: a quarter with no imported movement has no evidence and would read as zero income, so it stays projected");

        var projection = profile.Projection;
        if (actual.Count == 0)
        {
            return new LedgerEstimate(Input(profile, [], projection, config, asOf), coverage is null ? [] : [coverage], new LedgerCounts(null, 0, 0, 0));
        }

        var steps = new List<TraceStep>();
        var toDate = new List<QuarterToDate>();
        var (ingresosYtd, cuotasYtd) = (NoEuros, NoEuros);
        var (counted, awaitingReview, awaitingInvoice) = (0, 0, 0);
        foreach (var quarter in actual)
        {
            var movements = byQuarter[quarter].ToList();
            var income = movements.Where(line => line.Classification is Classification.Confirmed { Class: TransactionClass.ActivityIncome }).ToList();
            var cuotas = movements.Where(line => line.Classification is Classification.Confirmed { Class: TransactionClass.SocialSecurity }).ToList();
            var review = movements.Where(line => line.Classification is Classification.Unclear or Classification.Suggested).ToList();
            var invoice = movements.Where(line => line.Classification is Classification.Confirmed { Class: TransactionClass.DeductibleExpense }).ToList();

            // Signed: a debit classified as activity income lowers it, and a TGSS refund lowers the cuotas.
            var ingresos = income.Aggregate(NoEuros, (sum, line) => sum + line.Amount);
            var cuotasSs = cuotas.Aggregate(NoEuros, (sum, line) => sum - line.Amount);
            (ingresosYtd, cuotasYtd) = (ingresosYtd + ingresos, cuotasYtd + cuotasSs);
            // Gastos are the cuotas alone: a deductible expense counts only with a linked, confirmed invoice (business rule 1),
            // which GestorIA cannot hold yet.
            toDate.Add(new QuarterToDate(quarter, ingresosYtd, cuotasYtd, cuotasYtd));
            (counted, awaitingReview, awaitingInvoice) = (counted + income.Count + cuotas.Count, awaitingReview + review.Count, awaitingInvoice + invoice.Count);

            steps.Add(new TraceStep(
                Invariant($"ledger.{quarter}.movements"),
                TraceSection.Actividad,
                Invariant($"Movimientos clasificados, {quarter}"),
                [
                    .. income.Select(line => new TraceInput(TransactionClass.ActivityIncome.Name(), Named(line))),
                    .. cuotas.Select(line => new TraceInput(TransactionClass.SocialSecurity.Name(), Named(line))),
                    .. review.Select(line => new TraceInput("awaitingReview", Named(line))),
                    .. invoice.Select(line => new TraceInput("awaitingInvoice", Named(line))),
                ],
                Invariant($"ingresos: {Movements(income.Count)} = {Show(ingresos)}; cuotas SS: {Movements(cuotas.Count)} = {Show(cuotasSs)}; ")
                    + Invariant($"gastos = cuotas SS, no expense having a confirmed invoice yet; not counted: {review.Count} {Await(review.Count)} review, {invoice.Count} {Await(invoice.Count)} an invoice"),
                new TraceValue.Money(ingresos),
                "SPEC-004 §3; business rules 1 and 2: only confirmed movements count, a deductible expense only with a linked, confirmed invoice; "
                    + "business rule 3: the booking date puts a movement in its quarter, a cash approximation of devengo until invoices exist; #73"));
        }

        var last = actual[^1];
        if (coverage is not null)
        {
            steps.Add(coverage);
        }

        var projectedMonths = Enumerable.Range(firstMonth, monthsOfAlta).Count(month => month > (int)last * 3);
        Money Remaining(Money whole) => new Money(whole.Amount * projectedMonths / monthsOfAlta).Round2();
        var remaining = projection with { Ingresos = Remaining(projection.Ingresos), Gastos = Remaining(projection.Gastos) };
        steps.Add(new TraceStep(
            "ledger.projection-remaining",
            TraceSection.Actividad,
            "Proyección de los meses sin movimientos",
            [
                new("projection.ingresos", Show(projection.Ingresos)),
                new("projection.gastos", Show(projection.Gastos)),
                new("monthsOfAlta", Invariant($"{monthsOfAlta}")),
                new("projectedMonths", Invariant($"{projectedMonths}")),
            ],
            Invariant($"{Show(projection.Ingresos)} × {projectedMonths} / {monthsOfAlta} = {Show(remaining.Ingresos)}; {Show(projection.Gastos)} × {projectedMonths} / {monthsOfAlta} = {Show(remaining.Gastos)}"),
            new TraceValue.Money(remaining.Ingresos),
            "#73: the stored projection is the whole year's; the months the actuals cover drop their share, rounded half away from zero to the cent"));

        return new LedgerEstimate(
            Input(profile, toDate, remaining, config, asOf),
            steps,
            new LedgerCounts(last, counted, awaitingReview, awaitingInvoice));
    }

    private static SetAsideInput Input(Profile profile, IReadOnlyList<QuarterToDate> actuals, ActivityProjection projection, TaxYearConfig config, Quarter asOf) =>
        new(profile.Taxpayer, new ActivityPicture(actuals, projection, new Retenciones.ForeignPayersOnly()), config, asOf);

    private static string Named(ClassifiedLine line) => Invariant($"{line.Id} {line.BookingDate:yyyy-MM-dd}");

    private static string Movements(int count) => count == 1 ? "1 movement" : Invariant($"{count} movements");

    private static string Await(int count) => count == 1 ? "awaits" : "await";

    private static string Show(Money money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
