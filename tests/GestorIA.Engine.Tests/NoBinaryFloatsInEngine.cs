using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace GestorIA.Engine.Tests;

public class NoBinaryFloatsInEngine
{
    [Fact]
    public void EngineSourceDeclaresNoDoubleOrFloat()
    {
        var pattern = new Regex(@"\b(double|float)\b");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(EngineRoot(), "*.cs", SearchOption.AllDirectories))
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

    private static string EngineRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GestorIA.Engine")
        );
}