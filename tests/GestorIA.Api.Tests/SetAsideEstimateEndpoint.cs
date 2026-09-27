using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestorIA.Api.Tests;

public class SetAsideEstimateEndpoint(WebApplicationFactory<Program> api) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = api.CreateClient();

    public static TheoryData<string> Goldens => new(RepoFiles.SetAsideGoldens);

    [Theory]
    [MemberData(nameof(Goldens))]
    public async Task AGoldensInputFileGetsItsExpectedEstimate(string golden)
    {
        var expected = RepoFiles.Golden(golden)["expected"]!;

        var response = await client.Estimate(2025, RepoFiles.GoldenInput(golden));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estimate = await response.Json();
        Assert.Equal(2025, estimate["taxYear"]!.GetValue<int>());
        Assert.Equal(expected["holdBackShare"]!.GetValue<string>(), estimate["holdBackShare"]!.GetValue<string>());
        Assert.Equal(expected["monthlyCuotaSs"]!.GetValue<string>(), estimate["monthlyCuotaSs"]!.GetValue<string>());
        Assert.Equal(expected["annualTrueUpGap"]!.GetValue<string>(), estimate["annualTrueUpGap"]!.GetValue<string>());
        Assert.Equal(expected["annualTrueUpPayableIn"]!.GetValue<string>(), estimate["annualTrueUpPayableIn"]!.GetValue<string>());
        Assert.Equal(expected["ivaToSetAside"]!.GetValue<string>(), estimate["ivaToSetAside"]!.GetValue<string>());

        var next = estimate["nextPayment"]!;
        var want = expected["nextPayment"]!;
        Assert.Equal(want["quarter"]!.GetValue<string>(), next["quarter"]!.GetValue<string>());
        Assert.Equal(want["aIngresar"]!.GetValue<string>(), next["aIngresar"]!.GetValue<string>());
        Assert.Equal(
            want["dueWindow"]!.AsArray().Select(d => d!.GetValue<string>()),
            [next["dueFrom"]!.GetValue<string>(), next["dueBy"]!.GetValue<string>()]);
        Assert.Equal(
            expected["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Order(),
            estimate["notices"]!.AsArray().Select(n => n!["code"]!.GetValue<string>()).Order());
    }

    [Fact]
    public async Task TheTraceKeepsEachStepsKindAndTheEnginesOrder()
    {
        var estimate = await (await client.Estimate(2025, RepoFiles.GoldenInput("G14"))).Json();

        var trace = estimate["trace"]!.AsArray();
        var output = trace.ToDictionary(s => s!["id"]!.GetValue<string>(), s => s!["output"]!);
        Assert.Equal("set-aside.months-of-activity", trace[0]!["id"]!.GetValue<string>());
        Assert.Equal("set-aside.hold-back-share", trace[^1]!["id"]!.GetValue<string>());
        Assert.Equal(("count", "9"), Output(output["set-aside.projected-months"]));
        Assert.Equal(("money", "925.33"), Output(output["set-aside.cuota-ss-year"]));
        Assert.Equal(("money", "30408.27"), Output(output["set-aside.rendimiento-computable"]));
        Assert.Equal(("rate", "0.1947"), Output(output["set-aside.hold-back-share"]));
        Assert.Equal(("date", "2025-07-21"), Output(output["Q2.m130.due-date"]));
    }

    [Fact]
    public async Task AnInvalidFieldIsAValidationProblemKeyedByItsJsonPath()
    {
        var input = RepoFiles.GoldenInput("G14");
        input["activity"]!["projection"]!["gastos"] = "-900.00";

        var response = await client.Estimate(2025, input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/invalid-input", problem["type"]!.GetValue<string>());
        var message = "$.activity.projection.gastos is \"-900.00\"; it must be zero or more.";
        Assert.Equal(message, problem["detail"]!.GetValue<string>());
        Assert.Equal([message], problem["errors"]!["$.activity.projection.gastos"]!.AsArray().Select(e => e!.GetValue<string>()));
    }

    [Theory]
    [InlineData("{ \"asOf\": \"Q2\", \"asOf\": \"Q3\" }", "$")]
    [InlineData("not json", "$")]
    [InlineData("{ \"asOf\": \"Q2\", \"profile\": {}, \"activity\": {}, \"extra\": 1 }", "$.extra")]
    public async Task ABodyTheConsoleWouldRefuseIsRefusedTheSameWay(string body, string path)
    {
        var response = await client.Estimate(2025, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Json())["errors"]!.AsObject();
        Assert.Equal([path], errors.Select(e => e.Key));
    }

    [Fact]
    public async Task ADeclaredGapIsA422ConfigGapQuotingTheFilesNote()
    {
        var input = RepoFiles.GoldenInput("G14");
        input["profile"]!["activity"]!["alta"] = "2026-01-15";

        var response = await client.Estimate(2026, input);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/config-gap", problem["type"]!.GetValue<string>());
        Assert.Equal(422, problem["status"]!.GetValue<int>());
        Assert.Contains("seguridadSocial.tarifaPlana.amount", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATaxYearWithNoConfigurationIsA422ConfigGap()
    {
        var response = await client.Estimate(1999, RepoFiles.GoldenInput("G14"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/config-gap", problem["type"]!.GetValue<string>());
        Assert.Contains("1999.json", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInputOnlyTheEngineCanJudgeIsA422WithTheEnginesReason()
    {
        var input = RepoFiles.GoldenInput("G14");
        input["activity"]!["actuals"]![0]!["quarter"] = "Q2";

        var response = await client.Estimate(2025, input);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/estimate-refused", problem["type"]!.GetValue<string>());
        Assert.Equal("Actuals are the closed quarters in order from Q1, the first quarter of activity; got [Q2].", problem["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task AMissingTaxYearIsABadRequestProblem()
    {
        var response = await client.PostAsync("/api/v1/set-aside/estimate", new StringContent(RepoFiles.GoldenInput("G14").ToJsonString(), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
    }

    private static (string Kind, string Value) Output(JsonNode output) => (output["kind"]!.GetValue<string>(), output["value"]!.GetValue<string>());
}
