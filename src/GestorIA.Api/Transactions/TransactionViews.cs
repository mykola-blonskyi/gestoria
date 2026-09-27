using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GestorIA.Api.SetAside;
using GestorIA.Infrastructure.Transactions;

namespace GestorIA.Api.Transactions;

// The answer to an import: how many movements the file held, how many were new and how many an earlier import already stored.
public sealed record BankStatementImport(string Bank, int Lines, int Imported, int AlreadyImported);

// A stored statement line (SPEC-009 §1): money as a signed string with two decimals, positive for money in, dates ISO-8601.
public sealed record TransactionView(
    Guid Id,
    DateOnly BookingDate,
    DateOnly ValueDate,
    string Description,
    [property: RegularExpression(Amounts.Cents)] string Amount,
    [property: RegularExpression(Amounts.Cents)] string? Balance)
{
    public static TransactionView From(BankTransactionRow row)
    {
        var line = row.ToBankTransaction();
        return new(row.Id, line.BookingDate, line.ValueDate, line.Description, Euros(line.Amount.Amount), line.Balance is { } balance ? Euros(balance.Amount) : null);
    }

    private static string Euros(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

