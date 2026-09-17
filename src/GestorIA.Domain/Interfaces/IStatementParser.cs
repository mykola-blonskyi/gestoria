using GestorIA.Domain.Models;

namespace GestorIA.Domain.Interfaces;

public interface IStatementParser
{
    string BankName { get; }
    bool CanParse(string fileName);
    IAsyncEnumerable<Transaction> ParseAsync(Stream fileStream, CancellationToken cancellationToken = default);
}