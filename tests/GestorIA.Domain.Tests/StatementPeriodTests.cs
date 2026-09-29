using System.Globalization;
using GestorIA.Infrastructure.Transactions;
using Xunit;

namespace GestorIA.Domain.Tests;

// The rule a statement's period meets at the import and at the restore (#88): it holds every movement and runs at most 31
// days past them, so a wrong period cannot cover a quarter whose income was never imported.
public class StatementPeriodTests
{
    private static readonly DateOnly Today = Day("2026-09-29");

    [Theory]
    [InlineData("2025-03-07", "2025-04-07", null)]
    [InlineData("2025-03-06", "2025-04-07", "from")]
    [InlineData("2025-04-07", "2025-05-08", null)]
    [InlineData("2025-04-07", "2025-05-09", "to")]
    [InlineData("2025-04-08", "2025-04-07", "from")]
    [InlineData("2025-04-07", "2025-04-06", "to")]
    public void APeriodRunsAtMost31DaysPastItsMovements(string from, string to, string? refused)
    {
        var refusals = StatementPeriod.Refusals(Day(from), Day(to), Day("2025-04-07"), Day("2025-04-07"), Today);

        Assert.Equal(refused is null ? [] : [refused], refusals.Select(refusal => refusal.End));
    }

    [Fact]
    public void TheReasonSaysWhyALongerStretchIsRefused()
    {
        var (end, reason) = Assert.Single(StatementPeriod.Refusals(Day("2025-01-02"), Day("2025-12-31"), Day("2025-02-03"), Day("2025-12-31"), Today));

        Assert.Equal("from", end);
        Assert.StartsWith("is 2025-01-02, 32 days before the statement's first movement on 2025-02-03; a period may run at most 31 days", reason, StringComparison.Ordinal);
        Assert.Contains("would set aside too little", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatementWithoutMovementsHasNoPeriod()
    {
        var (end, _) = Assert.Single(StatementPeriod.Refusals(Day("2025-04-01"), Day("2025-04-30"), null, null, Today));

        Assert.Null(end);
    }

    [Fact]
    public void APeriodEndsOnADayAlreadyReachedUnlessItsMovementsRunLater()
    {
        Assert.Equal(["to"], StatementPeriod.Refusals(Day("2026-09-01"), Day("2026-09-30"), Day("2026-09-01"), Day("2026-09-28"), Today).Select(refusal => refusal.End));
        Assert.Empty(StatementPeriod.Refusals(Day("2026-09-01"), Day("2026-10-02"), Day("2026-09-01"), Day("2026-10-02"), Today));
    }

    private static DateOnly Day(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);
}
