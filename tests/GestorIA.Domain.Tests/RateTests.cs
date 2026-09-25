using System.Globalization;
using GestorIA.Domain.ValueObjects;
using Xunit;

namespace GestorIA.Domain.Tests;

public class RateTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.21")]
    [InlineData("0.0333")]
    public void ValuesInsideZeroToOneConstructSuccessfully(string value)
    {
        var parsed = decimal.Parse(value, CultureInfo.InvariantCulture);

        var rate = new Rate(parsed);

        Assert.Equal(parsed, rate.Value);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-1")]
    [InlineData("1.01")]
    [InlineData("2")]
    public void ValuesOutsideZeroToOneThrow(string value)
    {
        var parsed = decimal.Parse(value, CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Rate(parsed));
    }

    [Fact]
    public void ToStringPrintsTheUnroundedValue()
    {
        Assert.Equal("0.0333", new Rate(0.0333m).ToString());
    }

    [Fact]
    public void ToStringIsNotAffectedByCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");

            Assert.Equal("0.21", new Rate(0.21m).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
