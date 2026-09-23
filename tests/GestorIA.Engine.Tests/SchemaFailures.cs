using Json.Schema;

namespace GestorIA.Engine.Tests;

internal static class SchemaFailures
{
    internal static IEnumerable<string> Describe(this EvaluationResults r)
    {
        if (r.IsValid) { yield break; }

        var lines = new List<(string Loc, string Key, string Msg)>();

        foreach (var d in r.Details ?? [])
        {
            if (d.Errors is not { Count: > 0 }) { continue; }

            if (d.SchemaLocation.ToString().Contains("/if/", StringComparison.Ordinal)) { continue; }

            foreach (var e in d.Errors)
            {
                if (string.IsNullOrEmpty(e.Key)) { continue; }

                if (e.Key is "allOf" or "anyOf" or "oneOf" or "then" or "else" or "properties" or "items") { continue; }

                lines.Add((d.InstanceLocation.ToString(), e.Key, e.Value));
            }
        }

        var locations = lines.Select(l => l.Loc).ToHashSet();

        foreach (var (loc, key, msg) in lines)
        {
            // "required" and "additionalProperties" name the problem themselves, so a
            // deeper error must not hide them: a misspelled _tood re-arms the completeness
            // gate, and without this the only message left is "lines must be non-empty".
            if (key is not ("required" or "additionalProperties")
                && locations.Any(o => o.Length > loc.Length && o.StartsWith(loc + "/", StringComparison.Ordinal))) { continue; }

            yield return $"{(loc.Length == 0 ? "(root)" : loc),-32} {msg}";
        }
    }
}