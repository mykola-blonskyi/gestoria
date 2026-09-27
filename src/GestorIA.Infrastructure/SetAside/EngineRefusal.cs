namespace GestorIA.Infrastructure.SetAside;

// The engine rejects an input it cannot estimate with ArgumentException or NotSupportedException and says why. The console
// and the API show that reason to whoever wrote the input.
public static class EngineRefusal
{
    public static bool Is(Exception e) => e is ArgumentException or NotSupportedException;

    // ArgumentException's Message appends " (Parameter 'input')", and ArgumentOutOfRangeException's a line with the actual
    // value. Neither means anything to whoever wrote the input, so only the reason before them is kept.
    public static string Reason(Exception e)
    {
        if (e is not ArgumentException argument || argument.ParamName is null)
        {
            return e.Message;
        }

        var firstLine = argument.Message.Split(Environment.NewLine)[0];
        var suffix = $" (Parameter '{argument.ParamName}')";

        // [..^n] is a range: the string without its last n characters, like slice(0, -n).
        return firstLine.EndsWith(suffix, StringComparison.Ordinal) ? firstLine[..^suffix.Length] : firstLine;
    }
}
