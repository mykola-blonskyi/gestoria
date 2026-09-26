namespace GestorIA.Engine.Tests;

internal static class TraceStepOutputs
{
    // Assert.IsType fails unless the output is that kind, and returns it typed.
    internal static decimal Euros(this TraceStep step) => Assert.IsType<TraceValue.Money>(step.Output).Value.Amount;

    internal static int Count(this TraceStep step) => Assert.IsType<TraceValue.Count>(step.Output).Value;
}
