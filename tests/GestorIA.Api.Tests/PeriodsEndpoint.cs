using System.Net;
using System.Text.Json.Nodes;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Api.Tests;

// The two calculations endpoints against a real PostgreSQL (TestDatabase): opening one quarter or the annual true-up of a
// stored profile (#71). Each test starts an API on a database of its own, as ProfilesEndpoint does.
public class PeriodsEndpoint
{
    private const string ProfileNotFound = "https://gestoria.local/problems/profile-not-found";

    [Fact]
    public async Task AStoredProfilesQuarterHasItsAIngresarDueWindowCasillasAndTraceAndTheConfigsHash()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();
        var config = new TaxYearConfigLoader(RepoFiles.ConfigDirectory).Load(2025);

        var response = await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter?quarter=Q1", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Json();
        Assert.Equal("Q1", body["quarter"]!.GetValue<string>());
        Assert.Equal(config.ConfigHash, body["configHash"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(body["aIngresar"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(body["dueFrom"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(body["dueBy"]!.GetValue<string>()));
        Assert.NotEmpty(body["casillas"]!.AsArray());
        Assert.NotEmpty(body["trace"]!.AsArray());
        Assert.Equal(
            config.Modelo130.Lines.Keys.ToHashSet(),
            body["casillas"]!.AsArray().Select(c => c!["key"]!.GetValue<string>()).ToHashSet());
    }

    [Fact]
    public async Task TheAnnualTrueUpMatchesTheSetAsideEstimatesGapAndPayableIn()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var estimate = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1")).Json();
        var response = await client.PostAsync($"/api/v1/profiles/{id}/calculations/annual-true-up", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trueUp = await response.Json();
        Assert.Equal(estimate["annualTrueUpGap"]!.GetValue<string>(), trueUp["gap"]!.GetValue<string>());
        Assert.Equal(estimate["annualTrueUpPayableIn"]!.GetValue<string>(), trueUp["payableIn"]!.GetValue<string>());
        Assert.NotEmpty(trueUp["trace"]!.AsArray());
    }

    // A period runs on the actuals the estimate runs on (#73): with the synthetic statement imported and nothing reviewed every
    // closed quarter stays projected, and once it is reviewed the classified movements are Q1's and Q2's actuals. Either way the
    // quarter's Modelo 130 is the estimate's next payment for that quarter, the true-up is the estimate's, and both traces start
    // with the ledger's steps.
    [Fact]
    public async Task APeriodRunsOnTheClassifiedMovementsAsTheEstimateDoes()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, RepoFiles.Statement);

        var unreviewed = await Periods(client, id);
        Assert.Equal(["ledger.pending-review"], LedgerSteps(unreviewed.Quarter));
        Assert.Equal(["ledger.pending-review"], LedgerSteps(unreviewed.TrueUp));
        Assert.Null(unreviewed.Quarter["ledger"]!["actualsThrough"]);
        Assert.Equal(11, unreviewed.TrueUp["ledger"]!["awaitingReview"]!.GetValue<int>());

        await client.ClassifySyntheticQueue(id);
        var reviewed = await Periods(client, id);
        Assert.Equal(["ledger.Q1.movements", "ledger.Q2.movements", "ledger.projection-remaining"], LedgerSteps(reviewed.Quarter));
        Assert.Equal(["ledger.Q1.movements", "ledger.Q2.movements", "ledger.Q3.movements", "ledger.Q4.movements", "ledger.projection-remaining"], LedgerSteps(reviewed.TrueUp));
        Assert.Equal("Q2", reviewed.Quarter["ledger"]!["actualsThrough"]!.GetValue<string>());
        Assert.Equal("Q4", reviewed.TrueUp["ledger"]!["actualsThrough"]!.GetValue<string>());
        Assert.NotEqual(unreviewed.Quarter["aIngresar"]!.GetValue<string>(), reviewed.Quarter["aIngresar"]!.GetValue<string>());
    }

    // The payments calendar reads the whole year at asOf Q4, a quarter's page at asOf Qn. Each quarter's Modelo 130 is the same
    // in both, with no movements, with every movement reviewed, and with only Q1's reviewed (Q2 holding the rest back).
    [Theory]
    [InlineData("none")]
    [InlineData("reviewed")]
    [InlineData("q1Reviewed")]
    public async Task TheCalendarsModelo130IsEachQuartersOwn(string ledger)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        if (ledger != "none")
        {
            await client.ImportStatement(id, RepoFiles.Statement);
        }

        if (ledger == "reviewed")
        {
            await client.ClassifySyntheticQueue(id);
        }
        else if (ledger == "q1Reviewed")
        {
            foreach (var item in (await client.ReviewQueue(id)).Where(item => item!["bookingDate"]!.GetValue<string>().CompareTo("2025-04-01") < 0))
            {
                await client.ClassifyAs(item!["id"]!.GetValue<string>(), item["amount"]!.GetValue<string>().StartsWith('-') ? "personal" : "activityIncome");
            }
        }

        var calendar = (await (await client.GetAsync($"/api/v1/profiles/{id}/calendar")).Json())["obligations"]!.AsArray();
        foreach (var quarter in new[] { "Q1", "Q2", "Q3", "Q4" })
        {
            var own = await (await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter?quarter={quarter}", null)).Json();
            var listed = calendar.Single(o => o!["kind"]!.GetValue<string>() == "Modelo130" && o["period"]!.GetValue<string>() == quarter)!;
            Assert.Equal(own["aIngresar"]!.GetValue<string>(), listed["amount"]!["euros"]!.GetValue<string>());
        }
    }

    private static async Task<(JsonObject Quarter, JsonObject TrueUp)> Periods(HttpClient client, string id)
    {
        var quarter = await (await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter?quarter=Q2", null)).Json();
        var trueUp = await (await client.PostAsync($"/api/v1/profiles/{id}/calculations/annual-true-up", null)).Json();
        var q2 = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        var q4 = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q4")).Json();
        Assert.Equal(q2["nextPayment"]!["aIngresar"]!.GetValue<string>(), quarter["aIngresar"]!.GetValue<string>());
        Assert.Equal(q4["annualTrueUpGap"]!.GetValue<string>(), trueUp["gap"]!.GetValue<string>());
        return (quarter, trueUp);
    }

    private static List<string> LedgerSteps(JsonObject period) =>
        [.. period["trace"]!.AsArray().Select(step => step!["id"]!.GetValue<string>()).TakeWhile(id => id.StartsWith("ledger.", StringComparison.Ordinal))];

    [Theory]
    [InlineData("/calculations/quarter?quarter=Q1")]
    [InlineData("/calculations/annual-true-up")]
    public async Task AnUnknownProfileIsA404(string suffix)
    {
        await using var api = await Api();

        var response = await api.CreateClient().PostAsync($"/api/v1/profiles/{Guid.NewGuid()}{suffix}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProfileNotFound, (await response.Json())["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("", "quarter is missing; it must be the quarter to open, one of Q1, Q2, Q3, Q4.")]
    [InlineData("?quarter=Q5", "quarter is \"Q5\"; it must be the quarter to open, one of Q1, Q2, Q3, Q4.")]
    [InlineData("?quarter=1", "quarter is \"1\"; it must be the quarter to open, one of Q1, Q2, Q3, Q4.")]
    [InlineData("?quarter=q1", "quarter is \"q1\"; it must be the quarter to open, one of Q1, Q2, Q3, Q4.")]
    public async Task AMissingOrMalformedQuarterIsAnInvalidInputProblemKeyedByTheParameter(string query, string message)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter{query}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], (await response.Json())["errors"]!["quarter"]!.AsArray().Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public async Task AQuarterBeforeTheAltaIsA422WithTheEnginesReason()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var profile = RepoFiles.GoldenProfile("G12");
        profile["activity"]!["alta"] = "2025-08-01";
        var id = (await (await client.PostProfile(profile)).Json())["id"]!.GetValue<string>();

        var response = await client.PostAsync($"/api/v1/profiles/{id}/calculations/quarter?quarter=Q1", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/estimate-refused", problem["type"]!.GetValue<string>());
        Assert.Equal("Quarter Q1 of 2025 ends in 2025-03, before the alta month 2025-08 (alta 2025-08-01).", problem["detail"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("/calculations/quarter?quarter=Q1")]
    [InlineData("/calculations/annual-true-up")]
    public async Task A2026ProfileIsStoredAndItsCalculationsAreA422ConfigGapWhileTheRentaWindowIsUnpublished(string suffix)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var profile = RepoFiles.GoldenProfile("G16");
        profile["taxYear"] = 2026;
        var created = await client.PostProfile(profile);
        var id = (await created.Json())["id"]!.GetValue<string>();

        var response = await client.PostAsync($"/api/v1/profiles/{id}{suffix}", null);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/config-gap", problem["type"]!.GetValue<string>());
        Assert.Contains("calendar.", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }
}
