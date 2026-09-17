using System.Globalization;
using System.Runtime.CompilerServices;
using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;

namespace GestorIA.Infrastructure.Parsers;

public class BbvaCsvStatementParser : IStatementParser
{
    public string BankName => "BBVA";

    public bool CanParse(string fileName) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<Transaction> ParseAsync(Stream fileStream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(fileStream);
        string? line;
        bool isHeader = true;

        var esCulture = new CultureInfo("es-ES");

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            if (isHeader)
            {
                isHeader = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            var transaction = ParseLine(line.AsSpan(), esCulture);
            if (transaction != null)
            {
                yield return transaction;
            }
        }
    }

    private static Transaction? ParseLine(ReadOnlySpan<char> line, CultureInfo cultureInfo)
    {
        // Пример CSV: Date;ValueDate;Description;Amount;Balance
        // 15/01/2025;15/01/2025;PAGO EN HEROKU;-15,50;1250,00

        Span<Range> ranges = stackalloc Range[5];
        int splitCount = line.Split(ranges, ';', StringSplitOptions.TrimEntries);

        if (splitCount < 4) return null;

        var dateSpan = line[ranges[0]];
        var descSpan = line[ranges[2]];
        var amountSpan = line[ranges[3]];

        if (!DateOnly.TryParse(dateSpan, cultureInfo, DateTimeStyles.None, out var date)) return null;

        if (!decimal.TryParse(amountSpan, NumberStyles.Number, cultureInfo, out var amount)) return null;

        return new Transaction
        {
            Date = date,
            ValueDate = date,
            Description = descSpan.ToString(),
            Amount = amount,
            IsDeductible = false // Базовое значение, настраивается классификатором
        };
    }
}