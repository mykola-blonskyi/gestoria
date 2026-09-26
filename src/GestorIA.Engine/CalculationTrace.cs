using System.Globalization;
using System.Text.Json.Serialization;

namespace GestorIA.Engine;

// SPEC-002 §6. Section names are the IRPF ones plus SeguridadSocial for the TGSS cuota and Modelo130 for the quarterly advance.
public enum TraceSection
{
    Trabajo,
    Actividad,
    Ahorro,
    Inmuebles,
    Bases,
    Minimo,
    Cuota,
    Deducciones,
    Resultado,
    SeguridadSocial,
    Modelo130,
}

public sealed record TraceInput(string Name, string Value);

// SPEC-002 §6: a step's output says what it is, so a renderer can tell euros from rates, counts and dates (#42). The private
// constructor closes the union to the kinds nested here. Values are stored unrounded (SPEC-002 §5); Display only formats them.
// JsonDerivedType writes the kind as "$type" when a trace is serialized, so the kind survives serialization.
[JsonDerivedType(typeof(Money), "money")]
[JsonDerivedType(typeof(Rate), "rate")]
[JsonDerivedType(typeof(Count), "count")]
[JsonDerivedType(typeof(Date), "date")]
public abstract record TraceValue
{
    private TraceValue() { }

    // SPEC-010 §5, culture-invariant. Abstract, so a new kind does not compile until it says how it displays.
    public abstract string Display();

    public sealed record Money(Domain.ValueObjects.Money Value) : TraceValue
    {
        public override string Display() => Value.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";
    }

    public sealed record Rate(Domain.ValueObjects.Rate Value) : TraceValue
    {
        // The "%" in a custom format multiplies by 100 as it formats; the stored Rate is unchanged.
        public override string Display() => Value.Value.ToString("0.00 %", CultureInfo.InvariantCulture);
    }

    public sealed record Count(int Value) : TraceValue
    {
        public override string Display() => Value.ToString(CultureInfo.InvariantCulture);
    }

    public sealed record Date(DateOnly Value) : TraceValue
    {
        public override string Display() => Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // The synthesized record ToString would print the DateOnly in the current culture.
        public override string ToString() => "Date " + Display();
    }
}

public sealed record TraceStep(
    string Id,
    TraceSection Section,
    string Title,
    IReadOnlyList<TraceInput> Inputs,
    string Formula,
    TraceValue Output,
    string Reference);

public sealed record CalculationTrace(IReadOnlyList<TraceStep> Steps);