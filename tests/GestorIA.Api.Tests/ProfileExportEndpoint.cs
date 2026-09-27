using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

// GET /profiles/{id}/export (SPEC-009 §2.1, #74): everything stored for the profile, in the versioned document #75 restores.
public class ProfileExportEndpoint
{
    // The export's promise, held against the database rather than a list of kinds: every table of the EF Core model has its
    // member in entities, named after it, with as many rows as the table holds for the profile. A table the export forgets
    // fails here, as a table the delete forgets fails ProfileDeletion.
    [Fact]
    public async Task EveryTableOfTheModelIsExportedWithAllItsRows()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await StoredData.Seed(client);
        var tables = await StoredData.RowsPerTable(api);

        var entities = (await (await client.GetAsync($"/api/v1/profiles/{id}/export")).Json())["entities"]!.AsObject();

        Assert.Equal(tables.Select(table => Member(table.Name)).Order(StringComparer.Ordinal), entities.Select(kind => kind.Key).Order(StringComparer.Ordinal));
        Assert.All(tables, table => Assert.True(table.Rows > 0, $"{table.Name} holds no row; seed it in StoredData.Seed."));
        Assert.All(tables, table => Assert.Equal(table.Rows, entities[Member(table.Name)]!.AsArray().Count));
    }

    [Fact]
    public async Task TheExportHoldsTheProfileAndItsMovementsAsTheApiAnswersThem()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await StoredData.Seed(client);
        var profile = JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}"))!;
        var movements = JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}/transactions"))!.AsArray();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var export = await response.Json();
        Assert.Equal("gestoria.export", export["format"]!.GetValue<string>());
        Assert.Equal(1, export["formatVersion"]!.GetValue<int>());
        Assert.Equal("personal-financial-data", export["classification"]!.GetValue<string>());
        Assert.True(DateTimeOffset.UtcNow - DateTimeOffset.Parse(export["exportedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture) < TimeSpan.FromMinutes(1));
        Assert.True(JsonNode.DeepEquals(new JsonArray(profile.DeepClone()), export["entities"]!["profiles"]), export.ToJsonString());

        // Each movement is the list's answer, in the list's order, plus what a restore needs to store it again exactly.
        var exported = export["entities"]!["bankTransactions"]!.AsArray();
        Assert.Equal(movements.Count, exported.Count);
        foreach (var (listed, stored) in movements.Zip(exported))
        {
            var withoutRestoreFields = stored!.DeepClone().AsObject();
            Assert.True(withoutRestoreFields.Remove("importSequence") && withoutRestoreFields.Remove("lineNumber") && withoutRestoreFields.Remove("lineKey"));
            Assert.True(JsonNode.DeepEquals(listed, withoutRestoreFields), stored.ToJsonString());
            Assert.Matches("^[0-9a-f]{64}$", stored["lineKey"]!.GetValue<string>());
        }
    }

    // The file holds personal financial data: no cache may keep a copy, and its name says nothing about whose it is. Its date
    // is the day in Madrid, where the user files.
    [Fact]
    public async Task TheExportIsNotCachedAndItsFileNameCarriesOnlyTheMadridDate()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/export");

        Assert.True(response.Headers.CacheControl!.NoStore);
        var exportedAt = DateTimeOffset.Parse((await response.Json())["exportedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var day = TimeZoneInfo.ConvertTime(exportedAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal($"attachment; filename=\"gestoria-export-{day}.json\"", response.Content.Headers.GetValues("Content-Disposition").Single());
    }

    [Fact]
    public async Task AnUnknownProfileHasNoExport()
    {
        await using var api = await Api();

        var response = await api.CreateClient().GetAsync($"/api/v1/profiles/{Guid.NewGuid()}/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/profile-not-found", (await response.Json())["type"]!.GetValue<string>());
    }

    // The entities member a table is exported under: its name with a lower-case first letter, as the API writes JSON names.
    private static string Member(string table) => char.ToLowerInvariant(table[0]) + table[1..];

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }
}
