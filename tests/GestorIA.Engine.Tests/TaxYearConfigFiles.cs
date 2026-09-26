using System.Runtime.CompilerServices;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Engine.Tests;

internal static class TaxYearConfigFiles
{
    internal const string Example2025 = "2025.example.json";

    // Lazy<T> builds the value on first use, so a broken file fails the tests that read it rather than every test in the assembly.
    private static readonly Lazy<TaxYearConfig> LazyYear2025 =
        new(() => TaxYearConfigParser.Parse(File.ReadAllBytes(Path.Combine(Root(), Example2025)), Example2025));

    internal static TaxYearConfig Year2025 => LazyYear2025.Value;

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