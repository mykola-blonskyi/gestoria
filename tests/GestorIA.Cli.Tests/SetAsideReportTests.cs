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
        Assert.Contains("  Annual return (Renta) gap               4328.76 €, payable by the end of 2026-06", report, StringComparison.Ordinal);
        Assert.Contains("  IVA to set aside                        0.00 €", report, StringComparison.Ordinal);
        Assert.Contains("  Hold back from every payment received   45.59 %", report, StringComparison.Ordinal);
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
    public void TheTraceIsEveryStepInOrderWithItsUnroundedResult()
    {
        var result = RepoFiles.Estimate("G15");
        var report = SetAsideReport.Render(result, 2025, RepoFiles.ConfigFileName);

        var at = 0;
        foreach (var (step, number) in result.Trace.Steps.Select((step, index) => (step, index + 1)))
        {
            at = report.IndexOf(string.Create(CultureInfo.InvariantCulture, $"{number,3}. {step.Title}  ({step.Id})"), at, StringComparison.Ordinal);
            Assert.True(at >= 0, $"step {number} {step.Id} is missing or out of order");
            Assert.Contains("       formula: " + step.Formula, report, StringComparison.Ordinal);
        }

        Assert.Contains("       result:  9019.82179", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Formula\"", report, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsComeLastWithTheLostReduccionAndTheTrueUpGap()
    {
        var report = Render("G16");

        var estimate = report.IndexOf("\nEstimate\n", StringComparison.Ordinal);
        var warnings = report.IndexOf("\nNotices (4, 3 of them warnings): read these before relying on the figures above\n", StringComparison.Ordinal);
        Assert.True(estimate > 0 && warnings > estimate, "the warnings follow the estimate at the end of the output");
        Assert.StartsWith("GestorIA set-aside estimate, tax year 2025\n76 calculation steps first; the estimate and 4 notices (3 warnings) follow at the end.\n", report, StringComparison.Ordinal);
        Assert.Contains("  !! WARNING REDUCCION_TRABAJO_LOST\n     Activity net income of 8358.48 € is above the 6500.00 € cap on income other than employment, so the reducción por trabajo of 1194.15 € is lost entirely.", report, StringComparison.Ordinal);
        Assert.Contains("  !! WARNING MARGINAL_VS_EFFECTIVE\n", report, StringComparison.Ordinal);
        Assert.Contains("The annual return will want 1258.44 € more, payable by 2026-06-30.", report, StringComparison.Ordinal);
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
