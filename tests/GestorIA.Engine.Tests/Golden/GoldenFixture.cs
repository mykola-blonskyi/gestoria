using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests.Golden;

// Reads a tests/golden/2025/G0N.json fixture. Each calculation in it is a part with its own provenance (SPEC-011 §1).
internal static class GoldenFixture
{
    private static readonly string[] OracleTiers = ["gestor-prepared", "aeat-simulator", "published-example", "theory"];

    internal static JsonNode Load(string golden)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), $"{golden}.json")))!;

        Assert.Equal(golden, fixture["golden"]!.GetValue<string>());
        Assert.Equal(TaxYearConfigFiles.Example2025, fixture["config"]!.GetValue<string>());

        return fixture;
    }

    // SPEC-011 §1: a golden without its oracle, the reference and the date it was run is not a gate.
    internal static void AssertProvenance(JsonNode part)
    {
        Assert.Contains(part["oracle"]!.GetValue<string>(), OracleTiers);
        Assert.False(string.IsNullOrWhiteSpace(part["oracleRef"]!.GetValue<string>()));
        Assert.True(DateOnly.TryParseExact(part["oracleRunDate"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }

    internal static decimal DecimalOf(JsonNode? amount) =>
        decimal.Parse(amount!.GetValue<string>(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    internal static Money MoneyOf(JsonNode? amount) => new(DecimalOf(amount));

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "golden", "2025"));
}
