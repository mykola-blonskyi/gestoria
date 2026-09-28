using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GestorIA.Api.SetAside;
using GestorIA.Domain.Models;
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

    internal static string Euros(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

// A movement waiting for the user's class: unclear, or with a class a rule suggests (SPEC-004 §3). Money and dates as in
// TransactionView.
public sealed record ReviewItem(
    Guid Id,
    DateOnly BookingDate,
    DateOnly ValueDate,
    string Description,
    [property: RegularExpression(Amounts.Cents)] string Amount,
    ReviewSuggestion? Suggestion)
{
    public static ReviewItem From(BankTransactionRow row, Classification classification)
    {
        var line = row.ToBankTransaction();
        return new(
            row.Id,
            line.BookingDate,
            line.ValueDate,
            line.Description,
            TransactionView.Euros(line.Amount.Amount),
            classification is Classification.Suggested suggested ? new ReviewSuggestion(suggested.Class, suggested.RuleId) : null);
    }
}

// RuleId names the rule of config/transaction-rules.json that suggests the class.
public sealed record ReviewSuggestion(TransactionClass Class, string RuleId);

// The body of POST /transactions/{id}/classify, for the OpenAPI document; ClassifyInput reads the body.
public sealed record TransactionClassification(TransactionClass Class);

