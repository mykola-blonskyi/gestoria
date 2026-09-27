using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace GestorIA.Api.Tests;

public class ApiKeyRequirement(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string WebOrigin = "http://localhost:3000";

    // Every operation of the committed document, so an endpoint added later is covered without editing this list.
    public static TheoryData<string, string> Operations()
    {
        var paths = JsonNode.Parse(File.ReadAllText(RepoFiles.OpenApiDocument))!["paths"]!.AsObject();
        var operations = new TheoryData<string, string>();
        foreach (var (path, methods) in paths)
        {
            foreach (var (method, _) in methods!.AsObject())
            {
                operations.Add(method.ToUpperInvariant(), path.Replace("{year}", "2025", StringComparison.Ordinal));
            }
        }
        return operations;
    }

    public static TheoryData<string?> WrongKeys => new(null, "", "wrong", ApiFactory.Key.ToUpperInvariant(), ApiFactory.Key + " ", ApiFactory.Key[..^1]);

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryOperationButTheHealthChecksRefusesARequestWithoutTheKey(string method, string path)
    {
        var response = await api.CreateClientWithoutKey().SendAsync(new HttpRequestMessage(new HttpMethod(method), path + "?taxYear=2025"));

        if (path.StartsWith("/api/v1/health/", StringComparison.Ordinal))
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        else
        {
            await AssertUnauthorized(response);
        }
    }

    [Theory]
    [MemberData(nameof(WrongKeys))]
    public async Task AWrongKeyIsRefusedTheSameWayAsNoKey(string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config/tax-years");
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(ApiKey.Header, key);
        }

        await AssertUnauthorized(await api.CreateClientWithoutKey().SendAsync(request));
    }

    [Fact]
    public async Task TheKeySentTwiceIsRefused()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config/tax-years");
        request.Headers.Add(ApiKey.Header, [ApiFactory.Key, ApiFactory.Key]);

        await AssertUnauthorized(await api.CreateClientWithoutKey().SendAsync(request));
    }

    [Fact]
    public async Task TheKeyOpensTheApi()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/config/tax-years");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The web app calls the API from its own origin, so the browser must be allowed to send the header and to read the 401.
    [Fact]
    public async Task TheWebOriginMaySendTheKeyAndReadTheRefusal()
    {
        var client = api.CreateClientWithoutKey();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/config/tax-years");
        preflight.Headers.Add("Origin", WebOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", ApiKey.Header.ToLowerInvariant());
        var refused = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config/tax-years");
        refused.Headers.Add("Origin", WebOrigin);

        var allowed = await client.SendAsync(preflight);
        var refusal = await client.SendAsync(refused);

        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Contains(ApiKey.Header, allowed.Headers.GetValues("Access-Control-Allow-Headers").Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, refusal.StatusCode);
        Assert.Equal(WebOrigin, refusal.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void TheApiRefusesToStartWithoutAUsableKeyHash(string? hash)
    {
        using var unconfigured = new ApiFactoryWithHash(hash);

        var failure = Assert.Throws<OptionsValidationException>(() => unconfigured.CreateClient());
        Assert.Contains("Auth:ApiKeySha256", failure.Message, StringComparison.Ordinal);
    }

    private static async Task AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        Assert.Equal($"ApiKey header=\"{ApiKey.Header}\"", response.Headers.WwwAuthenticate.Single().ToString());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/api-key-required", problem["type"]!.GetValue<string>());
        Assert.Equal(401, problem["status"]!.GetValue<int>());
    }

    // An empty hash (null) stands for a machine where the key was never set; appsettings.json holds none.
    private sealed class ApiFactoryWithHash(string? hash) : ApiFactory
    {
        protected override string? ConfiguredHash => hash;
    }
}
