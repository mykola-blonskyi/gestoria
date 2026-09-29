using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Api.Tests;

// The migration that records statement imports (#88), run on an installation holding movements from before it. Each import
// that stored movements gets the period #73 read from them, so the estimate is the same after the upgrade as before. The
// imports are numbered 1 to n in their order, an import before #88 that stored nothing having left a number unused, so the
// next import takes the next number and the installation's export restores.
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
                Movement(profile.Id, 3, 1, "2025-04-07"),
                Movement(profile.Id, 3, 2, "2025-06-30"));
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

        Assert.Equal([(1, Day("2025-01-02"), Day("2025-03-31")), (3, Day("2025-04-07"), Day("2025-06-30"))], spans);
        Assert.Equal(
            spans.Select((span, index) => (index + 1, span.From, span.To)),
            statements.Select(s => (s!["sequence"]!.GetValue<int>(), Day(s["from"]!.GetValue<string>()), Day(s["to"]!.GetValue<string>()))));
        var next = await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n01/07/2025;01/07/2025;MERCADONA;-20,00;\n"));
        next.EnsureSuccessStatusCode();
        Assert.Equal(3, (await client.Statements(id)).Max(s => s!["sequence"]!.GetValue<int>()));
        var movements = JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}/transactions"))!.AsArray();
        Assert.Equal(["2025-01-02", "2025-02-14", "2025-03-31", "2025-04-07", "2025-06-30", "2025-07-01"], movements.Select(m => m!["bookingDate"]!.GetValue<string>()));

        var export = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        (await client.DeleteAsync($"/api/v1/profiles/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(System.Net.HttpStatusCode.Created, (await client.Restore(export)).StatusCode);
    }

    // Keyed as an import keys a line of its kind, so the export the test restores passes the restore's key check.
    private static BankTransactionRow Movement(Guid profileId, int import, int line, string date) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        LineKey = LineKeys.Of([new StatementLine(line, new BankTransaction(Day(date), Day(date), "MERCADONA", new Money(-1.00m), null))])[0].Key,
        ImportSequence = import,
        LineNumber = line,
        BookingDate = Day(date),
        ValueDate = Day(date),
        Description = "MERCADONA",
        Amount = -1.00m,
    };

    private static DateOnly Day(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);
}
