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

    private readonly HttpClient client = api.CreateClient();

    [Fact]
    public async Task TheG12ProfileAndItsEstimateAreTheApisAnswers()
    {
        var created = await client.PostProfile(RepoFiles.GoldenProfile("G12"));
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

        await AssertFixture("g12-profile.json", await client.GetAsync($"/api/v1/profiles/{id}"), id);
        await AssertFixture("g12-estimate.json", await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1"));
    }

    [Fact]
    public async Task TheTaxYearsAreTheApisList()
    {
        await AssertFixture("tax-years.json", await client.GetAsync("/api/v1/config/tax-years"));
    }

    private static async Task AssertFixture(string name, HttpResponseMessage response, string? id = null)
    {
        var path = RepoFiles.WebFixture(name);
        var text = await response.Content.ReadAsStringAsync();
        var answer = JsonNode.Parse(id is null ? text : text.Replace(id, FixtureId, StringComparison.Ordinal))!;

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
