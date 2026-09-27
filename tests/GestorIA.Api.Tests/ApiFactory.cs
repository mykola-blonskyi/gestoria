using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GestorIA.Api.Tests;

// The in-memory API with a test key's hash configured, and clients that send the key. The key is made up for the tests.
public class ApiFactory : WebApplicationFactory<Program>
{
    internal const string Key = "test-only-api-key";

    internal static string Sha256(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    // Added last, so it wins over appsettings.json and a developer's own user secrets.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection([new("Auth:ApiKeySha256", Sha256(Key))]));

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
