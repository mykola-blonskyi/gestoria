using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

internal static class RepoFiles
{
    // The set-aside goldens, all on 2025.json; G13 has no set-aside part.
    internal static readonly string[] SetAsideGoldens = ["G12", "G14", "G15", "G16", "G17", "G18", "G19", "G20", "G22"];

    internal static string ConfigDirectory => Path.Combine(Root(), "config", "tax-years");

    // The PostgreSQL image compose.yaml starts locally, so the tests run the same server version.
    internal static string ComposePostgresImage =>
        File.ReadLines(Path.Combine(Root(), "compose.yaml")).Select(line => line.Trim()).Single(line => line.StartsWith("image: postgres:", StringComparison.Ordinal))["image: ".Length..];

    internal static string OpenApiDocument => Path.Combine(Root(), "src", "GestorIA.Api", "openapi", "v1.json");

    // The web app's feature tests stub the API with these answers; WebFixtures proves they are the API's own.
    internal static string WebFixture(string name) => Path.Combine(Root(), "web", "tests", "fixtures", name);

    // A synthetic BBVA statement for 2025: made-up names and amounts, and the published example IBAN (SPEC-013).
    internal static byte[] Statement => File.ReadAllBytes(Path.Combine(Root(), "tests", "fixtures", "bank-statements", "bbva-2025-synthetic.csv"));

    internal static JsonObject Golden(string golden) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "golden", "2025", $"{golden}.json")))!["setAside"]!.AsObject();

    // The "inputs" object of a set-aside golden: the console's input file, and the estimate endpoint's body.
    internal static JsonObject GoldenInput(string golden) => Golden(golden)["inputs"]!.DeepClone().AsObject();

    // The goldens whose input states no closed quarter: a stored profile and the as-of quarter say everything they say.
    internal static readonly string[] ProfileGoldens = ["G12", "G16", "G22"];

    // A set-aside golden's taxpayer and projection as the body of POST /api/v1/profiles: the console's "noActivity" or
    // { "rendimientoNeto" } and "established" or { "period", ... } become the profile's kind-tagged objects.
    internal static JsonObject GoldenProfile(string golden)
    {
        var input = GoldenInput(golden);
        var profile = input["profile"]!;
        var registration = profile["activity"]!;

        return new JsonObject
        {
            ["taxYear"] = 2025,
            ["region"] = profile["region"]!.DeepClone(),
            ["employment"] = profile["employment"]!.DeepClone(),
            ["activity"] = new JsonObject
            {
                ["alta"] = registration["alta"]!.DeepClone(),
                ["previousYear"] = registration["previousYear"] is JsonObject known
                    ? new JsonObject { ["kind"] = "rendimientoNeto", ["rendimientoNeto"] = known["rendimientoNeto"]!.DeepClone() }
                    : new JsonObject { ["kind"] = "noActivity" },
                ["newActivity"] = registration["newActivity"] is JsonObject started
                    ? new JsonObject { ["kind"] = "started", ["period"] = started["period"]!.DeepClone(), ["ingresosFromFormerEmployer"] = started["ingresosFromFormerEmployer"]!.DeepClone() }
                    : new JsonObject { ["kind"] = "established" },
            },
            ["projection"] = input["activity"]!["projection"]!.DeepClone(),
        };
    }

    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
