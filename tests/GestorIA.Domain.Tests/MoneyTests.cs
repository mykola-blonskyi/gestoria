using GestorIA.Domain.ValueObjects;
using Xunit;

namespace GestorIA.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void AdditionSumsAmounts()
    {
        var result = new Money(10m) + new Money(5m);

        Assert.Equal(new Money(15m), result);
    }

    [Fact]
    public void SubtractionSubtractsAmounts()
    {
        var result = new Money(10m) - new Money(4m);

        Assert.Equal(new Money(6m), result);
    }

    [Fact]
    public void UnaryNegationFlipsSign()
    {
        var result = -new Money(10m);

        Assert.Equal(new Money(-10m), result);
    }

    [Fact]
    public void MultiplicationByRateScalesAmountWithoutImplicitRounding()
    {
        var result = new Money(10m) * new Rate(0.0333m);

        Assert.Equal(new Money(0.333m), result);
    }

    [Fact]
    public void Round2RoundsHalfAwayFromZeroAtTwoDecimalPlaces()
    {
        Assert.Equal(new Money(1.24m), new Money(1.235m).Round2());
        Assert.Equal(new Money(-1.24m), new Money(-1.235m).Round2());
    }

    [Fact]
    public void OrderingOperatorsCompareByAmount()
    {
        var smaller = new Money(1m);
        var larger = new Money(2m);

        Assert.True(smaller < larger);
        Assert.True(smaller <= larger);
        Assert.True(smaller <= new Money(1m));
        Assert.True(larger > smaller);
        Assert.True(larger >= smaller);
        Assert.True(larger >= new Money(2m));
    }

    [Fact]
    public void CompareToOrdersByAmount()
    {
        var smaller = new Money(1m);
        var larger = new Money(2m);

        Assert.True(smaller.CompareTo(larger) < 0);
        Assert.True(larger.CompareTo(smaller) > 0);
        Assert.Equal(0, smaller.CompareTo(new Money(1m)));
    }

    [Fact]
    public void EqualityTreatsEqualAmountsAsEqual()
    {
        Assert.Equal(new Money(5m), new Money(5m));
        Assert.True(new Money(5m) == new Money(5m));
    }

    [Fact]
    public void EqualityTreatsDifferentAmountsAsNotEqual()
    {
        Assert.NotEqual(new Money(5m), new Money(6m));
        Assert.True(new Money(5m) != new Money(6m));
    }

    [Fact]
    public void ZeroEqualsExplicitZero()
    {
        Assert.Equal(Money.Zero, new Money(0m));
    }
}
