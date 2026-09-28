using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Infrastructure.Transactions;

public enum RuleDirection
{
    Credit,
    Debit,
    Any,
}

// One rule of config/transaction-rules.json. Patterns are held as Normalize leaves them, without a leading space.
public sealed record TransactionRule(string Id, TransactionClass Class, RuleDirection Direction, bool Certain, IReadOnlyList<string> Patterns, string Source);

// The classifier's rules (SPEC-004 §3), read from config/transaction-rules.json once as the API starts. The first rule that
// matches a line decides it; a certain rule confirms its class, any other only suggests it. Only a debit rule for a class whose
// money enters no figure of the estimate can be certain.
public sealed class TransactionRules
{
    public IReadOnlyList<TransactionRule> Rules { get; }

    private TransactionRules(IReadOnlyList<TransactionRule> rules)
    {
        Rules = rules;
    }

    public static TransactionRules Load(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

    public static TransactionRules Parse(string json, string fileName)
    {
        List<RuleDocument> documents;
        try
        {
            documents = JsonSerializer.Deserialize<List<RuleDocument>>(json, Strict)
                ?? throw new InvalidTransactionRulesException(fileName, ["The file is null; it must be a list of rules."]);
        }
        catch (JsonException e)
        {
            throw new InvalidTransactionRulesException(fileName, [e.Message]);
        }

        var failures = new List<string>();
        var rules = new List<TransactionRule>();
        foreach (var (document, index) in documents.Select((document, index) => (document, index)))
        {
            var name = string.IsNullOrWhiteSpace(document.Id) ? $"rule {index + 1}" : $"rule \"{document.Id}\"";
            var before = failures.Count;
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                failures.Add($"{name} has no id.");
            }
            else if (documents.Take(index).Any(earlier => earlier.Id == document.Id))
            {
                failures.Add($"{name} repeats an id an earlier rule has.");
            }

            if (!TransactionClassNames.TryParse(document.Class, out var transactionClass))
            {
                failures.Add($"{name}: class \"{document.Class}\" is not one of {string.Join(", ", TransactionClassNames.All)}.");
            }

            RuleDirection? direction = document.Direction switch
            {
                "credit" => RuleDirection.Credit,
                "debit" => RuleDirection.Debit,
                "any" => RuleDirection.Any,
                _ => null,
            };
            if (direction is null)
            {
                failures.Add($"{name}: direction \"{document.Direction}\" is not one of credit, debit, any.");
            }

            // Business rule 2: money coming in could be activity income, so no rule confirms a credit without the user.
            if (document.Certain && direction is RuleDirection.Credit or RuleDirection.Any)
            {
                failures.Add($"{name} is certain for money coming in; a certain rule must be a debit rule, and a rule for credits may only suggest.");
            }

            // Business rule 2 again: a class whose money enters the estimate is the user's to confirm. The debit check stays
            // too: a credit confirmed into a class that counts nowhere could still be activity income taken out of it.
            if (document.Certain && TransactionClassNames.TryParse(document.Class, out var confirmed) && LedgerActuals.Counts(confirmed))
            {
                failures.Add($"{name} is certain for {document.Class}, a class whose money enters the estimate; such a rule may only suggest.");
            }

            if (document.Patterns.Count == 0 || document.Patterns.Any(pattern => Normalize(pattern).Trim().Length == 0))
            {
                failures.Add($"{name}: patterns must be a non-empty list of texts that each hold a letter or a digit.");
            }

            if (failures.Count == before)
            {
                rules.Add(new TransactionRule(document.Id, transactionClass, direction!.Value, document.Certain, [.. document.Patterns.Select(pattern => Normalize(pattern).TrimStart())], document.Source));
            }
        }

        return failures.Count == 0 ? new TransactionRules(rules) : throw new InvalidTransactionRulesException(fileName, failures);
    }

    // decided is the user's class for the line, stored; it wins over every rule.
    public Classification Classify(TransactionClass? decided, string description, Money amount)
    {
        if (decided is { } chosen)
        {
            return new Classification.Confirmed(chosen, null);
        }

        var text = Normalize(description).Trim() + " ";
        return Rules.FirstOrDefault(rule => Applies(rule.Direction, amount) && rule.Patterns.Any(pattern => StartsAWord(text, pattern))) switch
        {
            null => new Classification.Unclear(),
            { Certain: true } rule => new Classification.Confirmed(rule.Class, rule.Id),
            var rule => new Classification.Suggested(rule.Class, rule.Id),
        };
    }

    private static bool Applies(RuleDirection direction, Money amount) => direction switch
    {
        RuleDirection.Credit => amount > Money.Zero,
        RuleDirection.Debit => amount < Money.Zero,
        _ => true,
    };

    // A pattern matches where it begins a word: "AGUA" matches "RECIBO AGUA" and not "PARAGUAS". The text ends with a space, so a
    // pattern ending in one ("DIA ") matches only a whole word, at the end of the description too, and not "DIARIO".
    private static bool StartsAWord(string text, string pattern)
    {
        for (var at = text.IndexOf(pattern, StringComparison.Ordinal); at >= 0; at = text.IndexOf(pattern, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || text[at - 1] == ' ')
            {
                return true;
            }
        }

        return false;
    }

    // Upper case without diacritics, so "Nómina" and "NOMINA" read the same, and every run of what is not a letter or a digit
    // one space, so "MOD.130" matches "MOD 130" and "BAR," ends a word as "BAR " does. FormD splits "Ó" into "O" and a combining
    // accent, which is then dropped.
    private static string Normalize(string text)
    {
        var letters = text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        return Spaces.Replace(string.Concat(letters), " ");
    }

    private static readonly Regex Spaces = new(" {2,}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private sealed record RuleDocument(string Id, string Class, string Direction, bool Certain, IReadOnlyList<string> Patterns, string Source);
}

public sealed class InvalidTransactionRulesException(string fileName, IReadOnlyList<string> failures)
    : Exception($"{fileName} is not a valid rules file:\n" + string.Join("\n", failures))
{
    public IReadOnlyList<string> Failures { get; } = failures;
}
