namespace GestorIA.Engine;

// SPEC-007 §3: a tax year or region the configuration does not carry is an error, never a default.
public sealed class ConfigNotFoundException : Exception
{
    // ": base(message)" hands the message to Exception's own constructor, the C# form of super(message).
    public ConfigNotFoundException(string message)
        : base(message)
    {
    }
}
