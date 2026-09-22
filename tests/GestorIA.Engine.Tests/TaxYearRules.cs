using System.Globalization;
using System.Text.Json.Nodes;
using static System.FormattableString;

namespace GestorIA.Engine.Tests;

internal static class TaxYearRules
{
    internal static IEnumerable<string> Check(JsonNode root, string fileName)
    {
        foreach (var (pointer, scale) in Scales(root))
        {
            decimal? previousUpTo = null;
            decimal? previousRate = null;
            var openEnded = new List<int>();

            for (var i = 0; i < scale.Count; i++)
            {
                var tranche = scale[i] ?? throw new InvalidOperationException(
                    Invariant($"{pointer}/{i} is JSON null; schema.json should have rejected the file."));
                var upToNode = tranche["upTo"];
                decimal? upTo = upToNode?.GetValue<decimal>();
                var rate = tranche["rate"]!.GetValue<decimal>();

                if (upTo is null)
                {
                    openEnded.Add(i);
                }
                else if (previousUpTo is { } prev && upTo <= prev)
                {
                    yield return Invariant($"{pointer}/{i}/upTo is {upTo}, which is not above {prev} in tranche {i - 1}");
                }

                if (previousRate is { } pr && rate < pr)
                {
                    yield return Invariant($"{pointer}/{i}/rate is {rate}, below {pr} in tranche {i - 1}; a Spanish scale never descends");
                }

                previousUpTo = upTo ?? previousUpTo;
                previousRate = rate;
            }

            if (openEnded.Count != 1)
            {
                yield return Invariant($"{pointer} has {openEnded.Count} tranches with upTo: null, expected exactly 1");
            }
            else if (openEnded[0] != scale.Count - 1)
            {
                yield return Invariant($"{pointer}/{openEnded[0]} has upTo: null but is not the last tranche");
            }
        }

        foreach (var (pointer, window) in Windows(root["calendar"]!))
        {
            var from = Ordinal(window[0]!.GetValue<string>());
            var to = Ordinal(window[1]!.GetValue<string>());

            if (from > to)
            {
                yield return $"{pointer} runs from {window[0]} to {window[1]}, which ends before it starts";
            }
        }

        var stem = fileName.Split('.')[0];
        var taxYear = root["taxYear"]!.GetValue<int>();
        if (stem != taxYear.ToString())
        {
            yield return $"/taxYear is {taxYear} but the file is named {fileName}";
        }
    }

    private static IEnumerable<(string Pointer, JsonArray Scale)> Scales(JsonNode node, string pointer = "")
    {
        switch (node)
        {
            case JsonArray array:
                if (array.Count > 0 && array[0] is JsonObject first && first.ContainsKey("upTo") && first.ContainsKey("rate"))
                {
                    yield return (pointer, array);
                    yield break;
                }

                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        foreach (var found in Scales(item, $"{pointer}/{i}"))
                        {
                            yield return found;
                        }
                    }
                }

                break;

            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (value is { } child)
                    {
                        foreach (var found in Scales(child, $"{pointer}/{key}"))
                        {
                            yield return found;
                        }
                    }
                }

                break;
        }
    }

    // Windows are named, not sniffed: a two-element array of strings is not
    // self-identifying, and holidays would become a window the day it has two entries.
    private static IEnumerable<(string Pointer, JsonArray Window)> Windows(JsonNode calendar)
    {
        if (calendar["renta"] is JsonArray renta) { yield return ("/calendar/renta", renta); }

        if (calendar["modelo130"] is JsonArray quarters)
        {
            for (int i = 0; i < quarters.Count; i++)
            {
                if (quarters[i] is JsonArray window) { yield return ($"/calendar/modelo130/{i}", window); }
            }
        }
    }

    // "04-20" -> 420, "+1-01-30" -> 10130: comparable, and no date arithmetic needed.
    private static int Ordinal(string day)
    {
        var nextYear = day.StartsWith("+1-", StringComparison.Ordinal);
        var token = nextYear ? day[3..] : day;
        var month = int.Parse(token[..2], CultureInfo.InvariantCulture);
        var date = int.Parse(token[3..], CultureInfo.InvariantCulture);

        return (nextYear ? 10000 : 0) + (month * 100) + date;
    }
}