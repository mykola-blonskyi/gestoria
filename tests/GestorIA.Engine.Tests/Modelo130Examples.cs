using System.Globalization;
using System.Text.Json;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class Modelo130Examples
{
    private static readonly PreviousYear AboveEveryBand = new PreviousYear.RendimientoNeto(new Money(42400m));

    private static Modelo130Result Run(
        decimal ingresos,
        decimal gastos,
        Quarter quarter = Quarter.Q1,
        Modelo130Carry? carry = null,
        Retenciones? retenciones = null,
        PreviousYear? previousYear = null,
        TaxYearConfig? config = null) =>
        Modelo130Calculator.Pago(
            new Modelo130Input(quarter, new Money(ingresos), new Money(gastos), retenciones ?? new Retenciones.ForeignPayersOnly(), previousYear ?? AboveEveryBand, "VC"),
            carry ?? Modelo130Carry.StartOfYear,
            config ?? TaxYearConfigFiles.Year2025);

    private static TraceStep Step(Modelo130Result result, string id) => result.Trace.Steps.Single(s => s.Id == id);

    [Fact]
    public void ALossToDateIsABaseOfZero()
    {
        var result = Run(ingresos: 1000m, gastos: 3000m);

        Assert.Equal(-2000m, Step(result, "m130.rendimiento-neto").Euros());
        Assert.Equal(0m, Step(result, "m130.pago-bruto").Euros());
        Assert.Equal(Money.Zero, result.Resultado);
    }

    [Fact]
    public void PaymentIsTheRateOnTheBaseLessRetencionesEarlierPaymentsAndMinoracion()
    {
        var result = Run(
            ingresos: 30000m,
            gastos: 10000m,
            quarter: Quarter.Q2,
            carry: new Modelo130Carry(new Money(1500m), Money.Zero),
            retenciones: new Retenciones.Withheld(new Money(600m)),
            previousYear: new PreviousYear.RendimientoNeto(new Money(9500m)));

        // 0.20 × (20,000 − 1,000 difícil justificación) − 1,500 − 600 − 75
        Assert.Equal(new Money(1625m), result.Resultado);
        Assert.Equal(75m, Step(result, "m130.minoracion").Euros());
        Assert.Equal(result.Resultado, result.Casillas["resultado"]);
        Assert.Equal(new Money(75m), result.Casillas["minoracion"]);
        Assert.Equal(new Money(19000m), result.Casillas["rendimientoNeto"]);
        Assert.Equal(new Money(3800m), result.Casillas["pagoBruto"]);
        // AEAT instructions, casilla 17 = 14 − 15 − 16: casilla 16 (vivienda) and 18 (complementaria) are not modelled, so
        // casilla 17 equals casilla 19 (resultado) here, whatever quarter or scenario — asserted for a negative casilla 14 too.
        Assert.Equal(result.Resultado, result.Casillas["total"]);
    }

    [Fact]
    public void CasillasAreKeyedTheSameAsTheTaxYearsModelo130Lines()
    {
        var result = Run(ingresos: 30000m, gastos: 10000m);

        Assert.Equal(TaxYearConfigFiles.Year2025.Modelo130.Lines.Keys.ToHashSet(), result.Casillas.Keys.ToHashSet());
    }

    [Fact]
    public void MinoracionAboveCasilla12GivesANegativeResultThatPaysZeroAndIsPending()
    {
        var result = Run(ingresos: 250m, gastos: 0m, previousYear: new PreviousYear.NoActivity());

        Assert.Equal(47.50m, Step(result, "m130.casilla-12").Euros());
        Assert.Equal(new Money(-52.50m), result.Resultado);
        Assert.Equal(Money.Zero, result.AIngresar);
        Assert.Equal(new Modelo130Carry(new Money(47.50m), new Money(52.50m)), result.Carry);
        // Casilla 17 keeps casilla 14's sign here (a negative casilla 15 deduction is not applied to an already-negative
        // casilla 14, business rule 14), so it is not floored at zero: it equals the negative resultado, not AIngresar.
        Assert.Equal(new Money(-52.50m), result.Casillas["total"]);
        Assert.Equal(result.Resultado, result.Casillas["total"]);
    }

    [Fact]
    public void AQuarterWhoseLossToDateUndercutsEarlierPaymentsCarriesNothingAndTheNextQuarterCatchesUpThroughTheCumulativeBase()
    {
        var q1 = Run(ingresos: 5000m, gastos: 0m);
        var q2 = Run(ingresos: 4000m, gastos: 0m, quarter: Quarter.Q2, carry: q1.Carry);
        var q3 = Run(ingresos: 10000m, gastos: 0m, quarter: Quarter.Q3, carry: q2.Carry);

        Assert.Equal(-190m, Step(q2, "m130.casilla-07").Euros());
        Assert.Equal(0m, Step(q2, "m130.casilla-12").Euros());
        Assert.Equal(Money.Zero, q2.Resultado);
        Assert.Equal(new Modelo130Carry(new Money(950m), Money.Zero), q2.Carry);
        Assert.Equal(new Money(950m), q3.Resultado);
    }

    [Fact]
    public void RetencionesAboveThePagoBrutoCarryNothing()
    {
        var result = Run(ingresos: 5000m, gastos: 0m, retenciones: new Retenciones.Withheld(new Money(2000m)));

        Assert.Equal(-1050m, Step(result, "m130.casilla-07").Euros());
        Assert.Equal(Money.Zero, result.Resultado);
        Assert.Equal(Modelo130Carry.StartOfYear, result.Carry);
    }

    [Fact]
    public void PendingNegativesAreDeductedOnlyUpToAPositiveResult()
    {
        var result = Run(ingresos: 6000m, gastos: 0m, quarter: Quarter.Q2, carry: new Modelo130Carry(new Money(1000m), new Money(500m)));

        Assert.Equal(140m, Step(result, "m130.negativos-anteriores").Euros());
        Assert.Equal(Money.Zero, result.Resultado);
        Assert.Equal(new Modelo130Carry(new Money(1140m), new Money(360m)), result.Carry);
    }

    [Fact]
    public void ASecondNegativeQuarterAddsToThePendingNegatives()
    {
        var result = Run(ingresos: 0m, gastos: 0m, quarter: Quarter.Q3, carry: new Modelo130Carry(new Money(100m), new Money(50m)), previousYear: new PreviousYear.NoActivity());

        Assert.Equal(new Money(-100m), result.Resultado);
        Assert.Equal(new Money(150m), result.Carry.NegativosPendientes);
    }

    [Fact]
    public void DificilJustificacionIsFivePercentOfTheNetToDateInCasilla02CappedAtTheAnnualMaximum()
    {
        var belowCap = Run(ingresos: 10000m, gastos: 0m);
        var aboveCap = Run(ingresos: 50000m, gastos: 7600m);

        Assert.Equal(500m, Step(belowCap, "m130.dificil-justificacion").Euros());
        Assert.Equal(9500m, Step(belowCap, "m130.rendimiento-neto").Euros());
        Assert.Equal(2000m, Step(aboveCap, "m130.dificil-justificacion").Euros());
        Assert.Equal(40400m, Step(aboveCap, "m130.rendimiento-neto").Euros());
        Assert.Equal(new Money(8080m), aboveCap.Resultado);
    }

    [Fact]
    public void TheDificilJustificacionStepCitesTheRegulationAndTheCasilla()
    {
        var reference = Step(Run(ingresos: 10000m, gastos: 0m), "m130.dificil-justificacion").Reference;

        Assert.Contains("RD 439/2007 art. 30.2ª", reference, StringComparison.Ordinal);
        Assert.Contains("art. 110.1.a", reference, StringComparison.Ordinal);
        Assert.Contains("casilla 02", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignPayersGiveZeroRetencionesAndTheTraceSaysWhy()
    {
        var step = Step(Run(ingresos: 10000m, gastos: 0m), "m130.retenciones");

        Assert.Equal(0m, step.Euros());
        Assert.Contains(new TraceInput("payers", "EU and US only"), step.Inputs);
        Assert.Contains("SPEC-003 §0", step.Reference, StringComparison.Ordinal);
        Assert.Contains("no Spanish withholding obligation", step.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public void NoActivityLastYearCountsAsZeroAndTakesTheLowestBand()
    {
        var step = Step(Run(ingresos: 10000m, gastos: 0m, previousYear: new PreviousYear.NoActivity()), "m130.minoracion");

        Assert.Equal(100m, step.Euros());
        Assert.Contains("no activity in the previous year", step.Reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("9000", "100")]
    [InlineData("9000.01", "75")]
    [InlineData("12000", "25")]
    [InlineData("12000.01", "0")]
    [InlineData("-3000", "100")]
    public void MinoracionBandUpperBoundsAreInclusive(string previousYearNet, string minoracion)
    {
        var previous = new PreviousYear.RendimientoNeto(new Money(decimal.Parse(previousYearNet, CultureInfo.InvariantCulture)));

        var step = Step(Run(ingresos: 10000m, gastos: 0m, previousYear: previous), "m130.minoracion");

        Assert.Equal(decimal.Parse(minoracion, CultureInfo.InvariantCulture), step.Euros());
    }

    [Fact]
    public void MinoracionReducesEveryQuarterAndIsNotClawedBackByTheNext()
    {
        var q1 = Run(ingresos: 5000m, gastos: 0m, previousYear: new PreviousYear.NoActivity());
        var q2 = Run(ingresos: 10000m, gastos: 0m, quarter: Quarter.Q2, carry: q1.Carry, previousYear: new PreviousYear.NoActivity());

        Assert.Equal(new Money(850m), q1.Resultado);
        Assert.Equal(new Money(850m), q2.Resultado);
    }

    // 20 April 2025 is a Sunday and 21 April Easter Monday, a VC holiday; 30 January 2026 is a Friday.
    [Theory]
    [InlineData(Quarter.Q1, "2025-04-01", "2025-04-22")]
    [InlineData(Quarter.Q4, "2026-01-01", "2026-01-30")]
    public void TheDueWindowEndsOnAWorkingDay(Quarter quarter, string start, string end)
    {
        var window = Run(ingresos: 10000m, gastos: 0m, quarter: quarter).DueWindow;

        Assert.Equal(new DueWindow(DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture)), window);
    }

    // AEAT, instrucciones del modelo 130, casilla 19.
    [Fact]
    public void AZeroResultInQ1IsFiledNegativa()
    {
        // Default previousYear (AboveEveryBand) keeps minoracion at zero, so a zero base gives a zero resultado exactly.
        var result = Run(ingresos: 0m, gastos: 0m);

        Assert.Equal(Money.Zero, result.Resultado);
        Assert.Equal(Modelo130Filing.Negativa, result.Filing);
    }

    [Fact]
    public void ANegativeResultInQ4IsFiledNegativa()
    {
        var result = Run(ingresos: 0m, gastos: 100m, quarter: Quarter.Q4, previousYear: new PreviousYear.NoActivity());

        Assert.True(result.Resultado < Money.Zero);
        Assert.Equal(Modelo130Filing.Negativa, result.Filing);
    }

    [Fact]
    public void ANegativeResultInAnEarlierQuarterIsFiledADeducir()
    {
        var result = Run(ingresos: 0m, gastos: 100m, quarter: Quarter.Q2, previousYear: new PreviousYear.NoActivity());

        Assert.True(result.Resultado < Money.Zero);
        Assert.Equal(Modelo130Filing.ADeducir, result.Filing);
    }

    [Fact]
    public void APositiveResultIsFiledIngreso()
    {
        var result = Run(ingresos: 30000m, gastos: 10000m);

        Assert.True(result.Resultado > Money.Zero);
        Assert.Equal(Modelo130Filing.Ingreso, result.Filing);
    }

    [Fact]
    public void SameInputTwice_GivesTheSameResultAndTrace()
    {
        var first = JsonSerializer.Serialize(Run(ingresos: 33000m, gastos: 5700m, quarter: Quarter.Q3, carry: new Modelo130Carry(new Money(4240m), Money.Zero)));
        var second = JsonSerializer.Serialize(Run(ingresos: 33000m, gastos: 5700m, quarter: Quarter.Q3, carry: new Modelo130Carry(new Money(4240m), Money.Zero)));

        Assert.Equal(first, second);

        // JsonDerivedType writes the kind as "$type", so a step's output actually participates in the serialized trace (#42).
        Assert.Contains("\"$type\":\"money\"", first, StringComparison.Ordinal);
    }
}
