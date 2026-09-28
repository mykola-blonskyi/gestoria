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

// What a stored movement's description may hold, one rule for every way a movement is stored: a statement import (the
// parser applies it to each line) and a restore from an export (SPEC-009 §2.2). PostgreSQL text cannot hold NUL, a line
// break would split the line it came from and blur its key (LineKeys joins the fields with "\n"), outer spaces are trimmed
// from every field, and a lone surrogate is not text at all.
public static class StatementDescription
{
    private static readonly char[] Refused = ['\0', '\r', '\n', '\u0085', '\u2028', '\u2029', '\f', '\v'];

    public const string Rule = "holds no NUL, no line break, no leading or trailing space and only whole Unicode characters";

    public static bool IsValid(string description) =>
        description.IndexOfAny(Refused) < 0 && description == description.Trim() && WholeCharacters(description);

    // A UTF-16 surrogate is half of a character: a high one must be followed by a low one, and a low one preceded by a high one.
    private static bool WholeCharacters(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
