using System.Net;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

// GET /profiles/{id}/calendar and its RFC 5545 export (#70), against a real PostgreSQL (TestDatabase). Everything here runs
// before G12's alta (1 January 2025) unless a test sets its own clock, so every obligation is upcoming and the ICS export's
// event count matches the calendar's own, unless a test is specifically about the upcoming-only filter.
public class PaymentsCalendarEndpoint
{
    private static readonly DateTimeOffset BeforeG12sAlta = new(2024, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private const string ProfileNotFound = "https://gestoria.local/problems/profile-not-found";
    private const string ConfigGap = "https://gestoria.local/problems/config-gap";
    private const string InvalidInput = "https://gestoria.local/problems/invalid-input";

    [Fact]
    public async Task EveryQuarterCarriesModelo130Modelo303AndModelo349AndTheYearHasOneRentaTrueUp()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/calendar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Json();
        Assert.Equal(2025, body["taxYear"]!.GetValue<int>());
        var obligations = body["obligations"]!.AsArray();
        foreach (var quarter in new[] { "Q1", "Q2", "Q3", "Q4" })
        {
            AssertSingle(obligations, "Modelo130", quarter);
            AssertSingle(obligations, "Modelo303", quarter);
            AssertSingle(obligations, "Modelo349", quarter);
        }
        AssertSingle(obligations, "RentaTrueUp", "2025");
    }

    [Fact]
    public async Task Modelo130IsKnownAndModelo303IsNotYetKnown()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var body = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar")).Json();
        var obligations = body["obligations"]!.AsArray();

        var modelo130 = obligations.Single(o => o!["kind"]!.GetValue<string>() == "Modelo130" && o["period"]!.GetValue<string>() == "Q1")!;
        Assert.Equal("known", modelo130["amount"]!["kind"]!.GetValue<string>());
        Assert.Matches(@"^-?\d+\.\d{2}$", modelo130["amount"]!["euros"]!.GetValue<string>());

