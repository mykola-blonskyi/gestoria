using static System.FormattableString;

namespace GestorIA.Infrastructure.Transactions;

// One import of a bank statement under its profile (#88): the statement's period, stated by the user at import or else the
// first to the last booking date of every line the file held, duplicates of earlier imports included. Every import is
// recorded, one that stores nothing new too, so the periods together say which days the imported statements cover. The
// profile owns its imports and each import its movements (GestoriaDbContext).
public sealed class StatementImportRow
{
    public Guid ProfileId { get; set; }

    // The profile's imports counted from 1, taken under the profile's row lock; BankTransactionRow.ImportSequence names it.
    public int Sequence { get; set; }

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public StatementPeriod Period => new(From, To);
}

// The days one imported statement covers, both ends included.
public sealed record StatementPeriod(DateOnly From, DateOnly To)
{
    // Why a period cannot be a statement's, by the end it concerns ("from" or "to"), each reason to follow the end's name or
    // path: the boundary rule of the import and of the restore alike. first and last are the booking dates of the statement's
    // first and last movement, null when it holds none. A period ends on a day already reached, unless its movements run
    // later: a period running into days still to come would count them as covered and read their income as zero.
    public static IReadOnlyList<(string End, string Reason)> Refusals(DateOnly from, DateOnly to, DateOnly? first, DateOnly? last, DateOnly today)
    {
        var refusals = new List<(string, string)>();
        if (first is { } firstLine && from > firstLine)
        {
            refusals.Add(("from", Invariant($"is {from:yyyy-MM-dd}, after the statement's first movement on {firstLine:yyyy-MM-dd}; the period must hold every movement")));
        }

        if (last is { } lastLine && to < lastLine)
        {
            refusals.Add(("to", Invariant($"is {to:yyyy-MM-dd}, before the statement's last movement on {lastLine:yyyy-MM-dd}; the period must hold every movement")));
        }
        else if (to > today && (last is null || to > last))
        {
            refusals.Add(("to", Invariant($"is {to:yyyy-MM-dd}, after today, {today:yyyy-MM-dd}; a statement's period ends on a day already reached")));
        }

        if (refusals.Count == 0 && from > to)
        {
            refusals.Add(("from", Invariant($"is {from:yyyy-MM-dd}, after the period's last day, {to:yyyy-MM-dd}")));
        }

        return refusals;
    }
}
