using Npgsql;
using Testcontainers.PostgreSql;

namespace GestorIA.Api.Tests;

// A real PostgreSQL for the tests, never SQLite (ADR-0006), from the image compose.yaml runs locally. One container serves the
// whole test run; Testcontainers removes it when the run ends. Each ApiFactory asks for a database of its own in it, which the
// API's start-up migration creates, so test classes never see each other's profiles.
internal static class TestDatabase
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(async () =>
    {
        var container = new PostgreSqlBuilder(RepoFiles.ComposePostgresImage).Build();
        await container.StartAsync();
        return container;
    });

    internal static async Task<string> NewConnectionString()
    {
        var container = await Container.Value;
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = $"gestoria_{Guid.NewGuid():N}" }.ConnectionString;
    }
}
