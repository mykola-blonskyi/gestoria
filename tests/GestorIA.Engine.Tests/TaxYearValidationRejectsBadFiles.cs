using System.Text.Json.Nodes;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Engine.Tests;

public class TaxYearValidationRejectsBadFiles
{
    // JSON Schema ignores unknown keywords rather than rejecting them, so a rule can
    // be written, read like it works, and validate nothing. Every rule therefore has a
    // mutation that must fail, and must fail at a named pointer.
    private static readonly Dictionary<string, (Action<JsonNode> Apply, string Pointer)> Mutations = new()
    {
        ["rate above 1"] =
            (r => r["irpf"]!["escalaEstatal"]![1]!["rate"] = 1.5m, "/irpf/escalaEstatal/1/rate"),

        ["misspelled block key"] =
            (r => Rename(r["irpf"]!.AsObject(), "escalaEstatal", "escalaEstattal"), "/irpf"),

        ["empty sources"] =
            (r => r["sources"] = new JsonArray(), "/sources"),

        ["source entry without a ref"] =
            (r => Rename(r["sources"]![0]!.AsObject(), "ref", "reff"), "/sources/0"),

        ["region VC missing"] =
            (r => Rename(r["regions"]!.AsObject(), "VC", "XX"), "/regions"),

        ["casilla key that is not four digits"] =
            (r => Rename(r["casillas"]!.AsObject(), "0003", "003"), "/casillas/003"),

        ["_todo removed while block still empty"] =
            (r => r["regions"]!["MD"]!.AsObject().Remove("_todo"), "/regions/MD"),

        ["_todo misspelled, which re-arms the gate"] =
            (r => Rename(r["modelo303"]!.AsObject(), "_todo", "_tood"), "/modelo303"),

        ["tranches out of order"] =
            (r => Swap(r["irpf"]!["escalaEstatal"]!.AsArray(), 1, 2), "/irpf/escalaEstatal/2/upTo"),

        ["two open-ended tranches"] =
            (r => r["irpf"]!["escalaEstatal"]![0]!["upTo"] = null, "/irpf/escalaEstatal"),

        ["open-ended tranche not last"] =
            (r => Swap(r["irpf"]!["escalaEstatal"]!.AsArray(), 0, 5), "/irpf/escalaEstatal/0"),

        ["rate descends inside a scale"] =
            (r => r["irpf"]!["escalaEstatal"]![4]!["rate"] = 0.01m, "/irpf/escalaEstatal/4/rate"),

        ["calendar window that ends before it starts"] =
            (r => Swap(r["calendar"]!["modelo130"]![0]!.AsArray(), 0, 1), "/calendar/modelo130/0"),

        ["Modelo 130 calendar without a window for every quarter"] =
            (r => r["calendar"]!["modelo130"]!.AsArray().RemoveAt(3), "/calendar/modelo130"),

        ["taxYear disagrees with the file name"] =
            (r => r["taxYear"] = 2024, "/taxYear"),

        ["provenance entry verified against a source but carrying no date"] =
            (r => r["provenance"]!["/irpf/escalaEstatal"]!["kind"] = "boe", "~1irpf~1escalaEstatal"),

        ["provenance kind outside the enum"] =
            (r => r["provenance"]!["/irpf/escalaEstatal"]!["kind"] = "BOE", "~1irpf~1escalaEstatal/kind"),

        ["provenance key that is not a JSON Pointer"] =
            (r => Rename(r["provenance"]!.AsObject(), "/irpf/escalaEstatal", "irpf/escalaEstatal"),
             "/provenance/irpf~1escalaEstatal"),

        ["provenance pointer at a node that does not exist"] =
            (r => Rename(r["provenance"]!.AsObject(), "/irpf/escalaEstatal", "/irpf/escalaEstatalx"),
             "/provenance/~1irpf~1escalaEstatalx"),

        ["tax scale with no provenance entry"] =
            (r => r["provenance"]!.AsObject().Remove("/regions/VC/escalaAutonomica"),
             "/regions/VC/escalaAutonomica"),

        // The config carries neither bands nor credits yet, so these rules are exercised
        // against data the mutation supplies. They arm before #5 and SPEC-006 fill the file.
        ["gap between social security bands"] =
            (r => r["seguridadSocial"]!["tramos"] = new JsonArray(Tramo(0, 670), Tramo(700, null)),
             "/seguridadSocial/tramos/1/netFrom"),

        ["social security band with baseMin above baseMax"] =
            (r => r["seguridadSocial"]!["tramos"] = new JsonArray(Band(0, null, min: 900, max: 700)),
             "/seguridadSocial/tramos/0"),

        ["estatal credit citing a casilla that does not exist"] =
            (r => r["deducciones"] = new JsonArray(Deduccion("estatal", "9999")),
             "/deducciones/0/casilla"),

        ["regional credit citing a state-style casilla"] =
            (r => r["deducciones"] = new JsonArray(Deduccion("autonomica", "0003")),
             "/deducciones/0/casilla"),

        ["regional mínimos override missing a value"] =
            (r => r["regions"]!["VC"]!["minimosOverride"]!.AsObject().Remove("menor3"), "/regions/VC/minimosOverride"),

        ["regional mínimos override with a key the loader does not know"] =
            (r => Rename(r["regions"]!["VC"]!["minimosOverride"]!.AsObject(), "contribuyente", "contribuyentes"),
             "/regions/VC/minimosOverride"),

        ["minoracion band with a misspelled property"] =
            (r => Rename(r["modelo130"]!["minoracion"]![0]!.AsObject(), "amountPerQuarter", "amountPerQuater"),
             "/modelo130/minoracion/0"),
    };

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Mutations.Keys) { data.Add(name); }
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void MutatedFileIsRejectedWithAMessageNamingThePath(string name)
    {
        var path = TaxYearConfigFiles.All().First();
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        var (apply, pointer) = Mutations[name];

        apply(root);
        var failures = TaxYearConfigValidator.Validate(root, Path.GetFileName(path));

        Assert.True(failures.Count > 0,
            $"Mutation \"{name}\" was accepted. The rule meant to catch it is not running.");

        Assert.True(failures.Any(f => f.Contains(pointer, StringComparison.Ordinal)),
            $"Mutation \"{name}\" was rejected, but no message names {pointer}:\n\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void TheUnmutatedFileIsAccepted()
    {
        var path = TaxYearConfigFiles.All().First();
        var root = JsonNode.Parse(File.ReadAllText(path))!;

        Assert.Empty(TaxYearConfigValidator.Validate(root, Path.GetFileName(path)));
    }

    private static JsonObject Tramo(decimal from, decimal? upTo) => Band(from, upTo, min: 653.59m, max: 718.94m);

    private static JsonObject Band(decimal from, decimal? upTo, decimal min, decimal max) => new()
    {
        ["name"] = "Reducida 1",
        ["netFrom"] = from,
        ["netUpTo"] = upTo,
        ["baseMin"] = min,
        ["baseMax"] = max,
        ["cuotaMin"] = 205m,
        ["cuotaMinKind"] = "approximate",
    };

    private static JsonObject Deduccion(string scope, string casilla) => new()
    {
        ["id"] = "TEST",
        ["scope"] = scope,
        ["casilla"] = casilla,
    };

    private static void Rename(JsonObject obj, string from, string to)
    {
        var value = obj[from];
        obj.Remove(from);
        obj[to] = value;
    }

    private static void Swap(JsonArray array, int i, int j)
    {
        var a = array[i]!.DeepClone();
        var b = array[j]!.DeepClone();
        array[i] = b;
        array[j] = a;
    }
}
