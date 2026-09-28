using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GestorIA.Api.SetAside;
using GestorIA.Infrastructure.Transactions;

namespace GestorIA.Api.Profiles;

// Everything the installation stores about a profile, in one file the user keeps outside the machine (SPEC-013, #74), and
// what #75's restore reads back. SPEC-009 §2.1 documents the format. Entities is a map of entity kinds, each a list, so a new
// kind is a new member and not a new format.
public sealed record ProfileExport(string Format, int FormatVersion, string Classification, DateTimeOffset ExportedAt, ExportedEntities Entities)
{
    public const string FormatName = "gestoria.export";

    // Raised when a change would make an existing export read differently: a kind or a field removed, renamed or changed in
    // meaning. Adding a kind, or an optional field to one, keeps the version.
    public const int CurrentVersion = 1;

    // The file is labelled as what it holds, so it is recognised as such wherever it ends up.
    public const string PersonalFinancialData = "personal-financial-data";

    // The name the file is offered under: what it is and the day it was made where the user lives (ADR-0016's regions are all
    // on Madrid time), and nothing about whose it is.
    public static string FileName(DateTimeOffset exportedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"gestoria-export-{MadridDay.Of(exportedAt):yyyy-MM-dd}.json");

    public static ProfileExport Of(ProfileView profile, IEnumerable<BankTransactionRow> transactions, DateTimeOffset exportedAt) =>
        new(FormatName, CurrentVersion, PersonalFinancialData, exportedAt, new ExportedEntities([profile], [.. transactions.Select(ExportedBankTransaction.From)]));
}

// One member per table of the database, named after it (ProfileExportEndpoint holds the two together), each holding the rows
// that belong to the exported profile.
public sealed record ExportedEntities(IReadOnlyList<ProfileView> Profiles, IReadOnlyList<ExportedBankTransaction> BankTransactions);

// A stored statement line as the list answers it (TransactionView), plus what a restore needs to store it again exactly: its
// place in the day's order and the key that keeps a later import of the same statement from storing it twice.
public sealed record ExportedBankTransaction(
    Guid Id,
    DateOnly BookingDate,
    DateOnly ValueDate,
    string Description,
    [property: RegularExpression(Amounts.Cents)] string Amount,
    [property: RegularExpression(Amounts.Cents)] string? Balance,
    int ImportSequence,
    int LineNumber,
    string LineKey)
{
    public static ExportedBankTransaction From(BankTransactionRow row)
    {
        var line = row.ToBankTransaction();
        return new(
            row.Id,
            line.BookingDate,
            line.ValueDate,
            line.Description,
            Euros(line.Amount.Amount),
            line.Balance is { } balance ? Euros(balance.Amount) : null,
            row.ImportSequence,
            row.LineNumber,
            row.LineKey);
    }

    private static string Euros(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
