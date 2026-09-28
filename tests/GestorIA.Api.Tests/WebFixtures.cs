using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

// The web app's feature tests stub the API with these files, so each must be the API's own answer, not a hand-written guess.
// After a change to an answer or its shape, run these tests with GESTORIA_WRITE_WEB_FIXTURES=1 to rewrite the files.
public class WebFixtures(ApiFactory api) : IClassFixture<ApiFactory>
{
    // A stored profile's id is new on every run; the fixture carries this one in its place.
    private const string FixtureId = "00000000-0000-0000-0000-000000000069";

    // An export's time is new on every run too.
    private const string FixtureExportedAt = "2026-09-28T09:00:00+00:00";

    private readonly HttpClient client = api.CreateClient();

    [Fact]
    public async Task TheG12ProfileAndItsEstimateAreTheApisAnswers()
    {
        var created = await client.PostProfile(RepoFiles.GoldenProfile("G12"));
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

        await AssertFixture("g12-profile.json", await client.GetAsync($"/api/v1/profiles/{id}"), [(id, FixtureId)]);
        await AssertFixture("g12-estimate.json", await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1"));
        await AssertFixture("g12-quarter.json", await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter?quarter=Q1", null));
        await AssertFixture("g12-annual-true-up.json", await client.PostAsync($"/api/v1/profiles/{id}/calculations/annual-true-up", null));
        await AssertFixture("g12-calendar.json", await client.GetAsync($"/api/v1/profiles/{id}/calendar"));
    }

    // G12 with its projection's ingresos and gastos swapped: a quarter with far more spent than earned, so casilla 19
    // (resultado) comes out negative. A negative casilla 19 is never paid; it carries to later quarters instead
    // (Modelo130Carry.NegativosPendientes), so the web app must never call it due (#71 fix-forward).
    [Fact]
    public async Task ANegativeQuarterIsTheApisAnswer()
    {
        await using var own = new ApiFactory();
        await own.InitializeAsync();
        var client = own.CreateClient();

        var negative = RepoFiles.GoldenProfile("G12").DeepClone().AsObject();
        negative["projection"]!["ingresos"] = "5000.00";
        negative["projection"]!["gastos"] = "40000.00";
        var created = await client.PostProfile(negative);
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

        await AssertFixture("g12-quarter-negative.json", await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter?quarter=Q1", null));
    }

    // A new database per run gives each movement a new id; the fixture numbers them in order instead.
    [Fact]
    public async Task TheSyntheticStatementsImportAndMovementsAreTheApisAnswers()
    {
        await using var own = new ApiFactory();
        await own.InitializeAsync();
        var client = own.CreateClient();
        var id = await client.CreateProfile();

        await AssertFixture("g12-statement-import.json", await client.ImportStatement(id, RepoFiles.Statement));
        var year = await client.GetAsync($"/api/v1/profiles/{id}/transactions?year=2025");
        var ids = JsonNode.Parse(await year.Content.ReadAsStringAsync())!.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToList();
        var fixtureIds = ids.Select((movement, index) => (movement, $"00000000-0000-0000-0072-{index + 1:D12}")).Append((id, FixtureId)).ToList();
        await AssertFixture("g12-transactions-2025.json", year, fixtureIds);
        await AssertFixture("g12-transactions-2025-q1.json", await client.GetAsync($"/api/v1/profiles/{id}/transactions?year=2025&quarter=Q1"), fixtureIds);
        var export = await client.GetAsync($"/api/v1/profiles/{id}/export");
        var file = await export.Content.ReadAsStringAsync();
        await AssertFixture("g12-export.json", export, fixtureIds);

        await client.DeleteAsync($"/api/v1/profiles/{id}");
        await AssertFixture("g12-restore.json", await client.Restore(file), fixtureIds);
        var changed = JsonNode.Parse(file)!;
        changed["entities"]!["profiles"]![0]!["projection"]!["gastos"] = "1.00";
        await AssertFixture("g12-restore-conflict.json", await client.Restore(changed.ToJsonString()), fixtureIds);
        await AssertFixture("g12-review-queue.json", await client.GetAsync($"/api/v1/profiles/{id}/review-queue"), fixtureIds);

        await client.ClassifySyntheticQueue(id);
        await AssertFixture("g12-estimate-ledger.json", await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2"), fixtureIds);
    }

    [Fact]
    public async Task TheTaxYearsAreTheApisList()
    {
        await AssertFixture("tax-years.json", await client.GetAsync("/api/v1/config/tax-years"));
    }

    private static async Task AssertFixture(string name, HttpResponseMessage response, IEnumerable<(string Id, string FixtureId)>? ids = null)
    {
        var text = (ids ?? []).Aggregate(await response.Content.ReadAsStringAsync(), (answer, id) => answer.Replace(id.Id, id.FixtureId, StringComparison.Ordinal));
        var answer = JsonNode.Parse(text)!;
        if (answer is JsonObject export && export.ContainsKey("exportedAt"))
        {
            export["exportedAt"] = FixtureExportedAt;
        }

        // A problem's traceId is new on every request.
        (answer as JsonObject)?.Remove("traceId");

        AssertFixture(name, answer);
    }

    internal static void AssertFixture(string name, JsonNode answer)
    {
        var path = RepoFiles.WebFixture(name);
        if (Environment.GetEnvironmentVariable("GESTORIA_WRITE_WEB_FIXTURES") == "1")
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(path, answer.ToJsonString(options) + "\n");
        }

        Assert.True(
            JsonNode.DeepEquals(answer, JsonNode.Parse(File.ReadAllText(path))),
            $"{path} is not the API's answer; rerun with GESTORIA_WRITE_WEB_FIXTURES=1 to rewrite it.");
    }
}
