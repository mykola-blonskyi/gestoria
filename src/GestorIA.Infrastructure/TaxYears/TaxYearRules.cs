using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using static System.FormattableString;

namespace GestorIA.Infrastructure.TaxYears;

internal static partial class TaxYearRules
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

        var provenance = root["provenance"]!.AsObject();

        foreach (var (pointer, _) in provenance)
        {
            if (Resolve(root, pointer) is null)
            {
                yield return $"/provenance/{Escape(pointer)} points at {pointer}, which does not exist in this file";
            }
        }

        foreach (var (pointer, _) in Scales(root))
        {
            if (!provenance.Any(e => pointer == e.Key || pointer.StartsWith(e.Key + "/", StringComparison.Ordinal)))
            {
                yield return $"{pointer} is a tax scale with no provenance entry; add one saying where the numbers came from";
            }
        }

        var tramos = root["seguridadSocial"]!["tramos"]!.AsArray();

        for (var i = 0; i < tramos.Count; i++)
        {
            var tramo = tramos[i] ?? throw new InvalidOperationException(
                Invariant($"/seguridadSocial/tramos/{i} is JSON null; schema.json should have rejected the file."));

            var baseMin = tramo["baseMin"]!.GetValue<decimal>();
            var baseMax = tramo["baseMax"]!.GetValue<decimal>();

            if (baseMin > baseMax)
            {
                yield return Invariant($"/seguridadSocial/tramos/{i} has baseMin {baseMin} above baseMax {baseMax}");
            }

            if (i == 0) { continue; }

            var previousUpTo = tramos[i - 1]!["netUpTo"];
            var netFrom = tramo["netFrom"]!.GetValue<decimal>();

            if (previousUpTo is null)
            {
                yield return Invariant($"/seguridadSocial/tramos/{i - 1} is open-ended but is followed by another band");
            }
            else if (previousUpTo.GetValue<decimal>() != netFrom)
            {
                var previous = previousUpTo.GetValue<decimal>();

                yield return Invariant(
                    $"/seguridadSocial/tramos/{i}/netFrom is {netFrom}, leaving a gap after netUpTo {previous} in band {i - 1}; contribution bands must be contiguous");
            }
        }

        var casillas = root["casillas"]!.AsObject();

        foreach (var (index, deduccion) in root["deducciones"]!.AsArray().Index())
        {
            if (deduccion?["casilla"]?.GetValue<string>() is not { } casilla) { continue; }

            var estatal = deduccion["scope"]?.GetValue<string>() == "estatal";

            if (estatal && !casillas.ContainsKey(casilla))
            {
                yield return Invariant($"/deducciones/{index}/casilla is {casilla}, which is not a key of /casillas");
            }

            if (!estatal && !RegionalCasilla().IsMatch(casilla))
            {
                yield return Invariant($"/deducciones/{index}/casilla is {casilla}, which is not an annex identifier such as B.VC.12");
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

    // A JSON Pointer segment escapes "~" as "~0" and "/" as "~1" (RFC 6901).
    private static JsonNode? Resolve(JsonNode root, string pointer)
    {
        var node = root;

        foreach (var raw in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

            node = node switch
            {
                JsonObject obj => obj[segment],
                JsonArray array when int.TryParse(segment, CultureInfo.InvariantCulture, out var i)
                    && i >= 0 && i < array.Count => array[i],
                _ => null,
            };

            if (node is null) { return null; }
        }

        return node;
    }

    private static string Escape(string pointer) =>
        pointer.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    [GeneratedRegex(@"^[A-Z]\.[A-Z]{2}\.\d+$")]
    private static partial Regex RegionalCasilla();
}
