using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestorIA.Api.Transactions;
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
        var export = await (await client.GetAsync($"/api/v1/profiles/{id}/export")).Json();
        var noDelete = await (await client.DeleteAsync($"/api/v1/profiles/{Guid.NewGuid()}")).Json();
        var same = await client.Restore(export.ToJsonString());
        var changed = export.DeepClone();
        changed["entities"]!["profiles"]![0]!["projection"]!["gastos"] = "1.00";
        var notEmpty = await client.Restore(changed.ToJsonString());
        changed["entities"]!["profiles"]![0]!["projection"]!["gastos"] = "1.001";
        var refused = await (await client.Restore(changed.ToJsonString())).Json();

        AssertValid("ProfileView", profile);
        Assert.All(list, item => AssertValid("ProfileView", item!));
        AssertValid("SetAsideEstimate", estimate);
        AssertValid("ProblemDetails", conflict);
        AssertValid("ProblemDetails", notFound);
        AssertValid("HttpValidationProblemDetails", invalid);
        AssertValid("ProfileExport", export);
        AssertValid("ProblemDetails", noDelete);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (same.StatusCode, notEmpty.StatusCode));
        AssertValid("RestoredExport", await same.Json());
        AssertValid("ProblemDetails", await notEmpty.Json());
        AssertValid("HttpValidationProblemDetails", refused);
    }

    [Fact]
    public async Task TheTransactionAnswersMatchTheirDocumentedSchemas()
    {
        await using var own = new ApiFactory();
        await own.InitializeAsync();
        var ownClient = own.CreateClient();
        var id = await ownClient.CreateProfile();

        var imported = await (await ownClient.ImportStatement(id, RepoFiles.Statement)).Json();
        var movements = JsonNode.Parse(await ownClient.GetStringAsync($"/api/v1/profiles/{id}/transactions?year=2025&quarter=Q1"))!.AsArray();
        var badLine = await (await ownClient.ImportStatement(id, "Fecha;Fecha Valor;Concepto;Importe;Saldo\nx"u8.ToArray())).Json();
        var badFilter = await (await ownClient.GetAsync($"/api/v1/profiles/{id}/transactions?quarter=Q1")).Json();
        var tooLarge = await (await ownClient.ImportStatement(id, new byte[StatementFile.MaxBytes + 1])).Json();
        var wrongType = await (await ownClient.ImportStatement(id, RepoFiles.Statement, contentType: "application/json")).Json();
        var exportTooLarge = await (await ownClient.Restore($"\"{new string('x', Profiles.ProfileRestore.MaxBytes)}\"")).Json();
        var exportWrongType = await (await ownClient.Restore("{}", "text/plain")).Json();
        var queue = await ownClient.ReviewQueue(id);
        await ownClient.ClassifyAs(queue[0]!["id"]!.GetValue<string>(), "activityIncome");
        var suggested = await ownClient.ImportStatement(id, "Fecha;Fecha Valor;Concepto;Importe;Saldo\n03/02/2025;03/02/2025;GITHUB INC;-4,00;\n"u8.ToArray());
        var withSuggestion = await ownClient.ReviewQueue(id);
        var estimate = await (await ownClient.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        var badClass = await (await ownClient.ClassifyAs(queue[1]!["id"]!.GetValue<string>(), "unclear")).Json();
        var noLine = await (await ownClient.ClassifyAs(Guid.NewGuid().ToString(), "personal")).Json();

        AssertValid("BankStatementImport", imported);
        Assert.NotEmpty(movements);
        Assert.All(movements, movement => AssertValid("TransactionView", movement!));
        AssertValid("HttpValidationProblemDetails", badLine);
        AssertValid("HttpValidationProblemDetails", badFilter);
        AssertValid("ProblemDetails", tooLarge);
        AssertValid("ProblemDetails", wrongType);
        AssertValid("ProblemDetails", exportTooLarge);
        AssertValid("ProblemDetails", exportWrongType);
        Assert.Equal(HttpStatusCode.OK, suggested.StatusCode);
        Assert.Contains(withSuggestion, item => item!["suggestion"] is not null);
        Assert.All(withSuggestion, item => AssertValid("ReviewItem", item!));
        Assert.NotNull(estimate["ledger"]!["actualsThrough"]);
        AssertValid("SetAsideEstimate", estimate);
        AssertValid("HttpValidationProblemDetails", badClass);
        AssertValid("ProblemDetails", noLine);
    }

    [Theory]
    [InlineData("activityIncome", true)]
    [InlineData("unclear", false)]
    public void TheDocumentedClassificationAcceptsTheClassesTheApiAccepts(string transactionClass, bool valid)
    {
        Assert.Equal(valid, Evaluate("TransactionClassification", new JsonObject { ["class"] = transactionClass }).IsValid);
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
