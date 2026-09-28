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

    internal static Task<HttpResponseMessage> PostProfile(this HttpClient client, JsonNode body) =>
        client.PostAsync("/api/v1/profiles", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

    internal static Task<HttpResponseMessage> PutProfile(this HttpClient client, string id, JsonNode body) =>
        client.PutAsync($"/api/v1/profiles/{id}", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

    internal static Task<HttpResponseMessage> ImportStatement(this HttpClient client, string profileId, byte[] statement, string bank = "bbva", string? contentType = "text/csv")
    {
        var content = new ByteArrayContent(statement);
        content.Headers.ContentType = contentType is null ? null : System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return client.PostAsync($"/api/v1/profiles/{profileId}/bank-statements?bank={bank}", content);
    }

    internal static Task<HttpResponseMessage> Restore(this HttpClient client, string json, string contentType = "application/json") =>
        client.PostAsync("/api/v1/profiles/restore", new StringContent(json, Encoding.UTF8, contentType));

    internal static Task<HttpResponseMessage> Classify(this HttpClient client, string transactionId, string body) =>
        client.PostAsync($"/api/v1/transactions/{transactionId}/classify", new StringContent(body, Encoding.UTF8, "application/json"));

    internal static Task<HttpResponseMessage> ClassifyAs(this HttpClient client, string transactionId, string transactionClass) =>
        client.Classify(transactionId, new JsonObject { ["class"] = transactionClass }.ToJsonString());

    internal static async Task<JsonArray> ReviewQueue(this HttpClient client, string profileId) =>
        JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{profileId}/review-queue"))!.AsArray();

    internal static async Task<string> CreateProfile(this HttpClient client, string golden = "G12") =>
        (await (await client.PostProfile(RepoFiles.GoldenProfile(golden))).Json())["id"]!.GetValue<string>();

    internal static async Task<JsonObject> Json(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    internal static string? MediaType(this HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType;
}
