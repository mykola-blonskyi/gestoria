namespace GestorIA.Cli;

public sealed class InvalidInputFileException : Exception
{
    public InvalidInputFileException(string message)
        : base(message)
    {
    }
}
