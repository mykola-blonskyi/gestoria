using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestorIA.Api.Tests;

// IClassFixture shares one instance of the fixture, here one in-memory API, across the tests of the class.
public class HealthEndpoint(WebApplicationFactory<Program> api) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task LiveAnswersNoContent()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/health/live");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
