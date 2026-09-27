using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestorIA.Api.Tests;

// The web dashboard's tests stub the API with these files, so each must be the API's own answer, not a hand-written guess.
// After a change to an answer or its shape, run these tests with GESTORIA_WRITE_WEB_FIXTURES=1 to rewrite the files.
public class WebFixtures(WebApplicationFactory<Program> api) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = api.CreateClient();

    [Fact]
    public async Task TheG14EstimateIsTheApisAnswerToG14()
    {
        await AssertFixture("g14-estimate.json", await client.Estimate(2025, RepoFiles.GoldenInput("G14")));
    }

    [Fact]
    public async Task TheTaxYearsAreTheApisList()
    {
        await AssertFixture("tax-years.json", await client.GetAsync("/api/v1/config/tax-years"));
    }

    private static async Task AssertFixture(string name, HttpResponseMessage response)
    {
        var path = RepoFiles.WebFixture(name);
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

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
