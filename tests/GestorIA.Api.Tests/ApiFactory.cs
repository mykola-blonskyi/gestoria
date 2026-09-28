using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestorIA.Api.Tests;

// A TimeProvider fixed at one instant, for a test that needs "today" to be a date of its own choosing (#70's upcoming-only
// ICS export) rather than whenever the test happens to run.
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

// The in-memory API with a test key's hash and a database of its own configured, and clients that send the key. The key is
// made up for the tests. As a class fixture, xUnit awaits InitializeAsync before the first test; created by hand, the test
// awaits it. Given a connection string, the factory opens that database instead, as the API does after a restart.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string Key = "test-only-api-key";

    // xUnit builds a class fixture through its one public constructor.
    public ApiFactory()
    {
    }

    internal ApiFactory(string connectionString) => ConnectionString = connectionString;

    internal string ConnectionString { get; private set; } = "";

    // Set before the first CreateClient() call (host build happens then); TimeProvider.System otherwise.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    internal static string Sha256(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public async Task InitializeAsync()
    {
        if (ConnectionString == "")
        {
            ConnectionString = await TestDatabase.NewConnectionString();
        }
    }

    // WebApplicationFactory already has a DisposeAsync of another type; naming the interface keeps the two apart.
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    // Npgsql keeps a pool of idle connections per connection string after the API is gone. Every test API has a database of its
    // own in one shared container, so those pools add up past PostgreSQL's max_connections ("53300: too many clients"); a
    // disposed API closes its idle connections. A later API on the same database opens new ones.
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (ConnectionString != "")
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            NpgsqlConnection.ClearPool(connection);
        }
    }

    // Added last, so it wins over appsettings.json and a developer's own user secrets.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        [
            new("Auth:ApiKeySha256", Sha256(Key)),
            new("ConnectionStrings:Gestoria", ConnectionString == "" ? throw new InvalidOperationException("Await InitializeAsync before creating a client.") : ConnectionString),
        ]));
        builder.ConfigureTestServices(services => services.AddSingleton(Clock));
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(ApiKey.Header, Key);
    }

    // A client with no key, for the requests the key must not be needed for, or must be refused without.
    internal HttpClient CreateClientWithoutKey()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Remove(ApiKey.Header);
        return client;
    }
}
