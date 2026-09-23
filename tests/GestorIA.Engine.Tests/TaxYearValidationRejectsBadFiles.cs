using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace GestorIA.Engine.Tests;

public class TaxYearValidationRejectsBadFiles
{
    private static readonly Dictionary<string, Action<JsonNode>> Mutations = new()
    {
        ["rate above 1"] =
            r => r["irpf"]!["escalaEstatal"]![1]!["rate"] = 1.5m,

        ["misspelled block key"] =
            r => Rename(r["irpf"]!.AsObject(), "escalaEstatal", "escalaEstattal"),

        ["empty sources"] =
            r => r["sources"] = new JsonArray(),

        ["region VC missing"] =
            r => Rename(r["regions"]!.AsObject(), "VC", "XX"),

        ["_todo removed while block still empty"] =
            r => r["seguridadSocial"]!.AsObject().Remove("_todo"),

        ["tranches out of order"] =
            r => Swap(r["irpf"]!["escalaEstatal"]!.AsArray(), 1, 2),

        ["two open-ended tranches"] =
            r => r["irpf"]!["escalaEstatal"]![0]!["upTo"] = null,

        ["open-ended tranche not last"] =
            r => Swap(r["irpf"]!["escalaEstatal"]!.AsArray(), 0, 5),

        ["rate descends inside a scale"] =
            r => r["irpf"]!["escalaEstatal"]![4]!["rate"] = 0.01m,

        ["taxYear disagrees with the file name"] =
            r => r["taxYear"] = 2024,

        ["provenance entry verified against a source but carrying no date"] =
            r => r["provenance"]!["/irpf/escalaEstatal"]!["kind"] = "boe",

        ["provenance kind outside the enum"] =
            r => r["provenance"]!["/irpf/escalaEstatal"]!["kind"] = "BOE",

        ["provenance key that is not a JSON Pointer"] =
            r => Rename(r["provenance"]!.AsObject(), "/irpf/escalaEstatal", "irpf/escalaEstatal"),

        ["provenance pointer at a node that does not exist"] =
            r => Rename(r["provenance"]!.AsObject(), "/irpf/escalaEstatal", "/irpf/escalaEstatalx"),

        ["tax scale with no provenance entry"] =
            r => r["provenance"]!.AsObject().Remove("/regions/VC/escalaAutonomica"),
    };

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Mutations.Keys) { data.Add(name); }
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void MutatedFileIsRejected(string name)
    {
        var path = TaxYearConfigFiles.All().First();
        var root = JsonNode.Parse(File.ReadAllText(path))!;

        Mutations[name](root);

        Assert.True(Validate(root, Path.GetFileName(path)).Count > 0,
            $"Mutation \"{name}\" was accepted. The rule meant to catch it is not running.");
    }

    [Fact]
    public void TheUnmutatedFileIsAccepted()
    {
        var path = TaxYearConfigFiles.All().First();
        var root = JsonNode.Parse(File.ReadAllText(path))!;

        Assert.Empty(Validate(root, Path.GetFileName(path)));
    }

    private static List<string> Validate(JsonNode root, string fileName)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(root);
        var results = TaxYearConfigFiles.Schema()
            .Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });

        return [.. results.Describe(), .. TaxYearRules.Check(root, fileName)];
    }

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