using GestorIA.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorIA.Api.Tests;

// What an installation stores, read from the EF Core model rather than named, so a table added later is covered by the tests
// that use this (ProfileDeletion, ProfileExportEndpoint) without editing them.
internal static class StoredData
{
    // One profile and rows of every kind stored for it, through the API as a user would store them. A new table's ticket
    // seeds it here: ProfileDeletion refuses to run while a table holds no row.
    internal static async Task<string> Seed(HttpClient client)
    {
        var id = (await (await client.PostProfile(RepoFiles.GoldenProfile("G15"))).Json())["id"]!.GetValue<string>();
        (await client.ImportStatement(id, RepoFiles.Statement)).EnsureSuccessStatusCode();
        return id;
    }

    // Every table an entity type of the model maps to, with its row count. EF Core's own migrations history is not in the
    // model and holds no user data.
    internal static async Task<List<(string Name, int Rows)>> RowsPerTable(ApiFactory api)
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
            counts.Add((name, await db.Database.SqlQueryRaw<int>(count).SingleAsync()));
        }

        return counts;
    }
}
