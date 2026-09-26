namespace GestorIA.Engine;

public enum WarningSeverity
{
    Info,
    Warning,
    Error,
}

// SPEC-010 §4. Code is a key of the message catalogue in SPEC-010 §3.
public sealed record Warning(string Code, WarningSeverity Severity, string Text);

public static class WarningCodes
{
    public const string SsRegularizacionAhead = "SS_REGULARIZACION_AHEAD";
    public const string ReduccionTrabajoLost = "REDUCCION_TRABAJO_LOST";
    public const string MarginalVsEffective = "MARGINAL_VS_EFFECTIVE";

    // SPEC-010 §3.
    public const string SetAsideEstimate = "SET_ASIDE_ESTIMATE";
}