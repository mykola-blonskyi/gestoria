using System.Net;
using GestorIA.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorIA.Api.Tests;

// The documented deletion test (SPEC-013 §4): DELETE /profiles/{id} leaves nothing of the profile in the database. It reads
// the tables from the EF Core model rather than naming them, so a table added later is covered without editing this test. Local
// mode holds one profile per installation, so "nothing of the profile" is "every table empty". Before the delete every table
// must hold a row, or its emptiness afterwards would prove nothing: a new table's ticket seeds it in Seed.
public class ProfileDeletion
{
    [Fact]
    public async Task DeletingTheProfileEmptiesEveryTableOfTheModel()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await Seed(client);
        Assert.All(await RowsPerTable(api), table => Assert.True(table.Rows > 0, $"{table.Name} holds no row before the delete; seed it in {nameof(Seed)}."));

        var response = await client.DeleteAsync($"/api/v1/profiles/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(await RowsPerTable(api), table => Assert.True(table.Rows == 0, $"{table.Name} still holds {table.Rows} row(s) after the delete."));
    }

    [Fact]
    public async Task AfterTheDeleteTheProfileIsGoneAndANewOneCanBeCreated()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await Seed(client);

        await client.DeleteAsync($"/api/v1/profiles/{id}");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/profiles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/profiles/{id}/export")).StatusCode);
        Assert.Equal("[]", await client.GetStringAsync("/api/v1/profiles"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostProfile(RepoFiles.GoldenProfile("G12"))).StatusCode);
    }

    [Fact]
    public async Task DeletingAnUnknownProfileIsA404AndDeletesNothing()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        await Seed(client);

        var response = await client.DeleteAsync($"/api/v1/profiles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/profile-not-found", (await response.Json())["type"]!.GetValue<string>());
        Assert.All(await RowsPerTable(api), table => Assert.True(table.Rows > 0, $"{table.Name} lost its rows."));
    }

    // The web app deletes from its own origin, so the browser's preflight must allow the method.
    [Fact]
    public async Task TheWebOriginMaySendADelete()
    {
        await using var api = await Api();
        var preflight = new HttpRequestMessage(HttpMethod.Options, $"/api/v1/profiles/{Guid.NewGuid()}");
        preflight.Headers.Add("Origin", "http://localhost:3000");
        preflight.Headers.Add("Access-Control-Request-Method", "DELETE");
        preflight.Headers.Add("Access-Control-Request-Headers", ApiKey.Header.ToLowerInvariant());

        var response = await api.CreateClientWithoutKey().SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("DELETE", response.Headers.GetValues("Access-Control-Allow-Methods").Single(), StringComparison.Ordinal);
    }

    // One row of every kind the installation stores for a profile, through the API as a user would store it.
    private static async Task<string> Seed(HttpClient client) =>
        (await (await client.PostProfile(RepoFiles.GoldenProfile("G15"))).Json())["id"]!.GetValue<string>();

    // Every table an entity type of the model maps to, with its row count. EF Core's own migrations history is not in the
    // model and holds no user data.
    private static async Task<List<(string Name, int Rows)>> RowsPerTable(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GestoriaDbContext>();
        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(entity => (Schema: entity.GetSchema(), Name: entity.GetTableName()!))
            .Distinct()
            .ToList();
        Assert.NotEmpty(tables);

        var counts = new List<(string, int)>();
        foreach (var (schema, name) in tables)
        {
            var qualified = schema is null ? $"\"{name}\"" : $"\"{schema}\".\"{name}\"";
            // SqlQueryRaw maps a scalar result through a column named Value. A table name cannot be a SQL parameter, and this one
            // comes from the model, never from input.
            var count = $"SELECT count(*)::int AS \"Value\" FROM {qualified}";
            var rows = await db.Database.SqlQueryRaw<int>(count).SingleAsync();
            counts.Add((qualified, rows));
        }

        return counts;
    }

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }
}
