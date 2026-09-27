using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GestorIA.Api.Tests;

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

    // Added last, so it wins over appsettings.json and a developer's own user secrets.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        [
            new("Auth:ApiKeySha256", Sha256(Key)),
            new("ConnectionStrings:Gestoria", ConnectionString == "" ? throw new InvalidOperationException("Await InitializeAsync before creating a client.") : ConnectionString),
        ]));

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
