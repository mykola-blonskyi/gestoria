using System.Globalization;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using GestorIA.Infrastructure.Profiles;
using static System.FormattableString;

namespace GestorIA.Infrastructure.Transactions;

// A movement as the actuals read it. No description: nothing here can put one in a trace (SPEC-013).
public sealed record ClassifiedLine(Guid Id, DateOnly BookingDate, Money Amount, Classification Classification);

// ActualsThrough is the last quarter the movements cover, null when they cover none. Counted and AwaitingInvoice are of the
// movements in the covered quarters; AwaitingReview of those in every closed quarter up to asOf, which holds the rest back.
public sealed record LedgerCounts(Quarter? ActualsThrough, int Counted, int AwaitingReview, int AwaitingInvoice);

// The estimator's input from a stored profile and its movements, with the steps that explain the actuals, which go before
// the engine's own in the answer, and the counts.
public sealed record LedgerEstimate(SetAsideInput Input, IReadOnlyList<TraceStep> Steps, LedgerCounts Counts);

// Pure: the actuals of a stored profile's tax year from its classified movements (#73). Only confirmed activity income and
// RETA cuotas count (business rules 1 and 2), each in the quarter of its booking date, and only in a quarter none of whose
// movements awaits review.
public static class LedgerActuals
{
    // What a confirmed movement of each class does to the actuals, and so the one list of the classes whose money enters a
    // figure of the estimate. A class not here is outside the activity and enters none. A rule may confirm only a class not
    // here (TransactionRules): a wrong class here moves money nobody confirmed.
    private enum Part
    {
        Ingresos,
        CuotasSs,
        // Would enter gastos with a linked, confirmed invoice (business rule 1), which cannot exist yet.
        AwaitingInvoice,
    }

    private static readonly IReadOnlyDictionary<TransactionClass, Part> Parts = new Dictionary<TransactionClass, Part>
    {
        [TransactionClass.ActivityIncome] = Part.Ingresos,
        [TransactionClass.SocialSecurity] = Part.CuotasSs,
        [TransactionClass.DeductibleExpense] = Part.AwaitingInvoice,
    };

    public static bool Counts(TransactionClass transactionClass) => Parts.ContainsKey(transactionClass);

    // The amounts are cents; a sum of none keeps two decimals, so the engine's trace shows "0.00" as it does for an input file.
    private static readonly Money NoEuros = new(0.00m);

