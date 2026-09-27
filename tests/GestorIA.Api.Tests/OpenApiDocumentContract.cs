using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace GestorIA.Api.Tests;

// The request schema is written from C# types that the endpoint never binds (SetAsideInputDocument), and the response schema
// from the types it serializes. These tests hold the committed document to what the API actually accepts and answers, so the
// web types generated from it cannot promise a shape the API does not keep.
public class OpenApiDocumentContract(ApiFactory api) : IClassFixture<ApiFactory>
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
        var unauthorized = await (await api.CreateClientWithoutKey().GetAsync("/api/v1/config/tax-years")).Json();

        AssertValid("HttpValidationProblemDetails", invalid);
        AssertValid("ProblemDetails", gap);
        AssertValid("ProblemDetails", unauthorized);
    }

    public static TheoryData<string> ProfileGoldens => new(RepoFiles.ProfileGoldens.Append("G15").Append("G17"));

    [Theory]
    [MemberData(nameof(ProfileGoldens))]
    public void TheDocumentedProfileAcceptsEveryGoldensTaxpayer(string golden)
    {
        AssertValid("ProfileInputDocument", RepoFiles.GoldenProfile(golden));
    }

    [Theory]
    [InlineData("previousYear", "{ \"rendimientoNeto\": \"8000.00\" }")]
    [InlineData("newActivity", "\"established\"")]
    [InlineData("newActivity", "{ \"kind\": \"started\", \"period\": \"first\" }")]
    public void TheDocumentedProfileRefusesAUnionTheApiRefuses(string field, string value)
    {
        var profile = RepoFiles.GoldenProfile("G15");
        profile["activity"]![field] = JsonNode.Parse(value);

        Assert.False(Evaluate("ProfileInputDocument", profile).IsValid);
    }

    // The one test of this class that stores a profile: the class shares one API and so one database.
    [Fact]
    public async Task TheProfileAnswersMatchTheirDocumentedSchemas()
    {
        var created = await client.PostProfile(RepoFiles.GoldenProfile("G16"));
        var profile = await created.Json();
        var id = profile["id"]!.GetValue<string>();
        var list = JsonNode.Parse(await client.GetStringAsync("/api/v1/profiles"))!.AsArray();
        var estimate = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1")).Json();
        var conflict = await (await client.PostProfile(RepoFiles.GoldenProfile("G16"))).Json();
        var notFound = await (await client.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}")).Json();
        var invalid = await (await client.PutProfile(id, new JsonObject { ["taxYear"] = 2025 })).Json();

        AssertValid("ProfileView", profile);
        Assert.All(list, item => AssertValid("ProfileView", item!));
        AssertValid("SetAsideEstimate", estimate);
        AssertValid("ProblemDetails", conflict);
        AssertValid("ProblemDetails", notFound);
        AssertValid("HttpValidationProblemDetails", invalid);
    }

    [Fact]
    public void EveryOperationButTheHealthChecksDeclaresTheApiKeyAndItsRefusal()
    {
        var document = JsonNode.Parse(File.ReadAllText(RepoFiles.OpenApiDocument))!;
        var scheme = document["components"]!["securitySchemes"]!["apiKey"]!;

        Assert.Equal(("apiKey", "header", ApiKey.Header), (scheme["type"]!.GetValue<string>(), scheme["in"]!.GetValue<string>(), scheme["name"]!.GetValue<string>()));
        foreach (var (path, methods) in document["paths"]!.AsObject())
        {
            var open = path.StartsWith("/api/v1/health/", StringComparison.Ordinal);
            foreach (var (method, operation) in methods!.AsObject())
            {
                var secured = operation!["security"]?.AsArray().Any(requirement => requirement!["apiKey"] is not null) ?? false;
                Assert.True(secured != open, $"{method} {path}: security declared {secured}");
                Assert.True(operation["responses"]!["401"] is null == open, $"{method} {path}: 401 documented {!open}");
            }
        }
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
