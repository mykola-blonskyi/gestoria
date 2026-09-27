using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

internal static class Http
{
    internal const string ProblemJson = "application/problem+json";

    internal static Task<HttpResponseMessage> Estimate(this HttpClient client, int taxYear, JsonNode body) =>
        client.Estimate(taxYear, body.ToJsonString());

    internal static Task<HttpResponseMessage> Estimate(this HttpClient client, int taxYear, string body) =>
        client.PostAsync($"/api/v1/set-aside/estimate?taxYear={taxYear}", new StringContent(body, Encoding.UTF8, "application/json"));

    internal static async Task<JsonObject> Json(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    internal static string? MediaType(this HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType;
}
