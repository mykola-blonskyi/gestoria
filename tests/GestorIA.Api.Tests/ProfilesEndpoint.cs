using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

// The profile endpoints against a real PostgreSQL (TestDatabase). Local mode keeps one profile per installation, so each test
// starts an API on a database of its own rather than share a class fixture's.
public class ProfilesEndpoint
{
    private const string ProfileExists = "https://gestoria.local/problems/profile-exists";
    private const string ProfileNotFound = "https://gestoria.local/problems/profile-not-found";

    public static TheoryData<string> Goldens => new(RepoFiles.ProfileGoldens);

    [Fact]
    public async Task BeforeTheFirstProfileTheListIsEmpty()
    {
        await using var api = await Api();

        var list = JsonNode.Parse(await api.CreateClient().GetStringAsync("/api/v1/profiles"))!.AsArray();

        Assert.Empty(list);
    }

    [Fact]
    public async Task ACreatedProfileIsAnsweredWithItsIdAndReadBackAsSent()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var sent = RepoFiles.GoldenProfile("G15");

        var response = await client.PostProfile(sent);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Json();
        var id = created["id"]!.GetValue<string>();
        Assert.Equal($"/api/v1/profiles/{id}", response.Headers.Location!.OriginalString);
        Assert.True(JsonNode.DeepEquals(sent, WithoutId(created)), created.ToJsonString());
        Assert.True(JsonNode.DeepEquals(created, JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}"))));
        Assert.True(JsonNode.DeepEquals(new JsonArray(created.DeepClone()), JsonNode.Parse(await client.GetStringAsync("/api/v1/profiles"))));
    }

    [Fact]
    public async Task TheProfileOutlivesTheApi()
    {
        string id;
        string connectionString;
        await using (var first = await Api())
        {
            id = (await (await first.CreateClient().PostProfile(RepoFiles.GoldenProfile("G17"))).Json())["id"]!.GetValue<string>();
            connectionString = first.ConnectionString;
        }

        await using var restarted = new ApiFactory(connectionString);
        var stored = JsonNode.Parse(await restarted.CreateClient().GetStringAsync($"/api/v1/profiles/{id}"))!;

        Assert.True(JsonNode.DeepEquals(RepoFiles.GoldenProfile("G17"), WithoutId(stored.AsObject())));
    }

    [Fact]
    public async Task APutReplacesEveryField()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();
        var replacement = RepoFiles.GoldenProfile("G15");
        replacement["activity"]!["previousYear"]!["rendimientoNeto"] = "-1250.50";

