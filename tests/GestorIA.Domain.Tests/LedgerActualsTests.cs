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

    // One statement from December 2024 to January 2026, so every quarter of 2025 that holds lines is covered.
    private static readonly IReadOnlyList<StatementPeriod> WholeYear = [Period("2024-12-01", "2026-01-31")];

    // A stored movement on the 28th of every month WholeYear covers, so no stretch without one runs past 31 days.
    private static readonly IReadOnlyList<DateOnly> EveryMonth = [.. Enumerable.Range(0, 14).Select(month => new DateOnly(2024, 12, 28).AddMonths(month))];

    private static IReadOnlyList<DateOnly> Days(IEnumerable<DateOnly> days, params string[] more) =>
        [.. days.Concat(more.Select(Day)).Distinct().Order()];

    private static DateOnly Day(string day) => DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture);

    private static StatementPeriod Period(string from, string to) =>
        new(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

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
        var ledger = LedgerActuals.Of(January, [], [], EveryMonth, Config, Quarter.Q2, AfterTheYear);

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

        var actuals = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q2, AfterTheYear).Input.Activity.Actuals;

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

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q1, AfterTheYear);

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

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q4, AfterTheYear);

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

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q4, AfterTheYear);

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

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q2, new DateOnly(2025, 8, 10));

        Assert.Equal(new LedgerCounts(Quarter.Q1, 1, 1, 0), ledger.Counts);
    }

    [Fact]
    public void TheActualsStopAtTheFirstQuarterThatIsOpenOrAfterAsOf()
    {
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(5, 1, 100.00m, Income), Line(8, 1, 100.00m, Income)];

        var open = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q3, new DateOnly(2025, 8, 10));
        var asOf = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q1, AfterTheYear);

        Assert.Equal(Quarter.Q2, open.Counts.ActualsThrough);
        Assert.Equal(Quarter.Q1, asOf.Counts.ActualsThrough);
        Assert.DoesNotContain(open.Steps, step => step.Id == "ledger.coverage");
    }

    [Fact]
    public void AClosedQuarterWithoutLinesStopsTheActualsAndTheTraceSaysSo()
    {
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(8, 1, 100.00m, Income), Line(11, 1, 100.00m, Income)];

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q4, AfterTheYear);

        Assert.Equal(Quarter.Q1, ledger.Counts.ActualsThrough);
        var coverage = Assert.Single(ledger.Steps, step => step.Id == "ledger.coverage");
        Assert.Equal([new TraceInput("quarter", "Q2"), new TraceInput("laterWithMovements", "Q3, Q4")], coverage.Inputs);
        Assert.Equal(["ledger.Q1.movements", "ledger.coverage", "ledger.projection-remaining"], ledger.Steps.Select(step => step.Id));
    }

    [Fact]
    public void WhenTheFirstQuarterHoldsNoLineTheTraceSaysWhyNothingIsActual()
    {
        List<ClassifiedLine> lines = [Line(5, 1, 100.00m, Income)];

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q2, AfterTheYear);

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
    public void OnlyAQuarterTheStatementsReachPastIsActuals(string to, Quarter? q2)
    {
        List<ClassifiedLine> lines =
        [
            Line(1, 20, 1000.00m, Income),
            Line(4, 20, -312.88m, new Classification.Confirmed(TransactionClass.AeatPayment, "aeat")),
            Line(4, 22, -35.10m, new Classification.Confirmed(TransactionClass.Personal, "personal")),
        ];

        var ledger = LedgerActuals.Of(January, lines, [Period("2025-01-10", to)], EveryMonth, Config, Quarter.Q4, AfterTheYear);

        Assert.Equal(q2 ?? Quarter.Q1, ledger.Counts.ActualsThrough);
        var coverage = ledger.Steps.SingleOrDefault(step => step.Id == "ledger.coverage");
        if (q2 is null)
        {
            var gap = DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture).AddDays(1);
            Assert.Equal($"no imported statement's period covers the days from {gap:yyyy-MM-dd} on; Q2 stays on the projection, with what follows, until statements whose periods cover its days from 2025-04-01 through 2025-07-01 are imported", coverage!.Formula);
        }
        else
        {
            Assert.Null(coverage);
        }
    }

    // A first statement starting on 15 February lacks January, and a gap between two statements' periods lacks those days: a quarter missing
    // days stays projected, and the trace names the days. Two periods that meet cover it together.
    [Theory]
    [InlineData("2025-02-15", "2025-12-31", null, null, "Q1", "no imported statement's period covers the days from 2025-01-15 through 2025-02-14; Q1 stays on the projection, with what follows, until statements whose periods cover its days from 2025-01-15 through 2025-04-01 are imported")]
    [InlineData("2025-01-02", "2025-02-28", "2025-06-01", "2025-12-31", "Q1", "no imported statement's period covers the days from 2025-03-01 through 2025-05-31; Q1 stays on the projection, with what follows, until statements whose periods cover its days from 2025-01-15 through 2025-04-01 are imported")]
    [InlineData("2025-01-02", "2025-04-30", "2025-06-01", "2025-12-31", "Q2", "no imported statement's period covers the days from 2025-05-01 through 2025-05-31; Q2 stays on the projection, with what follows, until statements whose periods cover its days from 2025-04-01 through 2025-07-01 are imported")]
    [InlineData("2025-01-02", "2025-03-31", "2025-04-01", "2025-12-31", null, null)]
    public void AQuarterIsActualsOnlyWhenTheStatementsCoverEveryDayOfIt(string firstFrom, string firstTo, string? secondFrom, string? secondTo, string? projected, string? why)
    {
        List<ClassifiedLine> lines = [Line(2, 20, 1000.00m, Income), Line(5, 20, 1000.00m, Income), Line(8, 20, 1000.00m, Income)];
        List<StatementPeriod> statements = [Period(firstFrom, firstTo), .. secondFrom is null ? [] : new[] { Period(secondFrom, secondTo!) }];

        var ledger = LedgerActuals.Of(January, lines, statements, EveryMonth, Config, Quarter.Q3, AfterTheYear);

        if (projected is null)
        {
            Assert.Equal(Quarter.Q3, ledger.Counts.ActualsThrough);
            Assert.DoesNotContain(ledger.Steps, step => step.Id == "ledger.coverage");
        }
        else
        {
            Assert.Equal(projected == "Q1" ? null : Quarter.Q1, ledger.Counts.ActualsThrough);
            Assert.StartsWith(why!, Assert.Single(ledger.Steps, step => step.Id == "ledger.coverage").Formula, StringComparison.Ordinal);
        }
    }

    // The uncovered days read against the statements' periods, which the step names in date order (#88).
    [Fact]
    public void TheCoverageStepNamesEveryStatementsPeriod()
    {
        var ledger = LedgerActuals.Of(January, [Line(2, 20, 1000.00m, Income)], [Period("2025-03-10", "2025-12-31"), Period("2025-01-02", "2025-02-28")], EveryMonth, Config, Quarter.Q1, AfterTheYear);

        var coverage = Assert.Single(ledger.Steps, step => step.Id == "ledger.coverage");
        Assert.StartsWith("no imported statement's period covers the days from 2025-03-01 through 2025-03-09;", coverage.Formula, StringComparison.Ordinal);
        Assert.Equal(["2025-01-02..2025-02-28", "2025-03-10..2025-12-31"], coverage.Inputs.Where(input => input.Name == "statementPeriod").Select(input => input.Value));
    }

    // A statement dated in the last year there is has no next day for another period to meet; the periods still join.
    [Fact]
    public void APeriodEndingOnTheLastDayThereIsStillJoinsTheOthers()
    {
        var ledger = LedgerActuals.Of(January, [Line(2, 20, 1000.00m, Income)], [Period("2025-01-02", "9999-12-31"), Period("9999-12-31", "9999-12-31"), Period("2025-01-10", "2025-02-01")], EveryMonth, Config, Quarter.Q1, AfterTheYear);

        Assert.Equal(Quarter.Q1, ledger.Counts.ActualsThrough);
    }

    // A taxpayer who registered on 10 May can have no activity before it: Q2 needs movements from the alta on.
    [Fact]
    public void CoverageOfTheAltasQuarterStartsAtTheAlta()
    {
        var may = Profile(new(2025, 5, 10));
        List<ClassifiedLine> lines = [Line(5, 20, 1000.00m, Income)];

        var fromAlta = LedgerActuals.Of(may, lines, [Period("2025-05-10", "2025-07-05")], EveryMonth, Config, Quarter.Q2, AfterTheYear);
        var afterAlta = LedgerActuals.Of(may, lines, [Period("2025-05-11", "2025-07-05")], EveryMonth, Config, Quarter.Q2, AfterTheYear);

        Assert.Equal(Quarter.Q2, fromAlta.Counts.ActualsThrough);
        Assert.Null(afterAlta.Counts.ActualsThrough);
    }

    [Fact]
    public void TheActualsStartAtTheQuarterOfTheAlta()
    {
        var may = Profile(new(2025, 5, 10));
        List<ClassifiedLine> lines = [Line(2, 1, 100.00m, Income), Line(5, 20, 200.00m, Income), Line(8, 1, 300.00m, Income)];

        var ledger = LedgerActuals.Of(may, lines, WholeYear, EveryMonth, Config, Quarter.Q3, AfterTheYear);

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

        var ledger = LedgerActuals.Of(profile, [Line(1, 2, 100.00m, Income)], WholeYear, EveryMonth, Config, Quarter.Q2, AfterTheYear);

        Assert.Equal(new ActivityProjection(new Money(750.05m), new Money(900.00m), new Money(1274.51m)), ledger.Input.Activity.Projection);
        var step = Assert.Single(ledger.Steps, step => step.Id == "ledger.projection-remaining");
        Assert.Equal("1000.06 × 9 / 12 = 750.05; 1200.00 × 9 / 12 = 900.00", step.Formula);
        Assert.Equal(new TraceValue.Money(new Money(750.05m)), step.Output);
    }

    [Fact]
    public void TheEstimatorAcceptsTheInputThroughQ4WithNothingLeftToProject()
    {
        List<ClassifiedLine> lines = [.. Enumerable.Range(0, 4).Select(q => Line(q * 3 + 1, 5, 1000.00m, Income))];

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q4, AfterTheYear);

        Assert.Equal(new Money(0.00m), ledger.Input.Activity.Projection.Ingresos);
        Assert.Equal(Quarter.Q4, SetAsideEstimator.Estimate(ledger.Input).NextPayment.Quarter);
    }

    [Fact]
    public void ATraceInputNamesALineByIdAndDateOnly()
    {
        List<ClassifiedLine> lines = [Line(1, 2, 2345.67m, Income), Line(1, 3, -87.61m, Cuota), Line(1, 4, -23.79m, Confirmed(TransactionClass.DeductibleExpense))];

        var ledger = LedgerActuals.Of(January, lines, WholeYear, EveryMonth, Config, Quarter.Q1, AfterTheYear);

        var values = ledger.Steps[0].Inputs.Select(input => input.Value).ToList();
        Assert.Equal(lines.Select(line => $"{line.Id} {line.BookingDate:yyyy-MM-dd}"), values);
        Assert.All(values, value => Assert.Matches(new Regex("^[0-9a-f-]{36} 2025-01-0[234]$"), value));
    }

    // #94's example: A holds lines to 31 March and is stated to 1 May, B holds lines from 1 June and is stated from 1 May. Each
    // period is within 31 days of its lines and together they cover Q2, yet no statement holds April or May.
    [Fact]
    public void TwoPeriodsChainedOverMonthsNoStatementHoldsLeaveTheQuarterProjected()
    {
        List<ClassifiedLine> lines = [Line(2, 20, 1000.00m, Income), Line(6, 1, 1000.00m, Income), Line(8, 20, 1000.00m, Income)];
        var days = Days(EveryMonth.Where(day => day < new DateOnly(2025, 4, 1) || day > new DateOnly(2025, 6, 1)), "2025-02-20", "2025-03-31", "2025-06-01", "2025-08-20");

        var ledger = LedgerActuals.Of(January, lines, [Period("2024-12-01", "2025-05-01"), Period("2025-05-01", "2026-01-31")], days, Config, Quarter.Q3, AfterTheYear);

        Assert.Equal(Quarter.Q1, ledger.Counts.ActualsThrough);
        var coverage = Assert.Single(ledger.Steps, step => step.Id == "ledger.coverage");
        Assert.Equal(
            "no stored movement from 2025-04-01 through 2025-05-31 (61 days, over 31), though the statements' periods cover those days; "
                + "Q2 stays on the projection, with what follows, until the statement holding that stretch's movements is imported",
            coverage.Formula);
        Assert.Equal([new TraceInput("quarter", "Q2"), new TraceInput("quietFrom", "2025-04-01"), new TraceInput("quietThrough", "2025-05-31")], coverage.Inputs);
        Assert.Equal(new TraceValue.Count(61), coverage.Output);
    }

    // One statement covering the year whose movements skip a stretch: 31 days without one is a month's RETA cuota apart, 32 is
    // a statement missing.
    [Theory]
    [InlineData("2025-08-11", null)]
    [InlineData("2025-08-12", "no stored movement from 2025-07-11 through 2025-08-11 (32 days, over 31)")]
    public void OneStatementWithAQuietStretchOver31DaysLeavesItsQuarterProjected(string after, string? quiet)
    {
        List<ClassifiedLine> lines = [Line(2, 28, 1000.00m, Income), Line(5, 28, 1000.00m, Income), Line(7, 10, 1000.00m, Income)];
        var days = Days(EveryMonth.Where(day => day.Month != 7), "2025-07-10", after);

        var ledger = LedgerActuals.Of(January, lines, WholeYear, days, Config, Quarter.Q3, AfterTheYear);

        Assert.Equal(quiet is null ? Quarter.Q3 : Quarter.Q2, ledger.Counts.ActualsThrough);
        var coverage = ledger.Steps.SingleOrDefault(step => step.Id == "ledger.coverage");
        if (quiet is null)
        {
            Assert.Null(coverage);
        }
        else
        {
            Assert.StartsWith(quiet, coverage!.Formula, StringComparison.Ordinal);
        }
    }

    // A stretch counts whole in each quarter it runs into: six quiet days of March and twenty-six of April hold Q1 back.
    [Theory]
    [InlineData("2025-04-26", Quarter.Q2)]
    [InlineData("2025-04-27", null)]
    public void AQuietStretchRunningIntoTheNextQuarterHoldsBackTheOneItStartsIn(string after, Quarter? actualsThrough)
    {
        List<ClassifiedLine> lines = [Line(2, 28, 1000.00m, Income), Line(5, 28, 1000.00m, Income)];
        var days = Days(EveryMonth.Where(day => day.Month is not (3 or 4) || day.Year != 2025), "2025-03-25", after);

        var ledger = LedgerActuals.Of(January, lines, WholeYear, days, Config, Quarter.Q2, AfterTheYear);

        Assert.Equal(actualsThrough, ledger.Counts.ActualsThrough);
        if (actualsThrough is null)
        {
            Assert.StartsWith("no stored movement from 2025-03-26 through 2025-04-26 (32 days, over 31)", Assert.Single(ledger.Steps, step => step.Id == "ledger.coverage").Formula, StringComparison.Ordinal);
        }
    }

    // An honest account: the RETA cuota charged on the last business day of every month of 2025, and nothing else.
    [Fact]
    public void AStatementWithTheMonthlyCuotaAloneIsActuals()
    {
        string[] lastBusinessDays = ["2025-01-31", "2025-02-28", "2025-03-31", "2025-04-30", "2025-05-30", "2025-06-30", "2025-07-31", "2025-08-29", "2025-09-30", "2025-10-31"];
        List<ClassifiedLine> lines = [.. lastBusinessDays.Select(day => new ClassifiedLine(Guid.NewGuid(), Day(day), new Money(-87.61m), Cuota))];

        var ledger = LedgerActuals.Of(January, lines, [Period("2024-12-01", "2025-10-31")], Days([], ["2024-12-31", .. lastBusinessDays]), Config, Quarter.Q3, AfterTheYear);

        Assert.Equal(Quarter.Q3, ledger.Counts.ActualsThrough);
        Assert.DoesNotContain(ledger.Steps, step => step.Id == "ledger.coverage");
    }

    // No cuota is charged before the alta, so a quiet stretch starts there: none before 10 May counts, and the one from it to
    // the first movement does.
    [Theory]
    [InlineData("2025-03-01", "2025-06-10", Quarter.Q2)]
    [InlineData("2025-05-10", "2025-06-10", Quarter.Q2)]
    [InlineData("2025-05-10", "2025-06-11", null)]
    public void AQuietStretchStartsNoEarlierThanTheAlta(string from, string first, Quarter? actualsThrough)
    {
        var may = Profile(new(2025, 5, 10));
        List<ClassifiedLine> lines = [new(Guid.NewGuid(), Day(first), new Money(1000.00m), Income)];

        var ledger = LedgerActuals.Of(may, lines, [Period(from, "2025-07-05")], Days([], first, "2025-07-01"), Config, Quarter.Q2, AfterTheYear);

        Assert.Equal(actualsThrough, ledger.Counts.ActualsThrough);
    }

    private static string RepoRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
