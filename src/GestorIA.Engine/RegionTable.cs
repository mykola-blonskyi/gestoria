namespace GestorIA.Engine;

public sealed class RegionTable
{
    private readonly IReadOnlyDictionary<string, RegionConfig> complete;
    private readonly IReadOnlyDictionary<string, string> declaredIncomplete;

    // declaredIncomplete maps a region code to its _todo note: the file names the region but says it cannot be used yet.
    public RegionTable(IReadOnlyDictionary<string, RegionConfig> complete, IReadOnlyDictionary<string, string> declaredIncomplete)
    {
        this.complete = complete;
        this.declaredIncomplete = declaredIncomplete;
    }

    // SPEC-007 §3, SPEC-002 §7: an unusable region fails; it never falls back to another region or to the estatal scale alone.
    public RegionConfig For(string code)
    {
        if (complete.TryGetValue(code, out var region))
        {
            return region;
        }

        throw new ConfigNotFoundException(declaredIncomplete.TryGetValue(code, out var todo)
            ? $"Region {code} is declared incomplete in this configuration: {todo}"
            : $"Region {code} is not in this configuration.");
    }
}
