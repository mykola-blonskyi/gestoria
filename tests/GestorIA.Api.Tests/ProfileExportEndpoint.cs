using System.Net;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

// GET /profiles/{id}/export (SPEC-009 §2.1, #74): everything stored for the profile, in the versioned document #75 restores.
public class ProfileExportEndpoint
{
    [Fact]
    public async Task TheExportHoldsTheStoredProfileAsReadBackUnderAVersionedMapOfEntityKinds()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var created = await (await client.PostProfile(RepoFiles.GoldenProfile("G15"))).Json();
        var id = created["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var export = await response.Json();
        Assert.Equal("gestoria.export", export["format"]!.GetValue<string>());
        Assert.Equal(1, export["formatVersion"]!.GetValue<int>());
        Assert.Equal("personal-financial-data", export["classification"]!.GetValue<string>());
        Assert.True(DateTimeOffset.UtcNow - DateTimeOffset.Parse(export["exportedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) < TimeSpan.FromMinutes(1));
        Assert.Equal(["profiles"], export["entities"]!.AsObject().Select(kind => kind.Key));
        Assert.True(JsonNode.DeepEquals(new JsonArray(created.DeepClone()), export["entities"]!["profiles"]), export.ToJsonString());
    }

    // The file holds personal financial data: no cache may keep a copy, and its name says nothing about whose it is.
    [Fact]
    public async Task TheExportIsNotCachedAndItsFileNameCarriesOnlyTheDate()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).Json())["id"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/export");

        Assert.True(response.Headers.CacheControl!.NoStore);
        var day = (await response.Json())["exportedAt"]!.GetValue<string>()[..10];
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

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }
}
