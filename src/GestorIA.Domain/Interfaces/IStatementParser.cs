using GestorIA.Domain.Models;

namespace GestorIA.Domain.Interfaces;

// One adapter per bank export format (SPEC-004 §5). Parse reads a whole statement and answers every movement in the file's
// order with the number of the line it is on, or refuses the whole file with InvalidStatementException naming each line it cannot read: a statement imported
// with a line missing would be wrong without saying so.
public interface IStatementParser
{
    // The adapter id a client names in the request, such as "bbva".
    string Bank { get; }

    IReadOnlyList<StatementLine> Parse(string text);
}

// Number counts the file's lines from 1, the header and blank lines included, as a refusal's "line N" does.
public sealed record StatementLine(int Number, BankTransaction Movement);

// Errors are keyed by where the problem is: "line 7", or "file" for the file as a whole. The messages name positions and
// expected formats and never quote the file, so they are safe to show and never carry a description or an amount.
public sealed class InvalidStatementException(IReadOnlyDictionary<string, string[]> errors) : Exception("The statement cannot be read.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
