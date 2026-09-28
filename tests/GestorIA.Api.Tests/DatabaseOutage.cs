using System.Net;
using System.Text.Json.Nodes;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GestorIA.Api.Tests;

// SPEC-009 §4, SPEC-013 §2: PostgreSQL going away after the API started. The container is this class's own and is stopped
// halfway through the test; every answer after that is a 503 database-unavailable problem, and neither the answers nor any
// log line name the server, the port, the database, the user or the password.
public sealed class DatabaseOutage : IAsyncLifetime
{
    // Made up for this test, and distinct enough that finding one of them in a log line can only mean it leaked.
    private const string Username = "outage_test_user";
    private readonly string password = $"outage-test-password-{Guid.NewGuid():N}";
    private readonly PostgreSqlContainer container;
    private LoggedApi api = null!;
    private NpgsqlConnectionStringBuilder connection = null!;

    public DatabaseOutage() => container = TestDatabase.OwnContainer(Username, password);

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        connection = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = $"gestoria_outage_{Guid.NewGuid():N}" };
        api = new LoggedApi(connection.ConnectionString);
        await api.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await api.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact]
    public async Task AfterTheDatabaseStopsEveryRequestThatNeedsItIsA503AndNothingNamesTheConnection()
    {
        var client = api.CreateClient();
        var open = api.CreateClientWithoutKey();
        Assert.Equal(HttpStatusCode.NoContent, (await open.GetAsync("/api/v1/health/ready")).StatusCode);
        var created = await client.PostProfile(RepoFiles.GoldenProfile("G12"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Json())["id"]!.GetValue<string>();
        // A valid file, so the restore reaches the database: its checks before that need none.
        var export = await client.GetStringAsync($"/api/v1/profiles/{id}/export");

        var before = api.Lines.Count;
        await container.StopAsync();

        var ready = await open.GetAsync("/api/v1/health/ready");
        var answers = new[]
        {
            ready,
            await client.GetAsync("/api/v1/profiles"),
            await client.GetAsync($"/api/v1/profiles/{id}"),
            await client.PostProfile(RepoFiles.GoldenProfile("G12")),
            await client.PutProfile(id, RepoFiles.GoldenProfile("G16")),
            await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q1"),
            await client.ImportStatement(id, RepoFiles.Statement),
            await client.GetAsync($"/api/v1/profiles/{id}/transactions?year=2025"),
            await client.GetAsync($"/api/v1/profiles/{id}/export"),
            await client.DeleteAsync($"/api/v1/profiles/{id}"),
            await client.Restore(export),
        };

        foreach (var answer in answers)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
            Assert.Equal(Http.ProblemJson, answer.MediaType());
            var text = await answer.Content.ReadAsStringAsync();
            var problem = JsonNode.Parse(text)!.AsObject();
            Assert.Equal("https://gestoria.local/problems/database-unavailable", problem["type"]!.GetValue<string>());
            Assert.Equal(503, problem["status"]!.GetValue<int>());
            AssertNamesNothing(text);
        }

        // What needs no database still answers: the process is up, and the tax years are files next to the binary.
        Assert.Equal(HttpStatusCode.NoContent, (await open.GetAsync("/api/v1/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/config/tax-years")).StatusCode);

        // The web app's tests stub the 503 with this answer; traceId differs on every request.
        var fixture = JsonNode.Parse(await ready.Content.ReadAsStringAsync())!.AsObject();
        fixture.Remove("traceId");
        WebFixtures.AssertFixture("database-unavailable.json", fixture);

        // Every line written since the stop, at every level down to Trace.
        var outage = api.Lines.Skip(before).ToList();
        Assert.Contains(outage, line => line.StartsWith("Warning GestorIA.Api.DatabaseUnavailable The database is not reachable", StringComparison.Ordinal));
        foreach (var line in outage)
        {
            AssertNamesNothing(line);
        }
        Assert.DoesNotContain(api.Lines, line => line.Contains(password, StringComparison.Ordinal) || line.Contains(Username, StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyAFailureToReachTheServerCounts()
    {
        var refused = new NpgsqlException("Failed to connect", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        var lost = new NpgsqlException("Exception while reading from stream", new IOException("closed"));
        var shutdown = new PostgresException("terminating connection due to administrator command", "FATAL", "FATAL", PostgresErrorCodes.AdminShutdown);
        var duplicate = new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        Assert.True(DatabaseUnavailable.Is(refused));
        Assert.True(DatabaseUnavailable.Is(lost));
        Assert.True(DatabaseUnavailable.Is(shutdown));
        Assert.True(DatabaseUnavailable.Is(new Microsoft.EntityFrameworkCore.DbUpdateException("save failed", refused)));
        Assert.False(DatabaseUnavailable.Is(duplicate));
        Assert.False(DatabaseUnavailable.Is(new Microsoft.EntityFrameworkCore.DbUpdateException("save failed", duplicate)));
        Assert.False(DatabaseUnavailable.Is(new InvalidOperationException("not a database failure")));
    }

    private void AssertNamesNothing(string text)
    {
        Assert.DoesNotContain(password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Username, text, StringComparison.Ordinal);
        Assert.DoesNotContain(connection.Database!, text, StringComparison.Ordinal);
        Assert.DoesNotMatch($@"\b{connection.Port}\b", text);
        Assert.DoesNotContain($"{connection.Host}:", text, StringComparison.Ordinal);
    }
}
