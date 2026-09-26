using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GestorIA.Engine;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Cli.Tests;

internal static class RepoFiles
{
    internal const string ConfigFileName = "2025.example.json";

    private static readonly Lazy<TaxYearConfig> LazyConfig =
        new(() => TaxYearConfigParser.Parse(File.ReadAllBytes(Path.Combine(Root(), "config", "tax-years", ConfigFileName)), ConfigFileName));

    internal static TaxYearConfig Config => LazyConfig.Value;

    internal static string ExampleInput() => File.ReadAllText(Path.Combine(Root(), "src", "GestorIA.Cli", "set-aside-input.example.json"));

    // The "inputs" object of a set-aside golden, which is the console's input format.
    internal static string GoldenInput(string golden) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "golden", "2025", $"{golden}.json")))!["setAside"]!["inputs"]!.ToJsonString();

    internal static SetAsideResult Estimate(string golden) => SetAsideEstimator.Estimate(SetAsideInputFile.Parse(GoldenInput(golden), Config));

    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
