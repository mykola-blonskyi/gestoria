using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Infrastructure.Transactions;
using Xunit;

namespace GestorIA.Domain.Tests;

public class LineKeysTests
{
    private static IEnumerable<string> Keys(params BankTransaction[] movements) =>
        LineKeys.Of(movements.Select((movement, index) => new StatementLine(index + 2, movement))).Select(line => line.Key);

    private static BankTransaction Coffee(decimal? balance = null) =>
        new(new(2025, 3, 4), new(2025, 3, 4), "CAFETERIA SINTETICA", new Money(-1.80m), balance is { } b ? new Money(b) : null);

    [Fact]
    public void TwoIdenticalLinesInOneFileGetTwoKeys()
    {
        var keys = Keys(Coffee(), Coffee()).ToList();

        Assert.Equal(2, keys.Distinct().Count());
    }

    [Fact]
    public void ALaterExportOfTheSameDaysGivesTheSameKeys()
    {
        var first = Keys(Coffee(10m), Coffee(8.20m));
        var again = Keys(Coffee(), Coffee(), Coffee()).ToList();

        Assert.Equal(first, again.Take(2));
        Assert.DoesNotContain(again[2], first);
    }

    [Fact]
    public void TheKeyIsTheSameWhateverTheAmountsScale()
    {
        var two = Coffee() with { Amount = new Money(-1.80m) };
        var one = Coffee() with { Amount = new Money(-1.8m) };

        Assert.Equal(Keys(two).Single(), Keys(one).Single());
    }

    [Fact]
    public void TheKeyDoesNotDependOnWhichLineOfTheFileTheMovementIsOn()
    {
        var early = LineKeys.Of([new StatementLine(2, Coffee())]).Single().Key;
        var late = LineKeys.Of([new StatementLine(40, Coffee())]).Single().Key;

        Assert.Equal(early, late);
    }

    [Fact]
    public void AnyPrintedDifferenceGivesAnotherKey()
    {
        var coffee = Coffee();
        var variants = new[]
        {
            coffee,
            coffee with { BookingDate = new(2025, 3, 5) },
            coffee with { ValueDate = new(2025, 3, 5) },
            coffee with { Description = "CAFETERIA SINTETICA 2" },
            coffee with { Amount = new Money(1.80m) },
        };

        Assert.Equal(variants.Length, variants.Select(v => Keys(v).Single()).Distinct().Count());
    }
}
