using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using static System.FormattableString;

namespace GestorIA.Infrastructure.TaxYears;

// Pure: the bytes of one tax-year file in, a validated TaxYearConfig out. TaxYearConfigLoader owns the file access.
public static class TaxYearConfigParser
{
    public static TaxYearConfig Parse(byte[] source, string fileName)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(source, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException e)
        {
            throw new InvalidTaxYearConfigException(fileName, [e.Message]);
        }

        try
        {
            var failures = TaxYearConfigValidator.Validate(root, fileName);

            if (failures.Count > 0)
            {
                throw new InvalidTaxYearConfigException(fileName, failures);
            }

            return Map(root!, Convert.ToHexStringLower(SHA256.HashData(source)));
        }
        // "when" filters the catch: any other exception type passes through untouched.
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Past the schema a value can still be unreadable (12.0 as a month count, 1e40 as an amount) or break an
            // invariant that Scale and TramoTable re-check when built. Either way the file is invalid, not a crash.
            throw new InvalidTaxYearConfigException(fileName, [e.Message]);
        }
    }

    internal static CalendarDay ParseDay(string token)
    {
        var nextYear = token.StartsWith("+1-", StringComparison.Ordinal);
        var monthDay = nextYear ? token[3..] : token;

        return new CalendarDay(
            nextYear ? 1 : 0,
            int.Parse(monthDay[..2], CultureInfo.InvariantCulture),
            int.Parse(monthDay[3..], CultureInfo.InvariantCulture));
    }

    private static TaxYearConfig Map(JsonNode root, string configHash)
    {
        var irpf = root["irpf"]!;
        var reduccion = irpf["trabajo"]!["reduccion"]!;
        var dificilJustificacion = irpf["actividad"]!["dificilJustificacion"]!;
        var modelo130 = root["modelo130"]!;
        var seguridadSocial = root["seguridadSocial"]!;
        var tarifaPlana = seguridadSocial["tarifaPlana"]!;
        var calendar = root["calendar"]!;
        var taxYear = root["taxYear"]!.GetValue<int>();

        return new TaxYearConfig(
            taxYear,
            configHash,
            new IrpfConfig(
                ScaleOf(irpf["escalaEstatal"]),
                new MinimosConfig(MoneyOf(irpf["minimos"]!["contribuyente"])),
                new TrabajoConfig(
                    MoneyOf(irpf["trabajo"]!["otrosGastos"]),
                    new ReduccionTrabajoConfig(
                        MoneyOf(reduccion["fixed"]),
                        MoneyOf(reduccion["t1"]),
                        MoneyOf(reduccion["t2"]),
                        MoneyOf(reduccion["t3"]),
                        reduccion["k1"]!.GetValue<decimal>(),
                        reduccion["k2"]!.GetValue<decimal>(),
                        MoneyOf(reduccion["otherIncomeCap"]))),
                new ActividadConfig(new DificilJustificacionConfig(RateOf(dificilJustificacion["pct"]), MoneyOf(dificilJustificacion["max"])))),
            RegionsOf(root["regions"]!.AsObject()),
            new Modelo130Config(
                RateOf(modelo130["rate"]),
                modelo130["applyDj"]!.GetValue<bool>(),
                [.. modelo130["minoracion"]!.AsArray().Select(band =>
                    new MinoracionBand(MoneyOf(band!["prevYearNetUpTo"]), MoneyOf(band["amountPerQuarter"])))]),
            new SeguridadSocialConfig(
                new TramoTable([.. seguridadSocial["tramos"]!.AsArray().Select(TramoOf)]),
                new TarifaPlana(MoneyOf(tarifaPlana["amount"]), tarifaPlana["months"]!.GetValue<int>())),
            new TaxCalendar(
                [.. calendar["modelo130"]!.AsArray().Select(window => WindowOf(window, taxYear))],
                WindowOf(calendar["renta"], taxYear),
                [.. calendar["holidays"]!.AsArray().Select(day => DayIn(day, taxYear))]),
            ProvenanceOf(root["provenance"]!.AsObject()));
    }

    private static RegionTable RegionsOf(JsonObject regions)
    {
        var complete = new Dictionary<string, RegionConfig>();
        var declaredIncomplete = new Dictionary<string, string>();

        foreach (var (code, region) in regions)
        {
            if (region!["_todo"] is { } todo)
            {
                declaredIncomplete[code] = todo.GetValue<string>();
            }
            else if (region["minimosOverride"] is not null)
            {
                // Not modelled yet; dropping it would apply the state mínimos to this region without saying so.
                throw new ArgumentException($"/regions/{code}/minimosOverride is set, and the loader does not read regional mínimos yet.");
            }
            else
            {
                complete[code] = new RegionConfig(region["name"]!.GetValue<string>(), ScaleOf(region["escalaAutonomica"]));
            }
        }

        return new RegionTable(complete, declaredIncomplete);
    }

    private static ProvenanceTable ProvenanceOf(JsonObject provenance)
    {
        var entries = new Dictionary<string, Provenance>();

        foreach (var (pointer, entry) in provenance)
        {
            entries[pointer] = new Provenance(
                KindOf(entry!["kind"]!.GetValue<string>()),
                entry["ref"]!.GetValue<string>(),
                VerifiedOf(pointer, entry["verified"]));
        }

        return new ProvenanceTable(entries);
    }

    private static ProvenanceKind KindOf(string kind) => kind switch
    {
        "boe" => ProvenanceKind.Boe,
        "aeat-manual" => ProvenanceKind.AeatManual,
        "tgss" => ProvenanceKind.Tgss,
        "published-example" => ProvenanceKind.PublishedExample,
        "theory" => ProvenanceKind.Theory,
        _ => throw new InvalidOperationException($"Provenance kind \"{kind}\" passed schema.json but has no ProvenanceKind; the enum and the schema have drifted."),
    };

    // schema.json checks only the dddd-dd-dd shape, which lets 2026-02-30 through.
    private static DateOnly? VerifiedOf(string pointer, JsonNode? verified)
    {
        if (verified is null)
        {
            return null;
        }

        var text = verified.GetValue<string>();

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"The provenance entry for {pointer} was verified on {text}, which is not a calendar date.");
    }

    private static Scale ScaleOf(JsonNode? scale) =>
        new([.. scale!.AsArray().Select(tranche => new Tranche(OptionalMoneyOf(tranche!["upTo"]), RateOf(tranche["rate"])))]);

    private static Tramo TramoOf(JsonNode? tramo) =>
        new(tramo!["name"]!.GetValue<string>(), MoneyOf(tramo["netFrom"]), OptionalMoneyOf(tramo["netUpTo"]), MoneyOf(tramo["cuotaMin"]));

    private static DueWindow WindowOf(JsonNode? window, int taxYear) =>
        new(DayIn(window![0], taxYear), DayIn(window[1], taxYear));

    // The schema's calendarDay pattern admits 02-30, and 02-29 exists only in some years.
    private static CalendarDay DayIn(JsonNode? token, int taxYear)
    {
        var text = token!.GetValue<string>();
        var day = ParseDay(text);
        var year = taxYear + day.YearOffset;

        return day.Day <= DateTime.DaysInMonth(year, day.Month)
            ? day
            : throw new ArgumentException(Invariant($"Calendar day {text} does not exist in {year}."));
    }

    private static Money MoneyOf(JsonNode? amount) => new(amount!.GetValue<decimal>());

    private static Money? OptionalMoneyOf(JsonNode? amount) => amount is null ? null : MoneyOf(amount);

    private static Rate RateOf(JsonNode? rate) => new(rate!.GetValue<decimal>());
}
