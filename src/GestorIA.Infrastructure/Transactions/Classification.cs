using GestorIA.Domain.Models;

namespace GestorIA.Infrastructure.Transactions;

// What a bank line is, as far as GestorIA knows (SPEC-004 §2), closed like Retenciones in the engine. Unclear: no rule matched
// and the user has not decided. Suggested: a rule matched that the user must confirm. Confirmed: the user decided (RuleId null),
// or a rule certain enough to need no one (TransactionRules). Only the user's decision is stored; the rest is computed on read.
public abstract record Classification
{
    private Classification() { }

    private protected abstract void CloseTheUnion();

    public sealed record Unclear : Classification
    {
        private protected override void CloseTheUnion() { }
    }

    public sealed record Suggested(TransactionClass Class, string RuleId) : Classification
    {
        private protected override void CloseTheUnion() { }
    }

    public sealed record Confirmed(TransactionClass Class, string? RuleId) : Classification
    {
        private protected override void CloseTheUnion() { }
    }
}
