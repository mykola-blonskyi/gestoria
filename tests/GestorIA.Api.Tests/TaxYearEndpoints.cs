using System.Net;
using System.Text.Json.Nodes;
using GestorIA.Infrastructure.TaxYears;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestorIA.Api.Tests;

public class TaxYearEndpoints(WebApplicationFactory<Program> api) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = api.CreateClient();

    [Fact]
    public async Task TheListHasEveryYearOfConfigTaxYearsWithItsHash()
    {
        var response = await client.GetAsync("/api/v1/config/tax-years");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Equal([2025, 2026], list.Select(y => y!["taxYear"]!.GetValue<int>()));
        Assert.Equal(Hash(2025), list[0]!["configHash"]!.GetValue<string>());
        Assert.Equal(Hash(2026), list[1]!["configHash"]!.GetValue<string>());
    }

    [Fact]
    public async Task AYearNamesItsRegionsAndNoGapWhenItDeclaresNone()
    {
        var year = await (await client.GetAsync("/api/v1/config/tax-years/2025")).Json();

        Assert.Equal(2025, year["taxYear"]!.GetValue<int>());
        Assert.Equal(["MD", "VC"], year["regions"]!.AsArray().Select(r => r!["code"]!.GetValue<string>()));
        Assert.Empty(year["gaps"]!.AsArray());
    }

    [Fact]
    public async Task A2026GapIsListedWithTheFilesOwnNote()
    {
        var config = new TaxYearConfigLoader(RepoFiles.ConfigDirectory).Load(2026);

        var year = await (await client.GetAsync("/api/v1/config/tax-years/2026")).Json();

        var gaps = year["gaps"]!.AsArray().ToDictionary(g => g!["entry"]!.GetValue<string>(), g => g!["note"]!.GetValue<string>());
        Assert.Equal(config.SeguridadSocial.TarifaPlana.DeclaredIncomplete, gaps["seguridadSocial.tarifaPlana"]);
        Assert.Equal(config.Calendar.DeclaredIncomplete, gaps["calendar"]);
    }

    [Fact]
    public async Task AYearWithNoFileIsAProblemNotFound()
    {
        var response = await client.GetAsync("/api/v1/config/tax-years/1999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/tax-year-not-found", problem["type"]!.GetValue<string>());
        Assert.Contains("1999.json", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private static string Hash(int year) => new TaxYearConfigLoader(RepoFiles.ConfigDirectory).Load(year).ConfigHash;
}
