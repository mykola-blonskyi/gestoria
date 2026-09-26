using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests.Golden;

// Runs the "modelo130" part of a tests/golden/2025/G0N.json fixture: quarters in order, each one's carry fed to the next.
internal static class Modelo130Golden
{
    private static readonly string[] OracleTiers = ["gestor-prepared", "aeat-simulator", "published-example", "theory"];

    internal static void Passes(string golden)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), $"{golden}.json")))!;
        var part = fixture["modelo130"]!;

        Assert.Equal(golden, fixture["golden"]!.GetValue<string>());
        Assert.Equal(TaxYearConfigFiles.Example2025, fixture["config"]!.GetValue<string>());
        AssertProvenance(part);

        var inputs = part["inputs"]!;
        Assert.Equal("foreignPayersOnly", inputs["retenciones"]!.GetValue<string>());
        PreviousYear previousYear = inputs["previousYear"] is JsonObject known
            ? new PreviousYear.RendimientoNeto(MoneyOf(known["rendimientoNeto"]))
            : new PreviousYear.NoActivity();

        var carry = Modelo130Carry.StartOfYear;
        var total = Money.Zero;
        var mismatches = new List<string>();
        var expectedQuarters = part["expected"]!["quarters"]!.AsArray();
        var inputQuarters = inputs["quarters"]!.AsArray();

        Assert.Equal(inputQuarters.Count, expectedQuarters.Count);

        for (var i = 0; i < inputQuarters.Count; i++)
        {
            var quarterInput = inputQuarters[i]!;
            var expected = expectedQuarters[i]!.AsObject();
            var quarter = Enum.Parse<Quarter>(quarterInput["quarter"]!.GetValue<string>());

            var result = Modelo130Calculator.Pago(
                new Modelo130Input(quarter, MoneyOf(quarterInput["ingresosYtd"]), MoneyOf(quarterInput["gastosYtd"]), new Retenciones.ForeignPayersOnly(), previousYear),
                carry,
                TaxYearConfigFiles.Year2025);

            var actual = new Dictionary<string, decimal>
            {
                ["rendimientoNeto"] = Output(result, "m130.rendimiento-neto"),
                ["pagoBruto"] = Output(result, "m130.pago-bruto"),
                ["pagosAnteriores"] = Output(result, "m130.pagos-anteriores"),
                ["resultado"] = result.Resultado.Amount,
                ["aIngresar"] = result.AIngresar.Amount,
                ["negativosPendientes"] = result.Carry.NegativosPendientes.Amount,
            };

            Assert.Equal(quarter.ToString(), expected["quarter"]!.GetValue<string>());

            foreach (var (key, value) in expected.Where(e => e.Key != "quarter"))
            {
                var want = MoneyOf(value).Amount;
                if (!actual.TryGetValue(key, out var got))
                {
                    mismatches.Add($"{quarter} {key}: the fixture expects it, the runner does not compute it");
                }
                else if (got != want)
                {
                    mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"{quarter} {key}: expected {want}, got {got}"));
                }
            }

            carry = result.Carry;
            total += result.AIngresar;
        }

        var wantTotal = MoneyOf(part["expected"]!["totalAIngresar"]).Amount;
        if (total.Amount != wantTotal)
        {
            mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"totalAIngresar: expected {wantTotal}, got {total.Amount}"));
        }

        Assert.True(mismatches.Count == 0, $"{golden} modelo130:\n  " + string.Join("\n  ", mismatches));
    }

    // SPEC-011 §1: a golden without its oracle, the reference and the date it was run is not a gate.
    private static void AssertProvenance(JsonNode part)
    {
        Assert.Contains(part["oracle"]!.GetValue<string>(), OracleTiers);
        Assert.False(string.IsNullOrWhiteSpace(part["oracleRef"]!.GetValue<string>()));
        Assert.True(DateOnly.TryParseExact(part["oracleRunDate"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }

    private static decimal Output(Modelo130Result result, string stepId) =>
        result.Trace.Steps.Single(s => s.Id == stepId).Output;

    private static Money MoneyOf(JsonNode? amount) =>
        new(decimal.Parse(amount!.GetValue<string>(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "golden", "2025"));
}
