namespace GestorIA.Infrastructure.SetAside;

// Path is the JSON path of the offending value ("$.activity.actuals[0].gastosYtd"), so a form can show the error next to
// its field; the message starts with the same path for a reader of the console.
public sealed class InvalidInputFileException : Exception
{
    public string Path { get; }

    public InvalidInputFileException(string path, string message)
        : base(message)
    {
        Path = path;
    }
}
