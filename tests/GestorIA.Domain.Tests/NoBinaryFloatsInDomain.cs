using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace GestorIA.Domain.Tests;

public class NoBinaryFloatsInDomain
{
    [Fact]
    public void DomainSourceDeclaresNoDoubleOrFloat()
    {
        var pattern = new Regex(@"\b(double|float)\b");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(DomainRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            { continue; }

            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1} {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "ADR-0004: money is decimal. Binary floats found:\n" + string.Join("\n", offenders));
    }

    // [CallerFilePath] makes the compiler fill `here` with this source file's path at the call site.
    private static string DomainRoot([CallerFilePath] string here = "")
    {
        // `throw` can be used as an expression, so `?? throw` fails fast where TS would need a separate if.
        var dir = Path.GetDirectoryName(here)
            ?? throw new InvalidOperationException(
                $"[CallerFilePath] resolved to '{here}', which has no directory part.");

        return Path.GetFullPath(Path.Combine(dir, "..", "..", "src", "GestorIA.Domain"));
    }
}
