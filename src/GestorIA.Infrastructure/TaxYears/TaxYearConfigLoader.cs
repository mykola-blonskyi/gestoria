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
            throw new ConfigNotFoundException(Invariant($"No configuration for tax year {year}: {fileName} is not in {directory}."));
        }

        return TaxYearConfigParser.Parse(source, fileName);
    }
}
