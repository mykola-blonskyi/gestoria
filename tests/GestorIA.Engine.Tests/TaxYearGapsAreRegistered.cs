using System.Text.Json.Nodes;

namespace GestorIA.Engine.Tests;

// A _todo note declares a block incomplete and switches off its completeness
// gate in schema.json. That is a loaded gun: the gap has to be justified here,
// and the justification has to disappear when the gap does.
public class TaxYearGapsAreRegistered
{
    private static readonly Dictionary<string, string> Registered = new()
    {
        ["/regions/MD"] =
            "Madrid is out of v1.0 scope (ADR-0013). Closes by deleting the block, not by filling it.",

        ["/modelo130"] =
            "line numbers. Due before the Q1 2027 forms (plans/current.md, Phase 0). minoracion bands filled (#5).",

        ["/modelo303"] =
            "line numbers. Due before the Q1 2027 forms (plans/current.md, Phase 0).",
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
        var found = Gaps(JsonNode.Parse(File.ReadAllText(path))!).ToHashSet();

        var unregistered = found.Except(Registered.Keys).Order().ToList();
        var stale = Registered.Keys.Except(found).Order().ToList();

        Assert.True(unregistered.Count == 0 && stale.Count == 0,
            $"{Path.GetFileName(path)}: the _todo notes in the file and the registry in "
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
