using System.Runtime.CompilerServices;
using Json.Schema;

namespace GestorIA.Engine.Tests;

internal static class TaxYearConfigFiles
{
    private static readonly Lazy<JsonSchema> LazySchema =
        new(() => JsonSchema.FromFile(Path.Combine(Root(), "schema.json")));
    internal static string Root([CallerFilePath] string here = "")
    {
        var dir = Path.GetDirectoryName(here)
            ?? throw new InvalidOperationException(
                $"[CallerFilePath] resolved to '{here}', which has no directory part.");

        return Path.GetFullPath(Path.Combine(dir, "..", "..", "config", "tax-years"));
    }

    internal static IEnumerable<string> All() =>
        Directory.EnumerateFiles(Root(), "*.json")
        .Where(f => Path.GetFileName(f) != "schema.json");

    internal static JsonSchema Schema() => LazySchema.Value;
}