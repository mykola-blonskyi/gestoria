using System.Text.Json.Nodes;

namespace GestorIA.Engine.Tests;

// A _todo note declares a block incomplete and switches off its completeness
// gate in schema.json. That is a loaded gun: the gap has to be justified here,
// and the justification has to disappear when the gap does.
public class TaxYearGapsAreRegistered
{
    // Keyed by file name: each tax-year file declares its own gaps and is graded against its own registry.
    private static readonly Dictionary<string, Dictionary<string, string>> Registered = new()
    {
        [TaxYearConfigFiles.Example2025] = new()
        {
            ["/regions/MD"] =
                "Madrid is out of v1.0 scope (ADR-0013). Closes by deleting the block, not by filling it.",
        },

        [TaxYearConfigFiles.File2026] = new()
        {
            ["/seguridadSocial/tarifaPlana"] =
                "amount unpublished: no LPGE 2026, and RDL 13/2022 D.T. 5.ª stops at 2025 (#47). Closes when a norm fixes it.",

            ["/calendar"] =
                "renta window (Orden HAC, ~March 2027) and 2027 días inhábiles (AGE resolución, ~December 2026) (#47).",

            ["/casillas"] =
                "Modelo 100 casillas for 2026, fixed by the same Orden HAC, ~March 2027 (#47).",
        },
    };

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var path in TaxYearConfigFiles.All()) { data.Add(path); }
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryGapInTheFileIsRegisteredAndEveryRegisteredGapIsStillThere(string path)
    {
        var fileName = Path.GetFileName(path);

        Assert.True(Registered.TryGetValue(fileName, out var registered),
            $"{fileName} has no entry in {nameof(Registered)}. Register its gaps there, "
            + "with an empty dictionary if it has none, before this test can grade it.");

        var found = Gaps(JsonNode.Parse(File.ReadAllText(path))!).ToHashSet();

        var unregistered = found.Except(registered!.Keys).Order().ToList();
        var stale = registered.Keys.Except(found).Order().ToList();

        Assert.True(unregistered.Count == 0 && stale.Count == 0,
            $"{fileName}: the _todo notes in the file and the registry in "
            + $"{nameof(TaxYearGapsAreRegistered)} disagree.\n\n"
            + string.Join("\n", unregistered.Select(p =>
                $"  {p,-24} carries a _todo that is not registered. Add it with a reason and a deadline."))
            + (unregistered.Count > 0 && stale.Count > 0 ? "\n" : "")
            + string.Join("\n", stale.Select(p =>
                $"  {p,-24} is registered but carries no _todo. If the gap is closed, delete the entry.")));
    }

    private static IEnumerable<string> Gaps(JsonNode node, string pointer = "")
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.ContainsKey("_todo")) { yield return pointer; }

                foreach (var (key, value) in obj)
                {
                    if (value is { } child)
                    {
                        foreach (var found in Gaps(child, $"{pointer}/{key}")) { yield return found; }
                    }
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        foreach (var found in Gaps(item, $"{pointer}/{i}")) { yield return found; }
                    }
                }
                break;
        }
    }
}