        var response = await client.PutProfile(id, replacement);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}"))!.AsObject();
        Assert.Equal(id, stored["id"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(replacement, WithoutId(stored)), stored.ToJsonString());
    }

    [Fact]
    public async Task ASecondProfileIsRefusedNamingTheFirst()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.PostProfile(RepoFiles.GoldenProfile("G15"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        var problem = await response.Json();
        Assert.Equal(ProfileExists, problem["type"]!.GetValue<string>());
        Assert.Contains(id, problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Single(JsonNode.Parse(await client.GetStringAsync("/api/v1/profiles"))!.AsArray());
    }

    // The database holds the rule, not the handler's look-before-insert: of creates racing on an empty installation exactly
    // one is stored, and every other one is the same 409 as a create that comes later.
    [Fact]
    public async Task OfConcurrentCreatesExactlyOneIsStored()
    {
        await using var api = await Api();
        var client = api.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.PostProfile(RepoFiles.GoldenProfile("G12"))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal(ProfileExists, (await conflict.Json())["type"]!.GetValue<string>());
        }
        Assert.Single(JsonNode.Parse(await client.GetStringAsync("/api/v1/profiles"))!.AsArray());
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("GET", "/set-aside/estimate?asOf=Q1")]
    public async Task AnUnknownProfileIsA404(string method, string suffix)
    {
        await using var api = await Api();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/profiles/{Guid.NewGuid()}{suffix}")
        {
            Content = method == "PUT" ? new StringContent(RepoFiles.GoldenProfile("G12").ToJsonString(), Encoding.UTF8, "application/json") : null,
        };

        var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProfileNotFound, (await response.Json())["type"]!.GetValue<string>());
    }

    // A settings form marks every wrong field from one answer, so every refused value is named at once, each by its path.
    [Fact]
    public async Task EveryRefusedValueIsNamedAtOnceByItsJsonPath()
    {
        await using var api = await Api();
        var profile = RepoFiles.GoldenProfile("G17");
        profile["region"] = "CT";
        profile["employment"]!["ingresos"] = "1.234,56";
        profile["employment"]!["seguridadSocial"] = "-10.00";
        profile["activity"]!["previousYear"]!["rendimientoNeto"] = "9500.001";
        profile["activity"]!["newActivity"]!["ingresosFromFormerEmployer"] = "";
        profile["projection"]!["gastos"] = "1000000000000.00";
        profile["projection"]!["baseCotizacion"] = "100.00";

        var response = await api.CreateClient().PostProfile(profile);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/invalid-input", problem["type"]!.GetValue<string>());
        Assert.Equal(
            [
                "$.region",
                "$.employment.ingresos",
                "$.employment.seguridadSocial",
                "$.activity.previousYear.rendimientoNeto",
                "$.activity.newActivity.ingresosFromFormerEmployer",
                "$.projection.baseCotizacion",
                "$.projection.gastos",
            ],
            problem["errors"]!.AsObject().Select(e => e.Key));
        Assert.Equal(
            "$.projection.baseCotizacion is \"100.00\"; it must be a base of the 2025 tables, from 653.59 to 4909.50 (LGSS art. 308.1.a 3.ª).",
            problem["errors"]!["$.projection.baseCotizacion"]![0]!.GetValue<string>());
        Assert.Empty(JsonNode.Parse(await api.CreateClient().GetStringAsync("/api/v1/profiles"))!.AsArray());
    }

    [Theory]
    [InlineData(1999, "1999-01-15", "$.taxYear")]
    [InlineData(2025, "2026-01-01", "$.activity.alta")]
    public async Task AYearWithoutConfigurationOrAnAltaAfterTheYearIsRefused(int taxYear, string alta, string path)
    {
        await using var api = await Api();
        var profile = RepoFiles.GoldenProfile("G12");
        profile["taxYear"] = taxYear;
        profile["activity"]!["alta"] = alta;

        var response = await api.CreateClient().PostProfile(profile);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([path], (await response.Json())["errors"]!.AsObject().Select(e => e.Key));
    }

    // A body of the wrong shape is refused at its first wrong field, with that field's path.
    [Theory]
    [InlineData("not json", "$")]
    [InlineData("null", "$")]
    [InlineData("{ \"taxYear\": \"2025\" }", "$.taxYear")]
    [InlineData("{ \"taxYear\": 2025, \"extra\": 1 }", "$.extra")]
    public async Task ABodyOfTheWrongShapeIsRefusedAtItsFirstWrongField(string body, string path)
    {
        await using var api = await Api();

        var response = await api.CreateClient().PostAsync("/api/v1/profiles", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([path], (await response.Json())["errors"]!.AsObject().Select(e => e.Key));
    }

    // The serializer names the path of a missing kind only in its message, so that one is refused at "$" and named in words.
    [Theory]
    [InlineData("previousYear", "{ \"rendimientoNeto\": \"8000.00\" }", "$")]
    [InlineData("previousYear", "{ \"kind\": \"lastYear\" }", "$.activity.previousYear")]
    [InlineData("newActivity", "\"established\"", "$.activity.newActivity")]
    [InlineData("newActivity", "{ \"kind\": \"started\", \"period\": \"third\", \"ingresosFromFormerEmployer\": \"0.00\" }", "$.activity.newActivity.period")]
    public async Task AUnionWithoutAKnownKindIsRefused(string field, string value, string path)
    {
        await using var api = await Api();
        var profile = RepoFiles.GoldenProfile("G15");
        profile["activity"]![field] = JsonNode.Parse(value);

        var response = await api.CreateClient().PostProfile(profile);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Json())["errors"]!.AsObject();
        Assert.Equal([path], errors.Select(e => e.Key));
        Assert.Contains($"$.activity.{field}", errors[path]![0]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheKindMayComeAfterTheOtherMembers()
    {
        await using var api = await Api();
        var profile = RepoFiles.GoldenProfile("G15");
        profile["activity"]!["previousYear"] = JsonNode.Parse("{ \"rendimientoNeto\": \"30000.00\", \"kind\": \"rendimientoNeto\" }");

        var response = await api.CreateClient().PostProfile(profile);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The estimate of a stored profile is the console's estimate of the same facts: for a golden that states no closed
    // quarter, the stored profile and the golden's quarter give the golden's own answer, to the cent and step by step.
    [Theory]
    [MemberData(nameof(Goldens))]
    public async Task AStoredProfilesEstimateIsTheEstimateOfTheSameInputFile(string golden)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile(golden))).Json())["id"]!.GetValue<string>();
        var input = RepoFiles.GoldenInput(golden);

        var response = await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf={input["asOf"]}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fromProfile = await response.Json();
        var fromFile = await (await client.Estimate(2025, input)).Json();
        Assert.True(JsonNode.DeepEquals(fromFile, fromProfile), fromProfile.ToJsonString());
        Assert.Equal(RepoFiles.Golden(golden)["expected"]!["holdBackShare"]!.GetValue<string>(), fromProfile["holdBackShare"]!.GetValue<string>());
    }

    // #69: a 2026 profile is stored, and its estimate is refused, not guessed, while 2026.json declares its calendar (the Q4
    // Modelo 130 deadline and the renta window) unpublished: the estimate needs both, whatever the quarter. The web app's default tax year skips such a year (web/README.md, "The taxpayer profile").
    [Fact]
    public async Task A2026ProfileIsStoredAndItsEstimateIsA422ConfigGapWhileTheRentaWindowIsUnpublished()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var profile = RepoFiles.GoldenProfile("G16");
        profile["taxYear"] = 2026;
        var created = await client.PostProfile(profile);
        var id = (await created.Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/config-gap", problem["type"]!.GetValue<string>());
        Assert.Contains("calendar.", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "asOf is missing; it must be the quarter of the estimate, one of Q1, Q2, Q3, Q4.")]
    [InlineData("?asOf=Q5", "asOf is \"Q5\"; it must be the quarter of the estimate, one of Q1, Q2, Q3, Q4.")]
    [InlineData("?asOf=1", "asOf is \"1\"; it must be the quarter of the estimate, one of Q1, Q2, Q3, Q4.")]
    [InlineData("?asOf=q1", "asOf is \"q1\"; it must be the quarter of the estimate, one of Q1, Q2, Q3, Q4.")]
    public async Task AMissingOrMalformedQuarterIsAnInvalidInputProblemKeyedByTheParameter(string query, string message)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], (await response.Json())["errors"]!["asOf"]!.AsArray().Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public async Task AQuarterBeforeTheAltaIsA422WithTheEnginesReason()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var profile = RepoFiles.GoldenProfile("G12");
        profile["activity"]!["alta"] = "2025-08-01";
        var id = (await (await client.PostProfile(profile)).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/estimate-refused", problem["type"]!.GetValue<string>());
        Assert.Equal("Quarter Q1 of 2025 ends in 2025-03, before the alta month 2025-08 (alta 2025-08-01).", problem["detail"]!.GetValue<string>());
    }

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }

    private static JsonObject WithoutId(JsonObject profile)
    {
        var copy = profile.DeepClone().AsObject();
        copy.Remove("id");
        return copy;
    }
}
