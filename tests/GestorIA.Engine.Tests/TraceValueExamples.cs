using System.Globalization;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class TraceValueExamples
{
    [Theory]
    [InlineData("9019.82179", "9019.82 €")]
    [InlineData("-2000", "-2000.00 €")]
    [InlineData("0", "0.00 €")]
    public void MoneyDisplaysToTheCentWithoutRoundingTheStoredValue(string amount, string expected)
    {
        var stored = decimal.Parse(amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var value = new TraceValue.Money(new Money(stored));

        Assert.Equal(expected, value.Display());
        Assert.Equal(stored, value.Value.Amount);
    }

    [Theory]
    [InlineData("0.4559", "45.59 %")]
    [InlineData("0.409", "40.90 %")]
    public void RateDisplaysAsAPercentage(string fraction, string expected)
    {
        var value = new TraceValue.Rate(new Rate(decimal.Parse(fraction, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, value.Display());
    }

    [Fact]
    public void CountDisplaysAsAPlainInteger()
    {
        Assert.Equal("12", new TraceValue.Count(12).Display());
    }

    [Fact]
    public void DateDisplaysAsIso()
    {
        Assert.Equal("2025-04-22", new TraceValue.Date(new DateOnly(2025, 4, 22)).Display());
    }

    [Fact]
    public void DisplayAndToStringAreCultureInvariant()
    {
        TraceValue[] values =
        [
            new TraceValue.Money(new Money(9019.82179m)),
            new TraceValue.Rate(new Rate(0.409m)),
            new TraceValue.Count(12),
            new TraceValue.Date(new DateOnly(2025, 4, 22)),
        ];

        var original = CultureInfo.CurrentCulture;
        try
        {
            var invariant = values.Select(v => (v.Display(), v.ToString())).ToList();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            var esEs = values.Select(v => (v.Display(), v.ToString())).ToList();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var frFr = values.Select(v => (v.Display(), v.ToString())).ToList();

            Assert.Equal(invariant, esEs);
            Assert.Equal(invariant, frFr);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // An alta well before the tax year makes the estimator walk every quarter (SetAsideEstimator's firstQuarter is 1), so
    // Q1–Q4 each get a Modelo 130 due-date step alongside the renta one; actuals to Q3 and a Q4 projection exercise both
    // the actuals and the projected branch of the quarter-to-date steps.
    private static SetAsideInput FullEstimateInput() => new(
        new TaxpayerProfile(
            "VC",
            new EmploymentIncome(new Money(20000m), new Money(1300m)),
            new AutonomoRegistration(new DateOnly(2020, 3, 1), new PreviousYear.NoActivity(), new NewActivity.Established())),
        new ActivityPicture(
            [
                new(Quarter.Q1, new Money(7000.00m), new Money(1500.00m), new Money(1354.50m)),
                new(Quarter.Q2, new Money(14000.00m), new Money(3000.00m), new Money(2709.00m)),
                new(Quarter.Q3, new Money(21000.00m), new Money(4500.00m), new Money(4063.50m)),
            ],
            new ActivityProjection(new Money(13000.00m), new Money(400.00m)),
            new Retenciones.ForeignPayersOnly()),
        TaxYearConfigFiles.Year2025,
        Quarter.Q3);

    [Fact]
    public void EveryStepOfAFullSetAsideEstimateHasTheKindItsIdMeans()
    {
        var result = SetAsideEstimator.Estimate(FullEstimateInput());

        Assert.Contains(result.Trace.Steps, s => s.Id == "set-aside.Q1.to-date"); // actuals branch
        Assert.Contains(result.Trace.Steps, s => s.Id == "set-aside.Q4.to-date"); // projected branch
        Assert.Contains(result.Trace.Steps, s => s.Id.StartsWith("ss.", StringComparison.Ordinal)); // ss.*
        Assert.Equal(4, result.Trace.Steps.Count(s => s.Id.EndsWith(".m130.due-date", StringComparison.Ordinal))); // Qn.m130.*
        Assert.Contains(result.Trace.Steps, s => s.Id == "renta.due-date"); // renta.*
        Assert.Contains(result.Trace.Steps, s => s.Id == "set-aside.hold-back-share"); // set-aside.*

        foreach (var step in result.Trace.Steps)
        {
            var expectedKind = step.Id switch
            {
                "set-aside.months-of-activity" or "set-aside.projected-months" => typeof(TraceValue.Count),
                "renta.marginal-rate" or "set-aside.hold-back-share" => typeof(TraceValue.Rate),
                "renta.due-date" => typeof(TraceValue.Date),
                var id when id.EndsWith(".m130.due-date", StringComparison.Ordinal) => typeof(TraceValue.Date),
                _ => typeof(TraceValue.Money),
            };

            Assert.True(expectedKind.IsInstanceOfType(step.Output), $"{step.Id}: expected {expectedKind.Name}, got {step.Output.GetType().Name}");
        }
    }
}
