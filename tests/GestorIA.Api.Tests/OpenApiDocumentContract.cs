using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestorIA.Api.Tests;

// The request schema is written from C# types that the endpoint never binds (SetAsideInputDocument), and the response schema
// from the types it serializes. These tests hold the committed document to what the API actually accepts and answers, so the
// web types generated from it cannot promise a shape the API does not keep.
public class OpenApiDocumentContract(WebApplicationFactory<Program> api) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = api.CreateClient();

    public static TheoryData<string> Goldens => new(RepoFiles.SetAsideGoldens);

    [Theory]
    [MemberData(nameof(Goldens))]
    public void TheDocumentedRequestAcceptsEveryGoldensInputFile(string golden)
    {
        AssertValid("SetAsideInputDocument", RepoFiles.GoldenInput(golden));
    }

    [Theory]
    [InlineData("activity.projection", "gastoz")]
    [InlineData("profile.activity.newActivity", "periodo")]
    public void TheDocumentedRequestRefusesAFieldTheParserRefuses(string parent, string unknown)
    {
        var input = RepoFiles.GoldenInput("G14");
        var node = parent.Split('.').Aggregate((JsonNode)input, (at, name) => at[name]!);
        node[unknown] = "1.00";

        Assert.False(Evaluate("SetAsideInputDocument", input).IsValid);
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public async Task EveryEstimateMatchesTheDocumentedResponse(string golden)
    {
        var estimate = await (await client.Estimate(2025, RepoFiles.GoldenInput(golden))).Json();

        AssertValid("SetAsideEstimate", estimate);
    }

    [Fact]
    public async Task TheProblemsMatchTheirDocumentedSchemas()
    {
        var input = RepoFiles.GoldenInput("G14");
        input["asOf"] = "Q5";
        var invalid = await (await client.Estimate(2025, input)).Json();
        var gap = await (await client.Estimate(1999, RepoFiles.GoldenInput("G14"))).Json();

        AssertValid("HttpValidationProblemDetails", invalid);
        AssertValid("ProblemDetails", gap);
    }

    [Fact]
    public async Task TheTaxYearsMatchTheDocumentedResponse()
    {
        var years = JsonNode.Parse(await client.GetStringAsync("/api/v1/config/tax-years"))!.AsArray();

        Assert.All(years, year => AssertValid("TaxYearView", year!));
    }

    private static void AssertValid(string component, JsonNode instance)
    {
        var result = Evaluate(component, instance);
        Assert.True(result.IsValid, $"{component}: {string.Join("; ", Failures(result))}");
    }

    // JsonSchema.Net registers every schema it builds under its $id, once per process, so each is built once and kept.
    private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> Schemas = new();

    private static EvaluationResults Evaluate(string component, JsonNode instance) =>
        Schemas.GetOrAdd(component, name => new Lazy<JsonSchema>(() => Schema(name))).Value.Evaluate(JsonSerializer.SerializeToElement(instance), new EvaluationOptions { OutputFormat = OutputFormat.List });

    // OpenAPI 3.1 schemas are JSON Schema 2020-12. The components move under $defs, where any JSON Schema validator
    // resolves references, and the root refers to the one component under test.
    private static JsonSchema Schema(string component)
    {
        var document = File.ReadAllText(RepoFiles.OpenApiDocument).Replace("#/components/schemas/", "#/$defs/", StringComparison.Ordinal);
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"https://gestoria.local/openapi/{component}",
            ["$ref"] = $"#/$defs/{component}",
            ["$defs"] = JsonNode.Parse(document)!["components"]!["schemas"]!.DeepClone(),
        };

        return JsonSchema.FromText(root.ToJsonString());
    }

    private static IEnumerable<string> Failures(EvaluationResults result) =>
        (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} {e.Key}: {e.Value}"));
}
