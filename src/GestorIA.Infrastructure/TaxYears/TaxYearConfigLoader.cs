using System.Globalization;
using GestorIA.Engine;
using static System.FormattableString;

namespace GestorIA.Infrastructure.TaxYears;

// SPEC-007 §3. The one place a tax-year file is read from disk; the engine receives it parsed, validated and hashed.
public sealed class TaxYearConfigLoader
{
    private readonly string directory;

    public TaxYearConfigLoader(string directory)
    {
        this.directory = directory;
    }

    // The years the directory holds a file for, by name only: "2025.json" is tax year 2025, and schema.json is not a year.
    public IReadOnlyList<int> Years() =>
        [.. Directory.EnumerateFiles(directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is { Length: 4 } && name.All(char.IsAsciiDigit))
            .Select(name => int.Parse(name!, CultureInfo.InvariantCulture))
            .Order()];

    public TaxYearConfig Load(int year)
    {
        var fileName = Invariant($"{year}.json");
        byte[] source;

        try
        {
            source = File.ReadAllBytes(Path.Combine(directory, fileName));
        }
        catch (FileNotFoundException)
        {
            // The directory stays out of the message: the API answers with it, and a server path is not the client's business.
            throw new ConfigNotFoundException(Invariant($"No configuration for tax year {year}: there is no {fileName}."));
        }

        return TaxYearConfigParser.Parse(source, fileName);
    }
}
