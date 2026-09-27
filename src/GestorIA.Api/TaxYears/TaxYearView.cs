using GestorIA.Engine;

namespace GestorIA.Api.TaxYears;

// What a client needs to pick a tax year and label it (SPEC-009 §2): the hash that names the configuration behind every
// answer, the regions it can compute, and the gaps it declares, which the engine refuses rather than fill (SPEC-007 §3).
public sealed record TaxYearView(int TaxYear, string ConfigHash, IReadOnlyList<RegionView> Regions, IReadOnlyList<DeclaredGap> Gaps)
{
    public static TaxYearView From(TaxYearConfig config) => new(
        config.TaxYear,
        config.ConfigHash,
        [.. config.Regions.Usable.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new RegionView(r.Key, r.Value.Name))],
        [.. DeclaredGaps(config)]);

    private static IEnumerable<DeclaredGap> DeclaredGaps(TaxYearConfig config)
    {
        if (config.SeguridadSocial.TarifaPlana.DeclaredIncomplete is { } tarifaPlana)
        {
            yield return new DeclaredGap("seguridadSocial.tarifaPlana", tarifaPlana);
        }

        if (config.Calendar.DeclaredIncomplete is { } calendar)
        {
            yield return new DeclaredGap("calendar", calendar);
        }

        foreach (var (code, note) in config.Regions.DeclaredIncomplete.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            yield return new DeclaredGap($"regions.{code}", note);
        }
    }
}

public sealed record RegionView(string Code, string Name);

public sealed record DeclaredGap(string Entry, string Note);
