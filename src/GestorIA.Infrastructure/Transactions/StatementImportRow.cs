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
    // The longest stretch a statement's period may run past its first or last movement. An autónomo's account is charged the
    // RETA cuota every month, so a real statement has no month without a movement; a longer stretch is days it does not hold,
    // and counting them as covered would read their income as zero and set aside too little. The bound keeps a wrong period
    // well short of a quarter.
    public const int MaxDaysBeyondMovements = 31;

    // Why a period cannot be a statement's, by the end it concerns ("from", "to", or null for the statement as a whole), each
    // reason to follow the end's name or path: the boundary rule of the import and of the restore alike. first and last are
    // the booking dates of the statement's first and last movement, null when it holds none. A period holds every movement,
    // starts and ends within MaxDaysBeyondMovements of them, and ends on a day already reached unless its movements run later:
    // a period running into days still to come would count them as covered.
    public static IReadOnlyList<(string? End, string Reason)> Refusals(DateOnly from, DateOnly to, DateOnly? first, DateOnly? last, DateOnly today)
    {
        if ((first, last) is not ({ } firstLine, { } lastLine))
        {
            return [(null, Invariant($"holds no movement; a statement's period is read from its movements and may run at most {MaxDaysBeyondMovements} days past them"))];
        }

        var refusals = new List<(string?, string)>();
        if (from > firstLine)
        {
            refusals.Add(("from", Invariant($"is {from:yyyy-MM-dd}, after the statement's first movement on {firstLine:yyyy-MM-dd}; the period must hold every movement")));
        }
        else if (firstLine.DayNumber - from.DayNumber > MaxDaysBeyondMovements)
        {
            refusals.Add(("from", Invariant($"is {from:yyyy-MM-dd}, {firstLine.DayNumber - from.DayNumber} days before the statement's first movement on {firstLine:yyyy-MM-dd}; ") + Beyond));
        }

        if (to < lastLine)
        {
            refusals.Add(("to", Invariant($"is {to:yyyy-MM-dd}, before the statement's last movement on {lastLine:yyyy-MM-dd}; the period must hold every movement")));
        }
        else if (to.DayNumber - lastLine.DayNumber > MaxDaysBeyondMovements)
        {
            refusals.Add(("to", Invariant($"is {to:yyyy-MM-dd}, {to.DayNumber - lastLine.DayNumber} days after the statement's last movement on {lastLine:yyyy-MM-dd}; ") + Beyond));
        }
        else if (to > today && to > lastLine)
        {
            refusals.Add(("to", Invariant($"is {to:yyyy-MM-dd}, after today, {today:yyyy-MM-dd}; a statement's period ends on a day already reached")));
        }

        return refusals;
    }

    private static readonly string Beyond = Invariant(
        $"a period may run at most {MaxDaysBeyondMovements} days past the statement's movements, since an autónomo's account is charged the RETA cuota every month, and days the statement does not hold, counted as covered, would set aside too little");
}
