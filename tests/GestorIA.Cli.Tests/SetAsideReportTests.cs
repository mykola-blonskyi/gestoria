using System.Globalization;

namespace GestorIA.Cli.Tests;

public class SetAsideReportTests
{
    [Fact]
    public void AmountsPrintWithTwoDecimalsAndTheEuroSign()
    {
        var report = Render("G15");

        Assert.Contains("  Next Modelo 130, Q3                     1220.27 €, due 2025-10-01 to 2025-10-20", report, StringComparison.Ordinal);
        Assert.Contains("  Cuota SS per month this quarter         425.85 €", report, StringComparison.Ordinal);
        Assert.Contains("  Annual return (Renta) gap               4345.62 €, payable by the end of 2026-06", report, StringComparison.Ordinal);
        Assert.Contains("  IVA to set aside                        0.00 €", report, StringComparison.Ordinal);
        Assert.Contains("  Hold back from every payment received   45.64 %", report, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingDependsOnTheCurrentCulture()
    {
        var result = RepoFiles.Estimate("G15");
        var invariant = SetAsideReport.Render(result, 2025, RepoFiles.ConfigFileName);
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            var spanish = SetAsideReport.Render(result, 2025, RepoFiles.ConfigFileName);

            Assert.Equal(invariant, spanish);
            Assert.DoesNotContain("1220,27", spanish, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TheTaxYearAndConfigHashArePrinted()
    {
        var report = Render("G15");

        Assert.Contains("GestorIA set-aside estimate, tax year 2025", report, StringComparison.Ordinal);
        Assert.Contains("  Tax year                                2025", report, StringComparison.Ordinal);
        Assert.Contains($"  Configuration                           2025.example.json, sha256 {RepoFiles.Config.ConfigHash}", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTraceIsEveryStepInOrderEachRenderedByItsOutputKind()
    {
        var result = RepoFiles.Estimate("G15");
        var report = SetAsideReport.Render(result, 2025, RepoFiles.ConfigFileName).ReplaceLineEndings("\n");

        var at = 0;
        foreach (var (step, number) in result.Trace.Steps.Select((step, index) => (step, index + 1)))
        {
            at = report.IndexOf(string.Create(CultureInfo.InvariantCulture, $"{number,3}. {step.Title}  ({step.Id})"), at, StringComparison.Ordinal);
            Assert.True(at >= 0, $"step {number} {step.Id} is missing or out of order");
            Assert.Contains("       formula: " + step.Formula, report, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"Formula\"", report, StringComparison.Ordinal);

        // Money: the result line rounds to the cent, the formula above it keeps the unrounded figure (#42).
        Assert.Contains("9036.6771", result.Trace.Steps.Single(s => s.Id == "renta.liability-on-activity").Formula, StringComparison.Ordinal);
        ResultLineIs(report, "renta.liability-on-activity", "9036.68 €");

        ResultLineIs(report, "set-aside.hold-back-share", "45.64 %");
        ResultLineIs(report, "renta.marginal-rate", "41.00 %");

        ResultLineIs(report, "set-aside.projected-months", "6");

        ResultLineIs(report, "Q3.m130.due-date", "2025-10-20");
    }

    // Ties a "result:" line to the one step it belongs to, by anchoring on that step's own header line first: a loose
    // Contains on the value alone could match a different step that happens to render the same text.
    private static void ResultLineIs(string report, string stepId, string expected)
    {
        var header = report.IndexOf("(" + stepId + ")", StringComparison.Ordinal);
        Assert.True(header >= 0, $"step {stepId} is missing");

        const string resultLabel = "\n       result:  ";
        var resultAt = report.IndexOf(resultLabel, header, StringComparison.Ordinal);
        Assert.True(resultAt >= 0, $"step {stepId} has no result line");

        var start = resultAt + resultLabel.Length;
        var end = report.IndexOf('\n', start);
        Assert.Equal(expected, report[start..end]);
    }

    [Fact]
    public void WarningsComeLastWithTheLostReduccionAndTheTrueUpGap()
    {
        var report = Render("G16");

        var estimate = report.IndexOf("\nEstimate\n", StringComparison.Ordinal);
        var warnings = report.IndexOf("\nNotices (4, 3 of them warnings): read these before relying on the figures above\n", StringComparison.Ordinal);
        Assert.True(estimate > 0 && warnings > estimate, "the warnings follow the estimate at the end of the output");
        Assert.StartsWith("GestorIA set-aside estimate, tax year 2025\n84 calculation steps first; the estimate and 4 notices (3 warnings) follow at the end.\n", report, StringComparison.Ordinal);
        Assert.Contains("  !! WARNING REDUCCION_TRABAJO_LOST\n     Activity net income of 8826.45 € is above the 6500.00 € cap on income other than employment, so the reducción por trabajo of 1194.15 € is lost entirely.", report, StringComparison.Ordinal);
        Assert.Contains("  !! WARNING MARGINAL_VS_EFFECTIVE\n", report, StringComparison.Ordinal);
        Assert.Contains("The annual return will want 1305.24 € more, payable by 2026-06-30.", report, StringComparison.Ordinal);
    }

    // 20 April 2025 is a Sunday and 21 April Easter Monday, a holiday in the Comunitat Valenciana.
    [Fact]
    public void TheDueDatePrintedIsTheWorkingDayTheDeadlineMovesTo()
    {
        var report = Render("G16");

        Assert.Contains("  Next Modelo 130, Q1                     341.32 €, due 2025-04-01 to 2025-04-22\n", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDueDateSaysLocalHolidaysAreNotApplied()
    {
        var report = Render("G16");

        Assert.Contains(
            "due 2025-04-01 to 2025-04-22\n"
                + "                                          (municipal holidays where the taxpayer lives are not applied, so the date shown can be early but never late)\n",
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsPrintBeforeInformation()
    {
        var report = Render("G16");

        var lastWarning = report.LastIndexOf("  !! WARNING ", StringComparison.Ordinal);
        var info = report.IndexOf("     INFO SET_ASIDE_ESTIMATE", StringComparison.Ordinal);
        Assert.True(info > lastWarning, "INFO follows every WARNING");
    }

    private static string Render(string golden) =>
        SetAsideReport.Render(RepoFiles.Estimate(golden), 2025, RepoFiles.ConfigFileName).ReplaceLineEndings("\n");
}
