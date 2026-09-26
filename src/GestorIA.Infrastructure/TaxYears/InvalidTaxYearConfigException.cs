namespace GestorIA.Infrastructure.TaxYears;

public sealed class InvalidTaxYearConfigException : Exception
{
    public IReadOnlyList<string> Failures { get; }

    public InvalidTaxYearConfigException(string fileName, IReadOnlyList<string> failures)
        : base($"{fileName} is not a valid tax-year configuration:\n" + string.Join("\n", failures))
    {
        Failures = failures;
    }
}
