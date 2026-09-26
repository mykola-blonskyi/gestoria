using System.Collections.Frozen;

namespace GestorIA.Engine;

// SPEC-007 §1.1. One vocabulary with the golden oracle tiers of SPEC-011 §1.
public enum ProvenanceKind
{
    Boe,
    AeatManual,
    Tgss,
    PublishedExample,
    Theory,
}

// schema.json requires Verified for every kind except Theory, so only a Theory entry can leave it null.
public sealed record Provenance(ProvenanceKind Kind, string Ref, DateOnly? Verified);

public sealed class ProvenanceTable
{
    public IReadOnlyDictionary<string, Provenance> Entries { get; }

    public ProvenanceTable(IReadOnlyDictionary<string, Provenance> entries)
    {
        Entries = entries.ToFrozenDictionary();
    }

    // A value takes the entry of its nearest keyed ancestor: /irpf/escalaEstatal/2/rate is covered by /irpf/escalaEstatal.
    public Provenance? For(string pointer)
    {
        var owner = Entries.Keys
            .Where(key => pointer == key || pointer.StartsWith(key + "/", StringComparison.Ordinal))
            .MaxBy(key => key.Length);

        return owner is null ? null : Entries[owner];
    }
}
