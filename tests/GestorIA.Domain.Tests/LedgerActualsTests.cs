using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.TaxYears;
using GestorIA.Infrastructure.Transactions;
using Xunit;

namespace GestorIA.Domain.Tests;

public class LedgerActualsTests
{
    private static readonly TaxYearConfig Config = new TaxYearConfigLoader(Path.Combine(RepoRoot(), "config", "tax-years")).Load(2025);

    private static readonly DateOnly AfterTheYear = new(2026, 9, 28);

    // A statement reaching into the next year, so every quarter of 2025 that holds lines is covered.
    private static readonly DateOnly ThroughNextJanuary = new(2026, 1, 31);

    private static Profile Profile(DateOnly alta, decimal ingresos = 30000.00m, decimal gastos = 1200.00m) => new(
        2025,
        new TaxpayerProfile("VC", new EmploymentIncome(Money.Zero, Money.Zero), new AutonomoRegistration(alta, new PreviousYear.NoActivity(), new NewActivity.Established())),
        new ActivityProjection(new Money(ingresos), new Money(gastos), new Money(1274.51m)));

    private static readonly Profile January = Profile(new(2025, 1, 15));

    private static ClassifiedLine Line(int month, int day, decimal amount, Classification classification) =>
        new(Guid.NewGuid(), new(2025, month, day), new Money(amount), classification);

    private static Classification Confirmed(TransactionClass transactionClass) => new Classification.Confirmed(transactionClass, null);

    private static readonly Classification Income = Confirmed(TransactionClass.ActivityIncome);
    private static readonly Classification Cuota = new Classification.Confirmed(TransactionClass.SocialSecurity, "tgss");

    [Fact]
    public void WithoutLinesTheInputIsTheWholeProjectionAndNoStepIsAdded()
    {
        var ledger = LedgerActuals.Of(January, [], null, Config, Quarter.Q2, AfterTheYear);

        Assert.Equal((January.Taxpayer, Quarter.Q2), (ledger.Input.Profile, ledger.Input.AsOf));
        Assert.Empty(ledger.Input.Activity.Actuals);
        Assert.Equal(January.Projection, ledger.Input.Activity.Projection);
        Assert.IsType<Retenciones.ForeignPayersOnly>(ledger.Input.Activity.Retenciones);
        Assert.Empty(ledger.Steps);
        Assert.Equal(new LedgerCounts(null, 0, 0, 0), ledger.Counts);
    }

    [Fact]
    public void ActualsAreCumulativeSignedSumsOfConfirmedIncomeAndCuotas()
    {
        List<ClassifiedLine> lines =
        [
            Line(1, 2, 2345.67m, Income),
            Line(1, 3, -87.61m, Cuota),
            Line(2, 1, -100.00m, Income),
            Line(4, 7, 1876.54m, Income),
            Line(5, 1, 20.00m, Cuota),
            Line(5, 2, -87.61m, Cuota),
        ];

        var actuals = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q2, AfterTheYear).Input.Activity.Actuals;

