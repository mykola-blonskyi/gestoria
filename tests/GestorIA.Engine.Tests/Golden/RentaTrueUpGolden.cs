using System.Globalization;
using System.Text.Json.Nodes;
using static GestorIA.Engine.Tests.Golden.GoldenFixture;

namespace GestorIA.Engine.Tests.Golden;

// Runs the "rentaTrueUp" part of a tests/golden/2025/G0N.json fixture through AnnualTrueUpCalculator.
internal static class RentaTrueUpGolden
{
    internal static void Passes(string golden)
    {
        var fixture = Load(golden);
        var part = fixture["rentaTrueUp"]!;
        AssertProvenance(part);

        var inputs = part["inputs"]!;
        var advances = MoneyOf(inputs["modelo130Advances"]);

        // When the fixture also runs the Modelo 130 quarters, the advances are their total, not a second, separate claim.
        if (fixture["modelo130"] is JsonNode modelo130)
        {
            Assert.Equal(MoneyOf(modelo130["expected"]!["totalAIngresar"]), advances);
        }

        var result = AnnualTrueUpCalculator.Gap(
            new AnnualTrueUpInput(
                new EmploymentIncome(MoneyOf(inputs["employment"]!["ingresos"]), MoneyOf(inputs["employment"]!["seguridadSocial"])),
                new ActivityIncome(MoneyOf(inputs["activity"]!["ingresos"]), MoneyOf(inputs["activity"]!["gastos"]), NewActivityOf(inputs["activity"]!["newActivity"])),
                advances,
                inputs["region"]!.GetValue<string>()),
            TaxYearConfigFiles.Year2025);

        var expected = part["expected"]!;
        var actual = new Dictionary<string, decimal>
        {
            ["liabilityOnActivity"] = result.LiabilityOnActivity.Amount,
            ["marginalRate"] = result.MarginalRate.Value,
            ["reduccionTrabajoLost"] = result.ReduccionTrabajoLost.Amount,
            ["gap"] = result.Gap.Amount,
        };
        var mismatches = new List<string>();

        foreach (var (key, got) in actual)
        {
            var want = DecimalOf(expected[key]);
            if (got != want)
            {
                mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"{key}: expected {want}, got {got}"));
            }
        }

        var wantPayableIn = expected["payableIn"]!.GetValue<string>();
        if (result.PayableIn.ToString() != wantPayableIn)
        {
            mismatches.Add($"payableIn: expected {wantPayableIn}, got {result.PayableIn}");
        }

        var wantWarnings = expected["warnings"]!.AsArray().Select(code => code!.GetValue<string>()).ToList();
        var gotWarnings = result.Warnings.Select(w => w.Code).ToList();
        if (!wantWarnings.SequenceEqual(gotWarnings))
        {
            mismatches.Add($"warnings: expected [{string.Join(", ", wantWarnings)}], got [{string.Join(", ", gotWarnings)}]");
        }

        Assert.True(mismatches.Count == 0, $"{golden} rentaTrueUp:\n  " + string.Join("\n  ", mismatches));
    }
}
