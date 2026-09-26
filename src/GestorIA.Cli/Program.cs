using System.Text;
using GestorIA.Cli;
using GestorIA.Engine;
using GestorIA.Infrastructure.TaxYears;

// Top-level statements: the compiler wraps this file in a Main(string[] args) method, so it runs like a script from the first line.
Console.OutputEncoding = Encoding.UTF8;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/GestorIA.Cli -- <input.json> <tax-year-config.json>");
    Console.Error.WriteLine("Keep the input file outside the repository: it holds personal figures (SPEC-013). See src/GestorIA.Cli/README.md.");
    return 2;
}

var configFileName = Path.GetFileName(args[1]);
SetAsideResult result;
TaxYearConfig config;

try
{
    config = TaxYearConfigParser.Parse(File.ReadAllBytes(args[1]), configFileName);
    var input = SetAsideInputFile.Parse(File.ReadAllText(args[0]), config);
    result = SetAsideEstimator.Estimate(input);
}
catch (Exception e) when (e is InvalidInputFileException or InvalidTaxYearConfigException or ConfigNotFoundException
    or ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
{
    // The engine rejects inputs it cannot estimate with ArgumentException or NotSupportedException, and says why.
    Console.Error.WriteLine("No estimate: " + e.Message);
    return 1;
}

Console.Write(SetAsideReport.Render(result, config.TaxYear, configFileName));
return 0;