        var modelo303 = obligations.Single(o => o!["kind"]!.GetValue<string>() == "Modelo303" && o["period"]!.GetValue<string>() == "Q1")!;
        Assert.Equal("notYetKnown", modelo303["amount"]!["kind"]!.GetValue<string>());
        Assert.Equal(modelo130["dueFrom"]!.GetValue<string>(), modelo303["dueFrom"]!.GetValue<string>());
        Assert.Equal(modelo130["dueBy"]!.GetValue<string>(), modelo303["dueBy"]!.GetValue<string>());
    }

    // SPEC-003 §2.1: a quarter with no intra-EU operations files no 349 at all, a fact the calendar cannot know without
    // classified transactions, so the reason names the condition rather than implying every quarter owes one.
    [Fact]
    public async Task Modelo349sReasonNamesTheIntraEuCondition()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var body = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar")).Json();
        var modelo349 = body["obligations"]!.AsArray().Single(o => o!["kind"]!.GetValue<string>() == "Modelo349" && o["period"]!.GetValue<string>() == "Q1")!;

        Assert.Contains("intra-EU", modelo349["amount"]!["reason"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryMonthOfAltaCarriesAKnownTgssCuota()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var body = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar")).Json();
        var cuotas = body["obligations"]!.AsArray().Where(o => o!["kind"]!.GetValue<string>() == "SeguridadSocial").ToList();

        Assert.Equal(12, cuotas.Count);
        Assert.All(cuotas, o => Assert.Equal("known", o!["amount"]!["kind"]!.GetValue<string>()));
    }

    [Fact]
    public async Task AnUnknownProfileIsA404()
    {
        await using var api = await Api();

        var response = await api.CreateClient().GetAsync($"/api/v1/profiles/{Guid.NewGuid()}/calendar");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProfileNotFound, (await response.Json())["type"]!.GetValue<string>());
    }

    // #69: 2026.json declares its renta window and Q4 Modelo 130 deadline unpublished, so a 2026 profile's calendar is a 422
    // like its set-aside estimate, whatever quarter the gap is in.
    [Fact]
    public async Task A2026ProfilesCalendarIsA422ConfigGapWhileTheCalendarIsUnpublished()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var profile = RepoFiles.GoldenProfile("G16");
        profile["taxYear"] = 2026;
        var id = (await (await client.PostProfile(profile)).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/calendar");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ConfigGap, (await response.Json())["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheIcsExportListsOneEventPerUpcomingObligationWithoutAmountsByDefault()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();
        var calendarObligations = (await (await client.GetAsync($"/api/v1/profiles/{id}/calendar")).Json())["obligations"]!.AsArray().Count;

        var response = await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/calendar", response.MediaType());
        var ics = await response.Content.ReadAsStringAsync();
        // Every obligation is upcoming (the clock is before G12's alta), so the ICS still holds all of them.
        Assert.Equal(calendarObligations, CountOccurrences(ics, "BEGIN:VEVENT"));
        Assert.DoesNotMatch(@"€|\d+\.\d{2}", ics);
    }

    // The export holds only what is still to come: a clock set after Q1's due date and before Q3's excludes Q1 and keeps Q3.
    [Fact]
    public async Task TheIcsExportHoldsOnlyUpcomingObligations()
    {
        await using var api = await Api(now: new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero));
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var ics = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Modelo130-Q1-2025@", ics, StringComparison.Ordinal);
        Assert.Contains("Modelo130-Q3-2025@", ics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIcsExportAddsAmountsOnlyWhenAskedTo()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var ics = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics?amounts=true")).Content.ReadAsStringAsync();

        Assert.Matches(@"\d+\.\d{2}", ics);
    }

    [Fact]
    public async Task AMalformedAmountsIsAnInvalidInputProblem()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics?amounts=maybe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal(InvalidInput, problem["type"]!.GetValue<string>());
        Assert.Equal(["amounts"], problem["errors"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public async Task AMalformedLangIsAnInvalidInputProblem()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics?lang=fr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal(InvalidInput, problem["type"]!.GetValue<string>());
        Assert.Equal(["lang"], problem["errors"]!.AsObject().Select(e => e.Key));
    }

    [Theory]
    [InlineData("uk", "Строк сплати")]
    [InlineData("es", "Plazo")]
    [InlineData("en", "Due window")]
    [InlineData("ru", "Срок оплаты")]
    public async Task TheIcsExportsEventTextIsInTheAskedForLanguage(string lang, string dueWindowLabel)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var ics = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics?lang={lang}")).Content.ReadAsStringAsync();

        Assert.Contains(dueWindowLabel, ics, StringComparison.Ordinal);
    }

    // A local holiday moves a filing deadline forward, never late; it moves a RETA cuota's own deadline backward, so the
    // shown date can already be late (Ley 39/2015 art. 30.6; RD 1415/2004 art. 56.1 and 8).
    [Fact]
    public async Task TheIcsDescriptionWordsTheLocalHolidaysCaveatByDirection()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var ics = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics")).Content.ReadAsStringAsync();

        Assert.Contains("can be early but never late", ics, StringComparison.Ordinal);
        Assert.Contains("may already be a working day late", ics, StringComparison.Ordinal);
    }

    // Two exports a moment apart name every event the same, so importing the second updates the first instead of doubling
    // it. DTSTAMP is "now" and legitimately differs between the two calls, so only the deterministic lines are compared.
    [Fact]
    public async Task TheIcsExportsUidsAreStableAcrossExports()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var first = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics")).Content.ReadAsStringAsync();
        var second = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics")).Content.ReadAsStringAsync();

        Assert.Equal(UidLines(first), UidLines(second));
        Assert.NotEmpty(UidLines(first));
    }

    // Modelo130-Q1@gestoria.local repeated for both 2025 and a later tax year; the tax year makes it unique.
    [Fact]
    public async Task TheIcsUidsCarryTheTaxYearSoTheyDoNotCollideAcrossYears()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var ics = await (await client.GetAsync($"/api/v1/profiles/{id}/calendar.ics")).Content.ReadAsStringAsync();

        Assert.Contains("UID:Modelo130-Q1-2025@", ics, StringComparison.Ordinal);
    }

    private static IEnumerable<string> UidLines(string ics) =>
        ics.Split("\r\n").Where(line => line.StartsWith("UID:", StringComparison.Ordinal));

    private static void AssertSingle(JsonArray obligations, string kind, string period) =>
        Assert.Single(obligations, o => o!["kind"]!.GetValue<string>() == kind && o["period"]!.GetValue<string>() == period);

    private static int CountOccurrences(string text, string token) => (text.Length - text.Replace(token, "").Length) / token.Length;

    private static async Task<ApiFactory> Api(DateTimeOffset? now = null)
    {
        var api = new ApiFactory { Clock = new FixedTimeProvider(now ?? BeforeG12sAlta) };
        await api.InitializeAsync();
        return api;
    }
}
