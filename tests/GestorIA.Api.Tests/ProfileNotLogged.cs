using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GestorIA.Api.Tests;

// SPEC-013 §2: nothing about the profile reaches a log. Every category is captured at Trace, EF Core's SQL included, while a
// profile is created, refused, read, replaced, estimated, exported, deleted and restored. No line may hold one of its amounts, and none may look
// like a NIF or an IBAN, the identifiers the ledger will add.
public partial class ProfileNotLogged
{
    // The profile's own figures, chosen to appear nowhere else: G15's shape with amounts of their own.
    private static readonly string[] Figures = ["43210.98", "2765.43", "31234.56", "17654.32", "987.65", "1234.56", "-876.54"];

    [Fact]
    public async Task NoLogLineHoldsAProfileValueOrAnIdentifier()
    {
        await using var api = new LoggedApi();
        await api.InitializeAsync();
        var client = api.CreateClient();
        var profile = RepoFiles.GoldenProfile("G15");
        profile["employment"]!["ingresos"] = Figures[0];
        profile["employment"]!["seguridadSocial"] = Figures[1];
        profile["activity"]!["previousYear"]!["rendimientoNeto"] = Figures[2];
        profile["projection"]!["ingresos"] = Figures[3];
        profile["projection"]!["gastos"] = Figures[4];
        profile["projection"]!["baseCotizacion"] = Figures[5];

        var id = (await (await client.PostProfile(profile)).Json())["id"]!.GetValue<string>();
        profile["activity"]!["previousYear"]!["rendimientoNeto"] = Figures[6];
        await client.PutProfile(id, profile);
        profile["projection"]!["baseCotizacion"] = "12.34";
        await client.PutProfile(id, profile);
        await client.GetAsync($"/api/v1/profiles/{id}");
        await client.GetAsync("/api/v1/profiles");
        await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q3");
        var export = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        await client.DeleteAsync($"/api/v1/profiles/{id}");
        Assert.Equal(HttpStatusCode.Created, (await client.Restore(export)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Restore(export)).StatusCode);
        var tampered = JsonNode.Parse(export)!;
        tampered["entities"]!["profiles"]![0]!["projection"]!["gastos"] = Figures[3];
        Assert.Equal(HttpStatusCode.Conflict, (await client.Restore(tampered.ToJsonString())).StatusCode);
        tampered["entities"]!["profiles"]![0]!["projection"]!["gastos"] = Figures[4] + "1";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Restore(tampered.ToJsonString())).StatusCode);
        await client.DeleteAsync($"/api/v1/profiles/{id}");

        Assert.NotEmpty(api.Lines);
        Assert.Contains(api.Lines, line => line.Contains("Profiles", StringComparison.Ordinal));
        foreach (var figure in Figures.Append("12.34"))
        {
            Assert.DoesNotContain(api.Lines, line => line.Contains(figure, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(api.Lines, line => Nif().IsMatch(line));
        Assert.DoesNotContain(api.Lines, line => Iban().IsMatch(line));
    }

    // [GeneratedRegex] has the compiler write the matcher at build time, into the body of a partial method it declares.
    // A DNI (eight digits and a letter), an NIE (X, Y or Z, seven digits and a letter) or a CIF (a letter, seven digits and a
    // check character); and an IBAN, two letters and two check digits before up to thirty letters and digits.
    [GeneratedRegex(@"\b([0-9]{8}[A-Z]|[XYZ][0-9]{7}[A-Z]|[ABCDEFGHJNPQRSUVW][0-9]{7}[0-9A-J])\b")]
    internal static partial Regex Nif();

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2} ?([A-Z0-9]{4} ?){2,7}[A-Z0-9]{1,4}\b")]
    internal static partial Regex Iban();
}
