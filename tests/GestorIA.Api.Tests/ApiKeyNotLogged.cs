namespace GestorIA.Api.Tests;

// SPEC-013: logs never hold a secret. Every category is captured at Trace while the key is used, mistyped and left out, and
// no line may contain the key or the wrong one.
public class ApiKeyNotLogged
{
    [Fact]
    public async Task NeitherTheKeyNorAWrongOneReachesAnyLog()
    {
        const string wrongKey = "mistyped-key-for-the-log-test";
        await using var api = new LoggedApi();
        await api.InitializeAsync();

        await api.CreateClient().GetAsync("/api/v1/config/tax-years");
        await api.CreateClient().Estimate(2025, RepoFiles.GoldenInput("G14"));
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config/tax-years");
        wrong.Headers.Add(ApiKey.Header, wrongKey);
        await api.CreateClientWithoutKey().SendAsync(wrong);
        await api.CreateClientWithoutKey().GetAsync("/api/v1/config/tax-years");

        Assert.NotEmpty(api.Lines);
        Assert.DoesNotContain(api.Lines, line => line.Contains(ApiFactory.Key, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Lines, line => line.Contains(wrongKey, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Lines, line => line.Contains(ApiFactory.Sha256(ApiFactory.Key), StringComparison.OrdinalIgnoreCase));
    }
}
