using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Api.Tests;

// The migration that records statement imports (#88), run on an installation holding movements from before it. Each import
// that stored movements gets the period #73 read from them, so the estimate is the same after the upgrade as before, and the
// next import takes the next number.
public class StatementImportsMigration
{
    private const string BeforeStatementImports = "20260927235507_TransactionClass";

    [Fact]
    public async Task EachEarlierImportGetsThePeriodItsStoredMovementsSpanned()
    {
        var connectionString = await TestDatabase.NewConnectionString();
        var options = new DbContextOptionsBuilder<GestoriaDbContext>().UseNpgsql(connectionString).Options;
        List<(int Sequence, DateOnly From, DateOnly To)> spans;
        await using (var db = new GestoriaDbContext(options))
        {
            await db.Database.MigrateAsync(BeforeStatementImports);
            var profile = ProfileRow.From(new Profile(
                2025,
                new TaxpayerProfile("VC", new EmploymentIncome(Money.Zero, Money.Zero), new AutonomoRegistration(new(2025, 1, 15), new PreviousYear.NoActivity(), new NewActivity.Established())),
                new ActivityProjection(new Money(30000.00m), new Money(1200.00m), new Money(1274.51m))));
            db.Profiles.Add(profile);
            db.BankTransactions.AddRange(
                Movement(profile.Id, 1, 1, "2025-01-02"),
                Movement(profile.Id, 1, 2, "2025-03-31"),
                Movement(profile.Id, 1, 3, "2025-02-14"),
                Movement(profile.Id, 2, 1, "2025-04-07"),
                Movement(profile.Id, 2, 2, "2025-06-30"));
            await db.SaveChangesAsync();

            // #73's read of each import's span, run on the schema it was written for.
            spans = [.. (await db.BankTransactions.GroupBy(t => t.ImportSequence)
                .Select(import => new { import.Key, From = import.Min(t => t.BookingDate), To = import.Max(t => t.BookingDate) })
                .ToListAsync())
                .Select(span => (span.Key, span.From, span.To))
                .OrderBy(span => span.Key)];
        }

        await using var api = new ApiFactory(connectionString);
        var client = api.CreateClient();
        var id = JsonNode.Parse(await client.GetStringAsync("/api/v1/profiles"))![0]!["id"]!.GetValue<string>();

        var statements = await client.Statements(id);

        Assert.Equal([(1, Day("2025-01-02"), Day("2025-03-31")), (2, Day("2025-04-07"), Day("2025-06-30"))], spans);
        Assert.Equal(
            spans,
            statements.Select(s => (s!["sequence"]!.GetValue<int>(), Day(s["from"]!.GetValue<string>()), Day(s["to"]!.GetValue<string>()))));
        var next = await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n01/07/2025;01/07/2025;MERCADONA;-20,00;\n"));
        next.EnsureSuccessStatusCode();
        Assert.Equal(3, (await client.Statements(id)).Max(s => s!["sequence"]!.GetValue<int>()));
    }

    private static BankTransactionRow Movement(Guid profileId, int import, int line, string date) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        LineKey = new string((char)('a' + import), 63) + line.ToString(CultureInfo.InvariantCulture),
        ImportSequence = import,
        LineNumber = line,
        BookingDate = Day(date),
        ValueDate = Day(date),
        Description = "MERCADONA",
        Amount = -1.00m,
    };

    private static DateOnly Day(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);
}
