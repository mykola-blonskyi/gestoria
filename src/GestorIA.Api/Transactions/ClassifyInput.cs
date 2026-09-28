using System.Text.Json;
using System.Text.Json.Nodes;
using GestorIA.Domain.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GestorIA.Api.Transactions;

// The boundary between the body of POST /transactions/{id}/classify and a TransactionClass. Read as text, so a body that is
// not JSON is an invalid-input problem keyed "$" like ProfileInput's, and an unknown class one keyed "$.class".
public static class ClassifyInput
{
    // The refusal of the body, or null with the class it names.
    public static ValidationProblem? Refusal(string body, out TransactionClass decided)
    {
        decided = default;
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException e)
        {
            return Problems.Invalid("$", $"$: the body is not JSON: {e.Message}");
        }

        if (parsed is not JsonObject root)
        {
            return Problems.Invalid("$", "$ must be an object such as { \"class\": \"activityIncome\" }.");
        }

        if (root.Select(member => member.Key).FirstOrDefault(name => name != "class") is { } unknown)
        {
            return Problems.Invalid($"$.{unknown}", $"$.{unknown} is not a field of a classification; the body holds only \"class\".");
        }

        var expected = $"one of {string.Join(", ", TransactionClassNames.All)}";
        if (root["class"] is not JsonValue value || !value.TryGetValue<string>(out var name))
        {
            return Problems.Invalid("$.class", $"$.class is {(root.ContainsKey("class") ? root["class"]?.ToJsonString() ?? "null" : "missing")}; it must be {expected}.");
        }

        return TransactionClassNames.TryParse(name, out decided) ? null : Problems.Invalid("$.class", $"$.class is \"{name}\"; it must be {expected}.");
    }
}