    // lines: the profile's movements of its tax year. statements: the period of each statement imported for the profile, of any
    // year. movementDays: the distinct booking dates of the profile's stored movements, of any year, in date order. today
    // decides which quarters are closed.
    public static LedgerEstimate Of(
        Profile profile, IReadOnlyList<ClassifiedLine> lines, IReadOnlyList<StatementPeriod> statements, IReadOnlyList<DateOnly> movementDays, TaxYearConfig config, Quarter asOf, DateOnly today)
    {
        var year = config.TaxYear;
        var alta = profile.Taxpayer.Activity.Alta;
        var firstMonth = alta.Year == year ? alta.Month : 1;
        var monthsOfAlta = 13 - firstMonth;
        // A lookup is a dictionary whose values are lists: the lines of each quarter, none for a quarter no line is in.
        var byQuarter = lines.ToLookup(line => QuarterOf(line.BookingDate));
        DateOnly LastDay(Quarter quarter) => new DateOnly(year, (int)quarter * 3, 1).AddMonths(1).AddDays(-1);
        bool Closed(Quarter quarter) => today > LastDay(quarter);
        var covered = Merged(statements);
        // A quarter's movements are all imported only when the statements' periods together cover every day of it that can
        // hold activity, from its first day (the alta, when that falls inside it) to one day past its last: a period read from
        // the lines holds no day before its first line or after its last, so a statement ending on a quarter's last day, or
        // starting after its first, leaves it projected until a later or earlier statement is imported.
        DateOnly From(Quarter quarter) => new[] { LastDay(quarter).AddDays(1).AddMonths(-3), alta }.Max();
        DateOnly? Uncovered(Quarter quarter)
        {
            var from = From(quarter);
            var span = covered.FirstOrDefault(span => span.From <= from && span.To >= from);
            return span is null ? from : span.To > LastDay(quarter) ? null : span.To.AddDays(1);
        }

        bool Covered(Quarter quarter) => Uncovered(quarter) is null;
        var quiet = QuietStretches(covered, movementDays, alta);
        QuietStretch? Quiet(Quarter quarter) => quiet.FirstOrDefault(stretch => stretch.From <= LastDay(quarter) && stretch.To >= From(quarter));
        static bool AwaitsReview(ClassifiedLine line) => line.Classification is Classification.Unclear or Classification.Suggested;

        // The quarters that could be actuals: from the first of activity, closed, and not after asOf, so an unbroken run.
        var firstQuarter = QuarterOf(new DateOnly(year, firstMonth, 1));
        var closed = Enum.GetValues<Quarter>().Where(quarter => quarter >= firstQuarter && quarter <= asOf && Closed(quarter)).ToList();
        // A quarter with no imported line has no evidence, one the statements' periods do not reach past may have more, one a
        // quiet stretch runs into lacks a statement whatever the periods claim, and one with a line awaiting review has income
        // not known yet; each would read as missing income, so it and the rest stay projected.
        var actual = closed.TakeWhile(quarter => byQuarter.Contains(quarter) && Covered(quarter) && Quiet(quarter) is null && !byQuarter[quarter].Any(AwaitsReview)).ToList();
        var waiting = closed.SelectMany(quarter => byQuarter[quarter]).Where(AwaitsReview).ToList();

        var steps = new List<TraceStep>();
        // The run stopped at a closed quarter holding no movement while a later closed one holds some.
        List<Quarter> later = actual.Count < closed.Count && !byQuarter.Contains(closed[actual.Count]) ? [.. closed.Skip(actual.Count + 1).Where(byQuarter.Contains)] : [];
        if (later.Count > 0)
        {
            var next = closed[actual.Count];
            steps.Add(new TraceStep(
                "ledger.coverage",
                TraceSection.Actividad,
                "Trimestre sin movimientos importados",
                [new("quarter", Invariant($"{next}")), new("laterWithMovements", string.Join(", ", later))],
                Invariant($"{next} is closed and holds no imported movement, so the actuals stop ")
                    + (actual.Count == 0 ? "before it" : Invariant($"at {actual[^1]}"))
                    + Invariant($"; the projection covers {next} and what follows, the movements of {string.Join(", ", later)} included"),
                new TraceValue.Count(later.Count),
                "#73: a quarter with no imported movement has no evidence and would read as zero income, so it stays projected"));
        }

        // The run stopped at a quarter with movements that the statements' periods do not cover.
        if (actual.Count < closed.Count && byQuarter.Contains(closed[actual.Count]) && Uncovered(closed[actual.Count]) is { } gap)
        {
            var reached = closed[actual.Count];
            var gapEnd = covered.FirstOrDefault(span => span.From > gap)?.From.AddDays(-1);
            steps.Add(new TraceStep(
                "ledger.coverage",
                TraceSection.Actividad,
                "Trimestre no cubierto por los extractos importados",
                [
                    new("quarter", Invariant($"{reached}")),
                    new("uncoveredFrom", Invariant($"{gap:yyyy-MM-dd}")),
                    new("uncoveredThrough", gapEnd is { } end ? Invariant($"{end:yyyy-MM-dd}") : ""),
                    .. statements.OrderBy(period => period.From).ThenBy(period => period.To)
                        .Select(period => new TraceInput("statementPeriod", Invariant($"{period.From:yyyy-MM-dd}..{period.To:yyyy-MM-dd}"))),
                ],
                (gapEnd is { } through
                    ? Invariant($"no imported statement's period covers the days from {gap:yyyy-MM-dd} through {through:yyyy-MM-dd}")
                    : Invariant($"no imported statement's period covers the days from {gap:yyyy-MM-dd} on"))
                    + Invariant($"; {reached} stays on the projection, with what follows, until statements whose periods cover its days from {From(reached):yyyy-MM-dd} through {LastDay(reached).AddDays(1):yyyy-MM-dd} are imported"),
                new TraceValue.Count(byQuarter[reached].Count()),
                "#73, #88: the imported statements cover the union of their periods, each stated at import or else its lines' first to last booking date; "
                    + "a quarter they do not cover to one day past its end may hold income not imported, so it stays on the projection"));
        }

        // The run stopped at a covered quarter with movements that a quiet stretch runs into.
        if (actual.Count < closed.Count && byQuarter.Contains(closed[actual.Count]) && Covered(closed[actual.Count]) && Quiet(closed[actual.Count]) is { } stretch)
        {
            var reached = closed[actual.Count];
            steps.Add(new TraceStep(
                "ledger.coverage",
                TraceSection.Actividad,
                "Tramo sin movimientos importados",
                [
                    new("quarter", Invariant($"{reached}")),
                    new("quietFrom", Invariant($"{stretch.From:yyyy-MM-dd}")),
                    new("quietThrough", Invariant($"{stretch.To:yyyy-MM-dd}")),
                ],
                Invariant($"no stored movement from {stretch.From:yyyy-MM-dd} through {stretch.To:yyyy-MM-dd} ({stretch.Days} days, over {StatementPeriod.MaxDaysBeyondMovements}), ")
                    + Invariant($"though the statements' periods cover those days; {reached} stays on the projection, with what follows, until the statement holding that stretch's movements is imported"),
                new TraceValue.Count(stretch.Days),
                "#94: an autónomo's account is charged the RETA cuota on the last business day of every month, so a covered stretch of more than "
                    + Invariant($"{StatementPeriod.MaxDaysBeyondMovements} days without a stored movement means a statement is missing, and its quarter may hold income not imported")));
        }

        if (waiting.Count > 0)
        {
            var quarters = waiting.GroupBy(line => QuarterOf(line.BookingDate)).Select(group => Invariant($"{group.Key} {group.Count()}"));
            steps.Add(new TraceStep(
                "ledger.pending-review",
                TraceSection.Actividad,
                "Movimientos pendientes de revisión",
                [.. waiting.Select(line => new TraceInput("awaitingReview", Named(line)))],
                Invariant($"{Movements(waiting.Count)} of the closed quarters through {asOf} {Await(waiting.Count)} review ({string.Join(", ", quarters)}); ")
                    + "a quarter enters the actuals only once none of its movements awaits review, so the projection covers the first such quarter and what follows",
                new TraceValue.Count(waiting.Count),
                "Business rule 2: an unreviewed movement is unknown, not zero income, so its quarter stays projected until it is classified; #73"));
        }

        var projection = profile.Projection;
        if (actual.Count == 0)
        {
            return new LedgerEstimate(Input(profile, [], projection, config, asOf), steps, new LedgerCounts(null, 0, waiting.Count, 0));
        }

        var movementSteps = new List<TraceStep>();
        var toDate = new List<QuarterToDate>();
        var (ingresosYtd, cuotasYtd) = (NoEuros, NoEuros);
        var (counted, awaitingInvoice) = (0, 0);
        foreach (var quarter in actual)
        {
            var movements = byQuarter[quarter].ToList();
            List<ClassifiedLine> Of(Part part) =>
                [.. movements.Where(line => line.Classification is Classification.Confirmed confirmed && Parts.TryGetValue(confirmed.Class, out var found) && found == part)];
            var (income, cuotas, invoice) = (Of(Part.Ingresos), Of(Part.CuotasSs), Of(Part.AwaitingInvoice));

            // Signed: a debit classified as activity income lowers it, and a TGSS refund lowers the cuotas.
            var ingresos = income.Aggregate(NoEuros, (sum, line) => sum + line.Amount);
            var cuotasSs = cuotas.Aggregate(NoEuros, (sum, line) => sum - line.Amount);
            (ingresosYtd, cuotasYtd) = (ingresosYtd + ingresos, cuotasYtd + cuotasSs);
            // Gastos are the cuotas alone: a deductible expense counts only with a linked, confirmed invoice (business rule 1),
            // which GestorIA cannot hold yet.
            toDate.Add(new QuarterToDate(quarter, ingresosYtd, cuotasYtd, cuotasYtd));
            (counted, awaitingInvoice) = (counted + income.Count + cuotas.Count, awaitingInvoice + invoice.Count);

            movementSteps.Add(new TraceStep(
                Invariant($"ledger.{quarter}.movements"),
                TraceSection.Actividad,
                Invariant($"Movimientos clasificados, {quarter}"),
                [
                    .. income.Select(line => new TraceInput(TransactionClass.ActivityIncome.Name(), Named(line))),
                    .. cuotas.Select(line => new TraceInput(TransactionClass.SocialSecurity.Name(), Named(line))),
                    .. invoice.Select(line => new TraceInput("awaitingInvoice", Named(line))),
                ],
                Invariant($"ingresos: {Movements(income.Count)} = {Show(ingresos)}; cuotas SS: {Movements(cuotas.Count)} = {Show(cuotasSs)}; ")
                    + Invariant($"gastos = cuotas SS, no expense having a confirmed invoice yet; not counted: {invoice.Count} {Await(invoice.Count)} an invoice"),
                new TraceValue.Money(ingresos),
                "SPEC-004 §3; business rules 1 and 2: only confirmed movements count, a deductible expense only with a linked, confirmed invoice; "
                    + "business rule 3: the booking date puts a movement in its quarter, a cash approximation of devengo until invoices exist; #73"));
        }

        var last = actual[^1];
        steps.InsertRange(0, movementSteps);

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
            new LedgerCounts(last, counted, waiting.Count, awaitingInvoice));
    }

    private static SetAsideInput Input(Profile profile, IReadOnlyList<QuarterToDate> actuals, ActivityProjection projection, TaxYearConfig config, Quarter asOf) =>
        new(profile.Taxpayer, new ActivityPicture(actuals, projection, new Retenciones.ForeignPayersOnly()), config, asOf);

    // The statements' periods joined where they overlap or meet (one ending the day before the next begins), in date order.
    private static List<StatementPeriod> Merged(IReadOnlyList<StatementPeriod> statements)
    {
        var merged = new List<StatementPeriod>();
        foreach (var span in statements.OrderBy(span => span.From))
        {
            // The last day there is has no next day to meet.
            if (merged.Count > 0 && (merged[^1].To == DateOnly.MaxValue || span.From <= merged[^1].To.AddDays(1)))
            {
                merged[^1] = merged[^1] with { To = new[] { merged[^1].To, span.To }.Max() };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    // Days the statements' periods cover, both ends included, holding no stored movement.
    private sealed record QuietStretch(DateOnly From, DateOnly To)
    {
        public int Days => To.DayNumber - From.DayNumber + 1;
    }

    // The runs of more than MaxDaysBeyondMovements covered days from the alta on without a stored movement, in date order. Two
    // periods each within their bound can chain over a month no statement holds, and one statement can leave a month out; the
    // RETA cuota every month rules both out for a real account (#94). Days no period covers are claimed by none, so no run
    // crosses them, and before the alta no cuota is charged.
    private static List<QuietStretch> QuietStretches(List<StatementPeriod> covered, IReadOnlyList<DateOnly> movementDays, DateOnly alta)
    {
        var stretches = new List<QuietStretch>();
        foreach (var span in covered)
        {
            // Day numbers, so the day after 9999-12-31 is a number rather than an overflow.
            var from = Math.Max(span.From.DayNumber, alta.DayNumber);
            var movements = movementDays.Where(day => day >= span.From && day <= span.To).Select(day => day.DayNumber);
            foreach (var next in movements.Append(span.To.DayNumber + 1))
            {
                if (next - from > StatementPeriod.MaxDaysBeyondMovements)
                {
                    stretches.Add(new QuietStretch(DateOnly.FromDayNumber(from), DateOnly.FromDayNumber(next - 1)));
                }

                from = Math.Max(from, next + 1);
            }
        }

        return stretches;
    }

    private static Quarter QuarterOf(DateOnly date) => (Quarter)((date.Month + 2) / 3);

    private static string Named(ClassifiedLine line) => Invariant($"{line.Id} {line.BookingDate:yyyy-MM-dd}");

    private static string Movements(int count) => count == 1 ? "1 movement" : Invariant($"{count} movements");

    private static string Await(int count) => count == 1 ? "awaits" : "await";

    private static string Show(Money money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
