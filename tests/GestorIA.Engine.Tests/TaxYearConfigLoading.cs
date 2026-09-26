using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;
using GestorIA.Infrastructure.TaxYears;
using Json.Pointer;
using static System.FormattableString;

namespace GestorIA.Engine.Tests;

// xUnit builds a fresh instance per test and calls Dispose after it, so each test gets its own temp directory.
public sealed class TaxYearConfigLoading : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("gestoria-tax-years-").FullName;

    private static byte[] Example2025Bytes() =>
        File.ReadAllBytes(Path.Combine(TaxYearConfigFiles.Root(), TaxYearConfigFiles.Example2025));

    private static JsonNode Example2025Node() => JsonNode.Parse(Example2025Bytes())!;

    private static TaxYearConfig Parse(JsonNode root) =>
        TaxYearConfigParser.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), TaxYearConfigFiles.Example2025);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void LoadingAYearReturnsItsConfigurationNamedByTheSha256OfTheFile()
    {
        var bytes = Example2025Bytes();
        File.WriteAllBytes(Path.Combine(directory, "2025.json"), bytes);

        var config = new TaxYearConfigLoader(directory).Load(2025);

        Assert.Equal(2025, config.TaxYear);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), config.ConfigHash);
    }

    [Fact]
    public void TheHashNamesTheExactBytesNotJustTheValues()
    {
        var bytes = Example2025Bytes();
        byte[] reformatted = [.. bytes, (byte)'\n'];

        var original = TaxYearConfigParser.Parse(bytes, TaxYearConfigFiles.Example2025);
        var changed = TaxYearConfigParser.Parse(reformatted, TaxYearConfigFiles.Example2025);

        Assert.Equal(original.Modelo130.Rate, changed.Modelo130.Rate);
        Assert.NotEqual(original.ConfigHash, changed.ConfigHash);
    }

    [Fact]
    public void AnUnknownYearRaisesConfigNotFound()
    {
        File.WriteAllBytes(Path.Combine(directory, "2025.json"), Example2025Bytes());

        var error = Assert.Throws<ConfigNotFoundException>(() => new TaxYearConfigLoader(directory).Load(2024));

        Assert.Contains("2024.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownRegionRaisesConfigNotFound()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => TaxYearConfigFiles.Year2025.Regions.For("CT"));

        Assert.Contains("Region CT is not in this configuration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegionTheFileDeclaresIncompleteRaisesConfigNotFound()
    {
        var error = Assert.Throws<ConfigNotFoundException>(() => TaxYearConfigFiles.Year2025.Regions.For("MD"));

        Assert.Contains("Region MD is declared incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatFailsTheSchemaIsRefusedAtLoad()
    {
        var root = Example2025Node();
        root["irpf"]!["escalaEstatal"]![1]!["rate"] = 1.5m;

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => Parse(root));

        Assert.Contains(error.Failures, f => f.Contains("/irpf/escalaEstatal/1/rate", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileThatBreaksACrossFieldRuleIsRefusedAtLoad()
    {
        var root = Example2025Node();
        var scale = root["irpf"]!["escalaEstatal"]!.AsArray();
        var second = scale[1]!.DeepClone();
        scale[1] = scale[2]!.DeepClone();
        scale[2] = second;

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => Parse(root));

        Assert.Contains(error.Failures, f => f.Contains("/irpf/escalaEstatal/2/upTo", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileThatIsNotJsonIsRefusedAtLoad()
    {
        var truncated = Example2025Bytes()[..100];

        Assert.Throws<InvalidTaxYearConfigException>(() => TaxYearConfigParser.Parse(truncated, TaxYearConfigFiles.Example2025));
    }

    [Fact]
    public void ATramoTableTheEngineCannotUseIsRefusedAtLoad()
    {
        var root = Example2025Node();
        root["seguridadSocial"]!["tramos"]![0]!["netFrom"] = 10m;

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => Parse(root));

        Assert.Contains(error.Failures, f => f.Contains("The first tramo must start at 0", StringComparison.Ordinal));
    }

    [Fact]
    public void AVerificationDateThatIsNotOnTheCalendarIsRefusedAtLoad()
    {
        var root = Example2025Node();
        root["provenance"]!["/modelo130/minoracion"]!["verified"] = "2026-02-30";

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => Parse(root));

        Assert.Contains(error.Failures, f => f.Contains("/modelo130/minoracion was verified on 2026-02-30", StringComparison.Ordinal));
    }

    [Fact]
    public void ADuplicatedKeyIsRefusedAtLoad()
    {
        var text = Encoding.UTF8.GetString(Example2025Bytes()).Replace("\"fixed\": 7302,", "\"fixed\": 7302, \"fixed\": 9999,", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => TaxYearConfigParser.Parse(Encoding.UTF8.GetBytes(text), TaxYearConfigFiles.Example2025));

        Assert.Contains(error.Failures, f => f.Contains("fixed", StringComparison.Ordinal));
    }

    [Fact]
    public void AWholeNumberWrittenAsAFractionIsRefusedAtLoad()
    {
        var root = Example2025Node();
        root["seguridadSocial"]!["tarifaPlana"]!["months"] = 12.0m;

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => Parse(root));

        Assert.Contains(error.Failures, f => f.Contains("$.seguridadSocial.tarifaPlana.months", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAmountBeyondDecimalRangeIsRefusedAtLoad()
    {
        var text = Encoding.UTF8.GetString(Example2025Bytes()).Replace("\"amount\": 80,", "\"amount\": 1e40,", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => TaxYearConfigParser.Parse(Encoding.UTF8.GetBytes(text), TaxYearConfigFiles.Example2025));

        Assert.Contains(error.Failures, f => f.Contains("$.seguridadSocial.tarifaPlana.amount", StringComparison.Ordinal));
    }

    [Fact]
    public void ACalendarDayThatDoesNotExistIsRefusedAtLoad()
    {
        var root = Example2025Node();
        root["calendar"]!["holidays"]![0] = "02-29";

        var error = Assert.Throws<InvalidTaxYearConfigException>(() => Parse(root));

        Assert.Contains(error.Failures, f => f.Contains("Calendar day 02-29 does not exist in 2025", StringComparison.Ordinal));
    }

    [Fact]
    public void TheValencianMinimoDelContribuyenteIsItsOwnAndLeavesTheStateOneAlone()
    {
        var config = TaxYearConfigFiles.Year2025;

        Assert.Equal(new Money(6105m), config.Regions.For("VC").Minimos.Contribuyente);
        Assert.Equal(new Money(5550m), config.Irpf.Minimos.Contribuyente);
    }

    // LIRPF art. 56.3: a region that approves no amounts of its own uses the state ones for its scale too.
    [Fact]
    public void ARegionWithNoMinimosOverrideTakesTheStateMinimos()
    {
        var root = Example2025Node();
        root["regions"]!["VC"]!["minimosOverride"] = null;
        var provenance = root["provenance"]!.AsObject();

        foreach (var pointer in provenance.Select(e => e.Key).Where(k => k.StartsWith("/regions/VC/minimosOverride", StringComparison.Ordinal)).ToList())
        {
            provenance.Remove(pointer);
        }

        var config = Parse(root);

        Assert.Equal(config.Irpf.Minimos, config.Regions.For("VC").Minimos);
    }

    [Fact]
    public void ATheoryOnlyValueIsDistinguishableFromAVerifiedOne()
    {
        var provenance = TaxYearConfigFiles.Year2025.Provenance;

        var rate = provenance.For("/modelo130/rate");
        var band = provenance.For("/modelo130/minoracion/2/amountPerQuarter");

        Assert.Equal(new Provenance(ProvenanceKind.Theory, "Theory §7.3, ed. 2", null), rate);
        Assert.Equal(ProvenanceKind.Boe, band?.Kind);
        Assert.Equal(new DateOnly(2026, 9, 25), band?.Verified);
    }

    [Fact]
    public void AValueVerifiedAgainstTheAeatManualIsDistinguishableFromATheoryOnlyNeighbour()
    {
        var root = Example2025Node();
        root["provenance"]!["/irpf/trabajo/reduccion"] = new JsonObject
        {
            ["kind"] = "aeat-manual",
            ["ref"] = "Manual práctico Renta 2025, cap. 3",
            ["verified"] = "2026-09-25",
        };

        var provenance = Parse(root).Provenance;

        Assert.Equal(
            new Provenance(ProvenanceKind.AeatManual, "Manual práctico Renta 2025, cap. 3", new DateOnly(2026, 9, 25)),
            provenance.For("/irpf/trabajo/reduccion/k1"));
        Assert.Equal(ProvenanceKind.Theory, provenance.For("/irpf/trabajo/otrosGastos")?.Kind);
    }

    [Fact]
    public void ProvenanceIsInheritedWholeSegmentsOnly()
    {
        Assert.Null(TaxYearConfigFiles.Year2025.Provenance.For("/irpf/minimosFamiliares"));
    }

    [Fact]
    public void EveryValueTheEstimatorReadsRoundTripsWithItsType()
    {
        var raw = Example2025Node();
        var values = ValuesTheEstimatorReads(TaxYearConfigFiles.Year2025).ToList();

        var mismatches = values
            .Select(v => (v.Pointer, v.Value, Raw: Resolve(raw, v.Pointer)))
            .Where(v => !Matches(v.Raw, v.Value))
            .Select(v => Invariant($"{v.Pointer,-44} file {v.Raw?.ToJsonString() ?? "null"}, model {v.Value?.GetType().Name ?? "null"} {v.Value}"))
            .ToList();

        Assert.True(mismatches.Count == 0, "Values changed between the file and the model:\n\n" + string.Join("\n", mismatches));
    }

    private static IEnumerable<(string Pointer, object? Value)> ValuesTheEstimatorReads(TaxYearConfig c)
    {
        yield return ("/taxYear", c.TaxYear);

        foreach (var value in ScaleValues("/irpf/escalaEstatal", c.Irpf.EscalaEstatal)) { yield return value; }

        yield return ("/irpf/minimos/contribuyente", c.Irpf.Minimos.Contribuyente);
        yield return ("/irpf/trabajo/otrosGastos", c.Irpf.Trabajo.OtrosGastos);

        var reduccion = c.Irpf.Trabajo.Reduccion;
        yield return ("/irpf/trabajo/reduccion/fixed", reduccion.Fixed);
        yield return ("/irpf/trabajo/reduccion/t1", reduccion.T1);
        yield return ("/irpf/trabajo/reduccion/t2", reduccion.T2);
        yield return ("/irpf/trabajo/reduccion/t3", reduccion.T3);
        yield return ("/irpf/trabajo/reduccion/k1", new Coefficient(reduccion.K1));
        yield return ("/irpf/trabajo/reduccion/k2", new Coefficient(reduccion.K2));
        yield return ("/irpf/trabajo/reduccion/otherIncomeCap", reduccion.OtherIncomeCap);

        yield return ("/irpf/actividad/dificilJustificacion/pct", c.Irpf.Actividad.DificilJustificacion.Pct);
        yield return ("/irpf/actividad/dificilJustificacion/max", c.Irpf.Actividad.DificilJustificacion.Max);
        yield return ("/irpf/actividad/inicioActividad/pct", c.Irpf.Actividad.InicioActividad.Pct);
        yield return ("/irpf/actividad/inicioActividad/maxRendimiento", c.Irpf.Actividad.InicioActividad.MaxRendimiento);
        yield return ("/irpf/actividad/inicioActividad/formerEmployerShare", c.Irpf.Actividad.InicioActividad.FormerEmployerShare);

        var valenciana = c.Regions.For("VC");
        yield return ("/regions/VC/name", valenciana.Name);
        yield return ("/regions/VC/minimosOverride/contribuyente", valenciana.Minimos.Contribuyente);

        foreach (var value in ScaleValues("/regions/VC/escalaAutonomica", valenciana.EscalaAutonomica)) { yield return value; }

        yield return ("/modelo130/rate", c.Modelo130.Rate);
        yield return ("/modelo130/applyDj", c.Modelo130.ApplyDj);

        yield return ("/modelo130/minoracion", new Length(c.Modelo130.Minoracion.Count));

        foreach (var (i, band) in c.Modelo130.Minoracion.Index())
        {
            yield return ($"/modelo130/minoracion/{i}/prevYearNetUpTo", band.PrevYearNetUpTo);
            yield return ($"/modelo130/minoracion/{i}/amountPerQuarter", band.AmountPerQuarter);
        }

        yield return ("/seguridadSocial/tramos", new Length(c.SeguridadSocial.Tramos.Tramos.Count));

        foreach (var (i, tramo) in c.SeguridadSocial.Tramos.Tramos.Index())
        {
            yield return ($"/seguridadSocial/tramos/{i}/name", tramo.Name);
            yield return ($"/seguridadSocial/tramos/{i}/netFrom", tramo.NetFrom);
            yield return ($"/seguridadSocial/tramos/{i}/netUpTo", tramo.NetUpTo);
            yield return ($"/seguridadSocial/tramos/{i}/cuotaMin", tramo.CuotaMin);
        }

        yield return ("/seguridadSocial/tarifaPlana/amount", c.SeguridadSocial.TarifaPlana.Amount);
        yield return ("/seguridadSocial/tarifaPlana/months", c.SeguridadSocial.TarifaPlana.Months);

        yield return ("/calendar/modelo130", new Length(c.Calendar.Modelo130.Count));

        foreach (var (i, window) in c.Calendar.Modelo130.Index())
        {
            yield return ($"/calendar/modelo130/{i}/0", window.Start);
            yield return ($"/calendar/modelo130/{i}/1", window.End);
        }

        yield return ("/calendar/renta/0", c.Calendar.Renta.Start);
        yield return ("/calendar/renta/1", c.Calendar.Renta.End);

        yield return ("/calendar/holidays", new Length(c.Calendar.Holidays.Count));

        foreach (var (i, day) in c.Calendar.Holidays.Index())
        {
            yield return ($"/calendar/holidays/{i}", day);
        }

        yield return ("/provenance", new Length(c.Provenance.Entries.Count));

        foreach (var (pointer, entry) in c.Provenance.Entries)
        {
            var key = "/provenance/" + pointer.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            yield return ($"{key}/ref", entry.Ref);

            if (entry.Verified is { } verified) { yield return ($"{key}/verified", verified); }
        }
    }

    private static IEnumerable<(string Pointer, object? Value)> ScaleValues(string pointer, Scale scale)
    {
        yield return (pointer, new Length(scale.Tranches.Count));

        foreach (var (i, tranche) in scale.Tranches.Index())
        {
            yield return ($"{pointer}/{i}/upTo", tranche.UpTo);
            yield return ($"{pointer}/{i}/rate", tranche.Rate);
        }
    }

    private static JsonNode? Resolve(JsonNode root, string pointer) =>
        JsonPointer.Parse(pointer).TryEvaluate(root, out var node)
            ? node
            : throw new InvalidOperationException($"{pointer} does not exist in {TaxYearConfigFiles.Example2025}.");

    // A bare decimal matches nothing, so an amount that is not Money or a fraction that is not a Rate fails here.
    private static bool Matches(JsonNode? raw, object? value) => value switch
    {
        null => raw is null,
        Money money => raw is not null && raw.GetValue<decimal>() == money.Amount,
        Rate rate => raw is not null && raw.GetValue<decimal>() == rate.Value,
        Coefficient coefficient => raw is not null && raw.GetValue<decimal>() == coefficient.Value,
        int number => raw is not null && raw.GetValue<int>() == number,
        bool flag => raw is not null && raw.GetValue<bool>() == flag,
        string text => raw is not null && raw.GetValue<string>() == text,
        CalendarDay day => raw is not null && raw.GetValue<string>() == day.ToString(),
        DateOnly date => raw is not null && raw.GetValue<string>() == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Length length => raw switch
        {
            JsonArray array => array.Count == length.Count,
            JsonObject obj => obj.Count == length.Count,
            _ => false,
        },
        _ => false,
    };

    // A list mapped short would otherwise pass: only the elements the model still has get compared.
    private sealed record Length(int Count);

    // K1 and K2 are neither amounts nor fractions, the one place a plain decimal is the right type.
    private sealed record Coefficient(decimal Value);
}
