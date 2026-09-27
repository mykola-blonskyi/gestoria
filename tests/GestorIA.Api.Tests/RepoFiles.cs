using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace GestorIA.Api.Tests;

internal static class RepoFiles
{
    // The set-aside goldens, all on 2025.json; G13 has no set-aside part.
    internal static readonly string[] SetAsideGoldens = ["G12", "G14", "G15", "G16", "G17", "G18", "G19", "G20", "G22"];

    internal static string ConfigDirectory => Path.Combine(Root(), "config", "tax-years");

    internal static string OpenApiDocument => Path.Combine(Root(), "src", "GestorIA.Api", "openapi", "v1.json");

    // The dashboard's tests render these answers; WebFixtures proves they are the API's own.
    internal static string WebFixture(string name) => Path.Combine(Root(), "web", "src", "features", "dashboard", "tests", "fixtures", name);

    internal static JsonObject Golden(string golden) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "golden", "2025", $"{golden}.json")))!["setAside"]!.AsObject();

    // The "inputs" object of a set-aside golden: the console's input file, and the estimate endpoint's body.
    internal static JsonObject GoldenInput(string golden) => Golden(golden)["inputs"]!.DeepClone().AsObject();

    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
