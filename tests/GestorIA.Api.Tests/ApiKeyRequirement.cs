using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                operations.Add(method.ToUpperInvariant(), path.Replace("{year}", "2025", StringComparison.Ordinal).Replace("{id}", Guid.Empty.ToString(), StringComparison.Ordinal));
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

    // Without the key a caller learns nothing about which routes, methods or media types exist: routing alone would answer
    // 404, 405 or 415 to these, and each must be the same 401 as any other request.
    [Theory]
    [InlineData("DELETE", "/api/v1/config/tax-years", null)]
    [InlineData("PATCH", "/api/v1/config/tax-years/2025", null)]
    [InlineData("GET", "/api/v1/set-aside/estimate", null)]
    [InlineData("POST", "/api/v1/set-aside/estimate?taxYear=2025", "text/plain")]
    [InlineData("GET", "/api/v1/no-such-resource", null)]
    [InlineData("GET", "/API/V1/config/tax-years", null)]
    public async Task EveryRequestUnderTheApiButTheHealthChecksIsRefusedWithoutTheKeyWhateverItsMethodOrMediaType(string method, string path, string? contentType)
    {
        await AssertUnauthorized(await api.CreateClientWithoutKey().SendAsync(Request(method, path, contentType)));
    }

    [Theory]
    [InlineData("DELETE", "/api/v1/config/tax-years", null, HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/api/v1/set-aside/estimate?taxYear=2025", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("GET", "/api/v1/no-such-resource", null, HttpStatusCode.NotFound)]
    public async Task WithTheKeyRoutingAnswersAsItWould(string method, string path, string? contentType, HttpStatusCode expected)
    {
        var response = await api.CreateClient().SendAsync(Request(method, path, contentType));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task TheHealthCheckAnswersWithoutTheKey()
    {
        var response = await api.CreateClientWithoutKey().GetAsync("/api/v1/health/live");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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

    // The check the host runs as it starts (ValidateOnStart), run here without a host: a failed start through
    // WebApplicationFactory surfaces as an ObjectDisposedException on some runs instead of the validation error.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void TheApiRefusesToStartWithoutAUsableKeyHash(string? hash)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new("Auth:ApiKeySha256", hash)]).Build());
        services.AddApiKey();
        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("Auth:ApiKeySha256", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyHashIsAccepted()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new("Auth:ApiKeySha256", ApiFactory.Sha256(ApiFactory.Key))]).Build());
        services.AddApiKey();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static HttpRequestMessage Request(string method, string path, string? contentType) => new(new HttpMethod(method), path)
    {
        Content = contentType is null ? null : new StringContent("{}", System.Text.Encoding.UTF8, contentType),
    };

    private static async Task AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        Assert.Equal($"ApiKey header=\"{ApiKey.Header}\"", response.Headers.WwwAuthenticate.Single().ToString());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/api-key-required", problem["type"]!.GetValue<string>());
        Assert.Equal(401, problem["status"]!.GetValue<int>());
    }
}
