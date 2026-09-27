using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Engine.Tests;

internal static class TaxYearConfigFiles
{
    internal const string File2025 = "2025.json";

    // Lazy<T> builds the value on first use, so a broken file fails the tests that read it rather than every test in the assembly.
    private static readonly Lazy<TaxYearConfig> LazyYear2025 =
        new(() => TaxYearConfigParser.Parse(File.ReadAllBytes(Path.Combine(Root(), File2025)), File2025));

    internal static TaxYearConfig Year2025 => LazyYear2025.Value;

    internal const string File2026 = "2026.json";

    private static readonly Lazy<TaxYearConfig> LazyYear2026 = new(() => new TaxYearConfigLoader(Root()).Load(2026));

    internal static TaxYearConfig Year2026 => LazyYear2026.Value;

    // A region the file names but declares incomplete, as MD was until #60. No real file carries one any more, so the tests
    // that cover the refusal add this block to a copy of 2025.json.
    internal const string DeclaredIncompleteRegion = "GA";

    internal const string DeclaredIncompleteRegionNote = "scale, mínimos and holidays not researched";

    internal static JsonObject DeclaredIncompleteRegionBlock() => new()
    {
        ["name"] = "Galicia",
        ["escalaAutonomica"] = new JsonArray(),
        ["minimosOverride"] = null,
        ["holidays"] = new JsonArray(),
        ["_todo"] = DeclaredIncompleteRegionNote,
    };

    private static readonly Lazy<TaxYearConfig> LazyYear2025WithDeclaredIncompleteRegion = new(() =>
    {
        var root = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Root(), File2025)))!;
        root["regions"]![DeclaredIncompleteRegion] = DeclaredIncompleteRegionBlock();

        return TaxYearConfigParser.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), File2025);
    });

    internal static TaxYearConfig Year2025WithDeclaredIncompleteRegion => LazyYear2025WithDeclaredIncompleteRegion.Value;

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
}