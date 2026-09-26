using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace GestorIA.Infrastructure.TaxYears;

// SPEC-007 §2: schema.json for rules about one node, TaxYearRules for rules comparing two.
public static class TaxYearConfigValidator
{
    private const string SchemaResource = "GestorIA.Infrastructure.TaxYears.schema.json";

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    public static IReadOnlyList<string> Validate(JsonNode? root, string fileName)
    {
        var results = Schema.Value.Evaluate(
            JsonSerializer.SerializeToElement(root),
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!results.IsValid)
        {
            var failures = results.Describe().ToList();

            return failures.Count > 0 ? failures : [$"{fileName} failed schema.json"];
        }

        // The cross-field rules dereference nodes the schema made required, so they run only on a schema-valid document.
        return [.. TaxYearRules.Check(root!, fileName)];
    }

    // schema.json is compiled into this assembly, so the rules the parser relies on ship with the parser.
    private static JsonSchema LoadSchema()
    {
        using var stream = typeof(TaxYearConfigValidator).Assembly.GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException($"{SchemaResource} is not embedded in GestorIA.Infrastructure.");
        using var reader = new StreamReader(stream);

        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
