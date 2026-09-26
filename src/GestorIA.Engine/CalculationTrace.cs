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

public sealed record TraceStep(
    string Id,
    TraceSection Section,
    string Title,
    IReadOnlyList<TraceInput> Inputs,
    string Formula,
    decimal Output,
    string Reference);

public sealed record CalculationTrace(IReadOnlyList<TraceStep> Steps);