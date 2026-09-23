using System.Text.Json.Nodes;

namespace GestorIA.Engine.Tests;

// JSON Schema ignores unknown keywords instead of rejecting them, so a block
// written in the wrong place validates nothing and the suite stays green.
// Both of these caught real mistakes while this schema was being written.
public class SchemaIsWiredUp
{
    private static readonly string[] KnownRootKeywords =
        ["$schema", "$id", "type", "additionalProperties", "required", "properties", "$defs"];

    [Fact]
    public void SchemaRootCarriesNoStrayKeywords()
    {
        var stray = Root().Select(entry => entry.Key).Except(KnownRootKeywords).ToList();

        Assert.True(stray.Count == 0,
            "schema.json has keys at its root that JSON Schema does not know and therefore ignores:\n\n"
            + string.Join("\n", stray.Select(key => $"  {key}    move it into $defs and point at it with $ref"))
            + $"\n\nKnown root keywords: {string.Join(", ", KnownRootKeywords)}");
    }

    [Fact]
    public void EveryDefinitionIsReferenced()
    {
        var text = File.ReadAllText(SchemaPath());

        var unused = Root()["$defs"]!.AsObject()
            .Select(entry => entry.Key)
            .Where(name => !text.Contains($"\"#/$defs/{name}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(unused.Count == 0,
            "schema.json defines these but never references them, so they validate nothing:\n\n"
            + string.Join("\n", unused.Select(name => $"  $defs/{name}")));
    }

    private static string SchemaPath() => Path.Combine(TaxYearConfigFiles.Root(), "schema.json");

    private static JsonObject Root() => JsonNode.Parse(File.ReadAllText(SchemaPath()))!.AsObject();
}
