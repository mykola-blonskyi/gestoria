using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Infrastructure.Transactions;

public enum RuleDirection
{
    Credit,
    Debit,
    Any,
}

// One rule of config/transaction-rules.json. Patterns are held as Normalize leaves them.
public sealed record TransactionRule(string Id, TransactionClass Class, RuleDirection Direction, bool Certain, IReadOnlyList<string> Patterns, string Source);

// The classifier's rules (SPEC-004 §3), read from config/transaction-rules.json once as the API starts. The first rule that
// matches a line decides it; a certain rule confirms its class, any other only suggests it.
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

            if (document.Patterns.Count == 0 || document.Patterns.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add($"{name}: patterns must be a non-empty list of non-blank texts.");
            }

            if (failures.Count == before)
            {
                rules.Add(new TransactionRule(document.Id, transactionClass, direction!.Value, document.Certain, [.. document.Patterns.Select(Normalize)], document.Source));
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

        var text = Normalize(description);
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

    // A pattern matches where it begins a word: "AGUA" matches "RECIBO AGUA" and not "PARAGUAS".
    private static bool StartsAWord(string text, string pattern)
    {
        for (var at = text.IndexOf(pattern, StringComparison.Ordinal); at >= 0; at = text.IndexOf(pattern, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || !char.IsLetterOrDigit(text[at - 1]))
            {
                return true;
            }
        }

        return false;
    }

    // Upper case without diacritics, so "Nómina" and "NOMINA" read the same. FormD splits "Ó" into "O" and a combining accent,
    // which is then dropped.
    private static string Normalize(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();

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
