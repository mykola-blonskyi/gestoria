using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

// SPEC-013 §2 for bank statements: no description, amount or IBAN of a statement reaches a log. Every category is captured at
// Trace, EF Core's SQL included, while a statement is imported, imported again, refused for a bad line, and listed by filter, and
// while its export is restored, restored again and refused for a line key that does not fit.
public class TransactionsNotLogged
{
    [Fact]
    public async Task NoLogLineHoldsAMovementsDescriptionAmountOrIban()
    {
        await using var api = new LoggedApi();
        await api.InitializeAsync();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var text = Encoding.UTF8.GetString(RepoFiles.Statement);

        await client.ImportStatement(id, RepoFiles.Statement);
        await client.ImportStatement(id, RepoFiles.Statement);
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(text.Replace("-47,16", "-47,1x", StringComparison.Ordinal)));
        await client.GetAsync($"/api/v1/profiles/{id}/transactions");
        await client.GetAsync($"/api/v1/profiles/{id}/transactions?year=2025&quarter=Q1");
        var export = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        await client.DeleteAsync($"/api/v1/profiles/{id}");
        Assert.Equal(HttpStatusCode.Created, (await client.Restore(export)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Restore(export)).StatusCode);
        var tampered = JsonNode.Parse(export)!;
        var stored = tampered["entities"]!["bankTransactions"]!.AsArray();
        (stored[0]!["lineKey"], stored[1]!["lineKey"]) = (stored[1]!["lineKey"]!.DeepClone(), stored[0]!["lineKey"]!.DeepClone());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Restore(tampered.ToJsonString())).StatusCode);

        var movements = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Split(';')).ToList();
        var descriptions = movements.Select(fields => fields[2].Trim('"').Split(';')[0]);
        // Amounts of two digits or more before the decimal point, as printed and as the API writes them; "3.00" could be a timing.
        var amounts = movements.Select(fields => fields[^2])
            .Where(amount => amount.TrimStart('-').IndexOf(',', StringComparison.Ordinal) >= 2)
            .SelectMany(amount => new[] { amount, decimal.Parse(amount, NumberStyles.Number, new CultureInfo("es-ES")).ToString("0.00", CultureInfo.InvariantCulture) });

        Assert.Contains(api.Lines, line => line.Contains("BankTransactions", StringComparison.Ordinal));
        foreach (var secret in descriptions.Concat(amounts).Append("ES12"))
        {
            Assert.DoesNotContain(api.Lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(api.Lines, line => ProfileNotLogged.Nif().IsMatch(line));
        Assert.DoesNotContain(api.Lines, line => ProfileNotLogged.Iban().IsMatch(line));
    }
}
