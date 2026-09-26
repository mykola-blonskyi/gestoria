using System.Runtime.CompilerServices;

namespace GestorIA.Engine.Tests;

internal static class TaxYearConfigFiles
{
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