        Assert.Equal(
            [
                new QuarterToDate(Quarter.Q1, new Money(2245.67m), new Money(87.61m), new Money(87.61m)),
                new QuarterToDate(Quarter.Q2, new Money(4122.21m), new Money(155.22m), new Money(155.22m)),
            ],
            actuals);
    }

    [Fact]
    public void OnlyConfirmedIncomeAndCuotasCountAndAConfirmedExpenseAwaitsAnInvoice()
    {
        List<ClassifiedLine> lines =
        [
            Line(1, 2, 1000.00m, Income),
            Line(1, 7, -30.00m, Confirmed(TransactionClass.DeductibleExpense)),
            Line(1, 8, -9.00m, Confirmed(TransactionClass.Personal)),
            Line(1, 9, 700.00m, new Classification.Confirmed(TransactionClass.SavingsIncome, "savings")),
        ];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q1, AfterTheYear);

        Assert.Equal(new QuarterToDate(Quarter.Q1, new Money(1000.00m), new Money(0.00m), new Money(0.00m)), Assert.Single(ledger.Input.Activity.Actuals));
        Assert.Equal(new LedgerCounts(Quarter.Q1, 1, 0, 1), ledger.Counts);
        var step = ledger.Steps[0];
        Assert.Equal("ledger.Q1.movements", step.Id);
        Assert.Equal(["activityIncome", "awaitingInvoice"], step.Inputs.Select(input => input.Name));
        Assert.Equal(
            "ingresos: 1 movement = 1000.00; cuotas SS: 0 movements = 0.00; gastos = cuotas SS, no expense having a confirmed invoice yet; not counted: 1 awaits an invoice",
            step.Formula);
        Assert.Equal(new TraceValue.Money(new Money(1000.00m)), step.Output);
    }

    // An unreviewed movement is unknown income, not zero: its quarter, and every one after it, stays projected.
    [Fact]
    public void AQuarterWithAMovementAwaitingReviewStopsTheActualsAndTheTraceNamesTheMovements()
    {
        List<ClassifiedLine> lines =
        [
            Line(1, 2, 1000.00m, Income),
            Line(4, 5, 500.00m, new Classification.Unclear()),
            Line(5, 6, 800.00m, Income),
            Line(8, 6, -40.00m, new Classification.Suggested(TransactionClass.DeductibleExpense, "vendors")),
            Line(11, 6, 900.00m, Income),
        ];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q4, AfterTheYear);

        Assert.Equal([Quarter.Q1], ledger.Input.Activity.Actuals.Select(actual => actual.Quarter));
        Assert.Equal(new LedgerCounts(Quarter.Q1, 1, 2, 0), ledger.Counts);
        Assert.Equal(["ledger.Q1.movements", "ledger.pending-review", "ledger.projection-remaining"], ledger.Steps.Select(step => step.Id));
        var pending = ledger.Steps[1];
        Assert.Equal([new TraceInput("awaitingReview", $"{lines[1].Id} 2025-04-05"), new TraceInput("awaitingReview", $"{lines[3].Id} 2025-08-06")], pending.Inputs);
        Assert.StartsWith("2 movements of the closed quarters through Q4 await review (Q2 1, Q3 1); ", pending.Formula, StringComparison.Ordinal);
        Assert.Equal(new TraceValue.Count(2), pending.Output);
    }

    // A statement just imported, nothing reviewed: the estimate is the projection's, as before the import, and it can be made.
    [Fact]
    public void AnImportNobodyHasReviewedLeavesTheWholeProjection()
    {
        List<ClassifiedLine> lines = [.. Enumerable.Range(0, 4).Select(q => Line(q * 3 + 1, 5, 1000.00m, new Classification.Unclear()))];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q4, AfterTheYear);

        Assert.Empty(ledger.Input.Activity.Actuals);
        Assert.Equal(January.Projection, ledger.Input.Activity.Projection);
        Assert.Equal(new LedgerCounts(null, 0, 4, 0), ledger.Counts);
        Assert.Equal("ledger.pending-review", Assert.Single(ledger.Steps).Id);
        Assert.Equal(Quarter.Q4, SetAsideEstimator.Estimate(ledger.Input).NextPayment.Quarter);
    }

    [Fact]
    public void OnlyTheClosedQuartersUpToAsOfHoldTheActualsBack()
    {
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(5, 1, 100.00m, new Classification.Unclear()), Line(8, 1, 100.00m, new Classification.Unclear())];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q2, new DateOnly(2025, 8, 10));

        Assert.Equal(new LedgerCounts(Quarter.Q1, 1, 1, 0), ledger.Counts);
    }

    [Fact]
    public void TheActualsStopAtTheFirstQuarterThatIsOpenOrAfterAsOf()
    {
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(5, 1, 100.00m, Income), Line(8, 1, 100.00m, Income)];

        var open = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q3, new DateOnly(2025, 8, 10));
        var asOf = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q1, AfterTheYear);

        Assert.Equal(Quarter.Q2, open.Counts.ActualsThrough);
        Assert.Equal(Quarter.Q1, asOf.Counts.ActualsThrough);
        Assert.DoesNotContain(open.Steps, step => step.Id == "ledger.coverage");
    }

    [Fact]
    public void AClosedQuarterWithoutLinesStopsTheActualsAndTheTraceSaysSo()
    {
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(8, 1, 100.00m, Income), Line(11, 1, 100.00m, Income)];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q4, AfterTheYear);

        Assert.Equal(Quarter.Q1, ledger.Counts.ActualsThrough);
        var coverage = Assert.Single(ledger.Steps, step => step.Id == "ledger.coverage");
        Assert.Equal([new TraceInput("quarter", "Q2"), new TraceInput("laterWithMovements", "Q3, Q4")], coverage.Inputs);
        Assert.Equal(["ledger.Q1.movements", "ledger.coverage", "ledger.projection-remaining"], ledger.Steps.Select(step => step.Id));
    }

    [Fact]
    public void WhenTheFirstQuarterHoldsNoLineTheTraceSaysWhyNothingIsActual()
    {
        List<ClassifiedLine> lines = [Line(5, 1, 100.00m, Income)];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q2, AfterTheYear);

        Assert.Null(ledger.Counts.ActualsThrough);
        Assert.Empty(ledger.Input.Activity.Actuals);
        Assert.Equal(January.Projection, ledger.Input.Activity.Projection);
        var coverage = Assert.Single(ledger.Steps);
        Assert.Equal("ledger.coverage", coverage.Id);
        Assert.Equal([new TraceInput("quarter", "Q1"), new TraceInput("laterWithMovements", "Q2")], coverage.Inputs);
    }

    // A statement exported on 22 April holds Q1 and three weeks of Q2. A quarter holding lines is not a quarter the statement
    // covers: Q2 stays projected, whatever its lines are and whoever classified them, until movements after 30 June arrive.
    [Theory]
    [InlineData("2025-04-22", null)]
    [InlineData("2025-06-30", null)]
    [InlineData("2025-07-15", Quarter.Q2)]
    public void OnlyAQuarterTheImportedMovementsReachPastIsActuals(string importedThrough, Quarter? q2)
    {
        List<ClassifiedLine> lines =
        [
            Line(1, 2, 1000.00m, Income),
            Line(4, 20, -312.88m, new Classification.Confirmed(TransactionClass.AeatPayment, "aeat")),
            Line(4, 22, -35.10m, new Classification.Confirmed(TransactionClass.Personal, "personal")),
        ];

        var ledger = LedgerActuals.Of(January, lines, DateOnly.Parse(importedThrough, System.Globalization.CultureInfo.InvariantCulture), Config, Quarter.Q4, AfterTheYear);

        Assert.Equal(q2 ?? Quarter.Q1, ledger.Counts.ActualsThrough);
        var coverage = ledger.Steps.SingleOrDefault(step => step.Id == "ledger.coverage");
        if (q2 is null)
        {
            Assert.Equal($"movements imported through {importedThrough}; Q2 needs movements after 2025-06-30, so the projection covers Q2 and what follows", coverage!.Formula);
        }
        else
        {
            Assert.Null(coverage);
        }
    }

    [Fact]
    public void TheActualsStartAtTheQuarterOfTheAlta()
    {
        var may = Profile(new(2025, 5, 10));
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(5, 20, 200.00m, Income), Line(8, 1, 300.00m, Income)];

        var ledger = LedgerActuals.Of(may, lines, ThroughNextJanuary, Config, Quarter.Q3, AfterTheYear);

        Assert.Equal([Quarter.Q2, Quarter.Q3], ledger.Input.Activity.Actuals.Select(actual => actual.Quarter));
        Assert.Equal(new Money(500.00m), ledger.Input.Activity.Actuals[^1].IngresosYtd);
        var remaining = ledger.Steps[^1];
        Assert.Equal("30000.00 × 3 / 8 = 11250.00; 1200.00 × 3 / 8 = 450.00", remaining.Formula);
    }

    // 1000.06 × 9 / 12 = 750.045: half away from zero gives 750.05 where banker's rounding would give 750.04.
    [Fact]
    public void TheProjectionLosesTheShareOfTheMonthsTheActualsCoverRoundedHalfAwayFromZero()
    {
        var profile = Profile(new(2025, 1, 15), ingresos: 1000.06m, gastos: 1200.00m);

        var ledger = LedgerActuals.Of(profile, [Line(1, 2, 100.00m, Income)], ThroughNextJanuary, Config, Quarter.Q2, AfterTheYear);

        Assert.Equal(new ActivityProjection(new Money(750.05m), new Money(900.00m), new Money(1274.51m)), ledger.Input.Activity.Projection);
        var step = Assert.Single(ledger.Steps, step => step.Id == "ledger.projection-remaining");
        Assert.Equal("1000.06 × 9 / 12 = 750.05; 1200.00 × 9 / 12 = 900.00", step.Formula);
        Assert.Equal(new TraceValue.Money(new Money(750.05m)), step.Output);
    }

    [Fact]
    public void TheEstimatorAcceptsTheInputThroughQ4WithNothingLeftToProject()
    {
        List<ClassifiedLine> lines = [.. Enumerable.Range(0, 4).Select(q => Line(q * 3 + 1, 5, 1000.00m, Income))];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q4, AfterTheYear);

        Assert.Equal(new Money(0.00m), ledger.Input.Activity.Projection.Ingresos);
        Assert.Equal(Quarter.Q4, SetAsideEstimator.Estimate(ledger.Input).NextPayment.Quarter);
    }

    [Fact]
    public void ATraceInputNamesALineByIdAndDateOnly()
    {
        List<ClassifiedLine> lines = [Line(1, 2, 2345.67m, Income), Line(1, 3, -87.61m, Cuota), Line(1, 4, -23.79m, Confirmed(TransactionClass.DeductibleExpense))];

        var ledger = LedgerActuals.Of(January, lines, ThroughNextJanuary, Config, Quarter.Q1, AfterTheYear);

        var values = ledger.Steps[0].Inputs.Select(input => input.Value).ToList();
        Assert.Equal(lines.Select(line => $"{line.Id} {line.BookingDate:yyyy-MM-dd}"), values);
        Assert.All(values, value => Assert.Matches(new Regex("^[0-9a-f-]{36} 2025-01-0[234]$"), value));
    }

    private static string RepoRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
