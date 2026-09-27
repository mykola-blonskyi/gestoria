using System.Net;

namespace GestorIA.Api.Tests;

// IClassFixture shares one instance of the fixture, here one in-memory API, across the tests of the class.
public class HealthEndpoint(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LiveAnswersNoContentWithoutTheKey()
    {
        var response = await api.CreateClientWithoutKey().GetAsync("/api/v1/health/live");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
