using System.Globalization;
using System.Text.Json.Nodes;
using static GestorIA.Engine.Tests.Golden.GoldenFixture;

namespace GestorIA.Engine.Tests.Golden;

// Runs the "setAside" part of a tests/golden/2025/G0N.json fixture through SetAsideEstimator.
internal static class SetAsideGolden
{
    internal static void Passes(string golden)
    {
        var part = Load(golden)["setAside"]!;
        AssertProvenance(part);

        var inputs = part["inputs"]!;
        var profileJson = inputs["profile"]!;
        var employmentJson = profileJson["employment"]!;
        var registrationJson = profileJson["activity"]!;
        var pictureJson = inputs["activity"]!;

        Assert.Equal("foreignPayersOnly", pictureJson["retenciones"]!.GetValue<string>());

        var alta = DateOnly.ParseExact(registrationJson["alta"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        PreviousYear previousYear = registrationJson["previousYear"] is JsonObject known
            ? new PreviousYear.RendimientoNeto(MoneyOf(known["rendimientoNeto"]))
            : new PreviousYear.NoActivity();

        var actuals = pictureJson["actuals"]!.AsArray()
            .Select(a => new QuarterToDate(Enum.Parse<Quarter>(a!["quarter"]!.GetValue<string>()), MoneyOf(a["ingresosYtd"]), MoneyOf(a["gastosYtd"])))
            .ToList();

        var projectionJson = pictureJson["projection"]!;
        var input = new SetAsideInput(
            new TaxpayerProfile(
                profileJson["region"]!.GetValue<string>(),
                new EmploymentIncome(MoneyOf(employmentJson["ingresos"]), MoneyOf(employmentJson["seguridadSocial"])),
                new AutonomoRegistration(alta, previousYear, NewActivityOf(registrationJson["newActivity"]))),
            new ActivityPicture(actuals, new ActivityProjection(MoneyOf(projectionJson["ingresos"]), MoneyOf(projectionJson["gastos"])), new Retenciones.ForeignPayersOnly()),
            TaxYearConfigFiles.Year2025,
            Enum.Parse<Quarter>(inputs["asOf"]!.GetValue<string>()));

        var result = SetAsideEstimator.Estimate(input);
        var expected = part["expected"]!;
        var mismatches = new List<string>();

        var actualByKey = new Dictionary<string, decimal>
        {
            ["holdBackShare"] = result.HoldBackShare.Value,
            ["monthlyCuotaSs"] = result.MonthlyCuotaSs.Amount,
            ["annualTrueUpGap"] = result.AnnualTrueUpGap.Amount,
            ["ivaToSetAside"] = result.IvaToSetAside.Amount,
        };

        foreach (var (key, got) in actualByKey)
        {
            var want = DecimalOf(expected[key]);
            if (got != want)
            {
                mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"{key}: expected {want}, got {got}"));
            }
        }

        var wantNextPayment = expected["nextPayment"]!;
        if (wantNextPayment["quarter"]!.GetValue<string>() != result.NextPayment.Quarter.ToString())
        {
            mismatches.Add($"nextPayment.quarter: expected {wantNextPayment["quarter"]}, got {result.NextPayment.Quarter}");
        }

        var wantAIngresar = DecimalOf(wantNextPayment["aIngresar"]);
        if (wantAIngresar != result.NextPayment.AIngresar.Amount)
        {
            mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"nextPayment.aIngresar: expected {wantAIngresar}, got {result.NextPayment.AIngresar.Amount}"));
        }

        var wantDueWindow = wantNextPayment["dueWindow"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();
        var gotDueWindow = new[] { result.NextPayment.DueWindow.Start.ToString(), result.NextPayment.DueWindow.End.ToString() };
        if (!wantDueWindow.SequenceEqual(gotDueWindow))
        {
            mismatches.Add($"nextPayment.dueWindow: expected [{string.Join(", ", wantDueWindow)}], got [{string.Join(", ", gotDueWindow)}]");
        }

        var wantPayableIn = expected["annualTrueUpPayableIn"]!.GetValue<string>();
        if (wantPayableIn != result.AnnualTrueUpPayableIn.ToString())
        {
            mismatches.Add($"annualTrueUpPayableIn: expected {wantPayableIn}, got {result.AnnualTrueUpPayableIn}");
        }

        foreach (var (id, wantNode) in expected["traceOutputs"]!.AsObject())
        {
            var want = DecimalOf(wantNode);
            var got = result.Trace.Steps.Single(s => s.Id == id).Output;
            if (want != got)
            {
                mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"traceOutputs.{id}: expected {want}, got {got}"));
            }
        }

        var wantWarnings = expected["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).ToList();
        var gotWarnings = result.Warnings.Select(w => w.Code).ToList();
        if (!wantWarnings.SequenceEqual(gotWarnings))
        {
            mismatches.Add($"warnings: expected [{string.Join(", ", wantWarnings)}], got [{string.Join(", ", gotWarnings)}]");
        }

        // Each entry names a warning code and text that one warning with that code must contain, such as the amount it reports.
        foreach (var (code, textNode) in expected["warningTexts"]!.AsObject())
        {
            var text = textNode!.GetValue<string>();
            if (!result.Warnings.Any(w => w.Code == code && w.Text.Contains(text, StringComparison.Ordinal)))
            {
                mismatches.Add($"warningTexts.{code}: no {code} warning contains \"{text}\"");
            }
        }

        if (result.ConfigHash != TaxYearConfigFiles.Year2025.ConfigHash)
        {
            mismatches.Add($"configHash: expected {TaxYearConfigFiles.Year2025.ConfigHash}, got {result.ConfigHash}");
        }

        Assert.True(mismatches.Count == 0, $"{golden} setAside:\n  " + string.Join("\n  ", mismatches));
    }
}
