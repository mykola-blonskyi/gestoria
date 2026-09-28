using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Infrastructure.Transactions;

// A bank statement line stored under its profile (ADR-0006: money in numeric(18,6)). The profile owns its lines: deleting
// the profile deletes them (GestoriaDbContext).
public sealed class BankTransactionRow
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    // What makes an import idempotent, held by a unique index on (ProfileId, LineKey); LineKeys says how it is made.
    public required string LineKey { get; set; }

    // The list's order within a day: the profile's imports counted from 1, then the line's number in its file. Explicit
    // numbers, so no clock or id decides which of a day's lines comes first.
    public int ImportSequence { get; set; }

    public int LineNumber { get; set; }

    public DateOnly BookingDate { get; set; }

    public DateOnly ValueDate { get; set; }

    public required string Description { get; set; }

    public decimal Amount { get; set; }

    public decimal? Balance { get; set; }

    // The class the user gave the line, null until they decide. What the rules make of it is never stored: it is computed on
    // every read, so a change to the rules reaches every line the user has not decided.
    public TransactionClass? Class { get; set; }

    public Classification ClassifiedBy(TransactionRules rules) => rules.Classify(Class, Description, Cents(Amount));

    public BankTransaction ToBankTransaction() =>
        new(BookingDate, ValueDate, Description, Cents(Amount), Balance is { } balance ? Cents(balance) : null);

    // numeric(18,6) answers 12.50 as 12.500000; what is stored has at most two decimals (the parsers refuse more).
    private static Money Cents(decimal stored) => new(decimal.Round(stored, 2));
}

// A statement line's key is the SHA-256 of what the bank printed about it (the two dates, the amount and the description)
// and of its occurrence: the count of identical lines before it in the same file. Two coffees of the same price on the same
// day are two lines with occurrences 1 and 2, not one line twice, and a later export of the same days gives them the same
// two keys again. The balance is left out: it is optional in an export and adds nothing the occurrence does not.
public static class LineKeys
{
    public static IReadOnlyList<(StatementLine Line, string Key)> Of(IEnumerable<StatementLine> statement)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return
        [
            .. statement.Select(line =>
            {
                var movement = line.Movement;
                var printed = string.Join(
                    '\n',
                    movement.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    movement.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    movement.Amount.Round2().Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    movement.Description);
                var occurrence = seen[printed] = seen.GetValueOrDefault(printed) + 1;
                var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{printed}\n{occurrence}")));
                return (line, key);
            }),
        ];
    }
}
