using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Profiles;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Infrastructure.Transactions;

// A stored profile's movements as the estimate and the review queue read them.
public static class Ledger
{
    // The transactions list's order: booking date, then the imports' order, then the lines' order in their files.
    public static IQueryable<BankTransactionRow> InListOrder(this IQueryable<BankTransactionRow> rows) =>
        rows.OrderBy(t => t.BookingDate).ThenBy(t => t.ImportSequence).ThenBy(t => t.LineNumber);

    public static IQueryable<BankTransactionRow> OfTaxYear(this IQueryable<BankTransactionRow> rows, Guid profileId, int taxYear)
    {
        var (first, last) = (new DateOnly(taxYear, 1, 1), new DateOnly(taxYear, 12, 31));
        return rows.Where(t => t.ProfileId == profileId && t.BookingDate >= first && t.BookingDate <= last).InListOrder();
    }

    // The one way from a stored profile to the set-aside estimator's input: its movements of the tax year, classified as they
    // are read, give the actuals of its closed quarters (LedgerActuals).
    public static async Task<LedgerEstimate> SetAsideInputAsync(
        this GestoriaDbContext db, TransactionRules rules, ProfileRow row, TaxYearConfig config, Quarter asOf, DateOnly today, CancellationToken cancellationToken)
    {
        var profile = row.ToProfile();
        var stored = await db.BankTransactions.AsNoTracking().OfTaxYear(row.Id, profile.TaxYear).ToListAsync(cancellationToken);
        var lines = stored.Select(line => new ClassifiedLine(line.Id, line.BookingDate, line.ToBankTransaction().Amount, line.ClassifiedBy(rules))).ToList();
        // Of every year: a statement running into January shows the previous December was covered to its end.
        var statements = await db.StatementImports.AsNoTracking().Where(i => i.ProfileId == row.Id)
            .Select(i => new StatementPeriod(i.From, i.To))
            .ToListAsync(cancellationToken);
        var movementDays = await db.BankTransactions.AsNoTracking().Where(t => t.ProfileId == row.Id)
            .Select(t => t.BookingDate).Distinct().OrderBy(day => day)
            .ToListAsync(cancellationToken);
        return LedgerActuals.Of(profile, lines, statements, movementDays, config, asOf, today);
    }
}
