using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GestorIA.Api.SetAside;

// The two fields of the input file that are a string or an object. The generator cannot infer that from a C# type, so this
// transformer writes their schemas by hand, from the same shapes SetAsideInputFile accepts.
public sealed class UnionSchemas : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(PreviousYearDocument))
        {
            OneOf(schema, "noActivity", Object(("rendimientoNeto", Amount(Amounts.Signed))));
        }
        else if (type == typeof(NewActivityDocument))
        {
            OneOf(
                schema,
                "established",
                Object(
                    ("period", new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create("first"), JsonValue.Create("following")] }),
                    ("ingresosFromFormerEmployer", Amount(Amounts.ZeroOrMore))));
        }

        return Task.CompletedTask;
    }

    private static void OneOf(OpenApiSchema schema, string word, OpenApiSchema obj)
    {
        schema.Type = null;
        schema.Properties = null;
        schema.Required = null;
        schema.AdditionalPropertiesAllowed = true;
        schema.OneOf = [new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create(word)] }, obj];
    }

    private static OpenApiSchema Object(params (string Name, OpenApiSchema Schema)[] fields) => new()
    {
        Type = JsonSchemaType.Object,
        Properties = fields.ToDictionary(field => field.Name, field => (IOpenApiSchema)field.Schema),
        Required = fields.Select(field => field.Name).ToHashSet(),
        AdditionalPropertiesAllowed = false,
    };

    private static OpenApiSchema Amount(string pattern) => new() { Type = JsonSchemaType.String, Pattern = pattern };
}
