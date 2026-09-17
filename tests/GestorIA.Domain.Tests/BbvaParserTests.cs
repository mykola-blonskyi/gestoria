using System.Text;
using GestorIA.Infrastructure.Parsers;
using Xunit;

namespace GestorIA.Domain.Tests;

public class BbvaParserTest
{
    [Fact]
    public async Task ParseAsync_ValidCsv_ReturnsCorrectTransactions()
    {
        //Arrange
        var csvContent = """
        Fecha;Fecha Valor;Concepto;Importe;Saldo
        10/01/2025;10/01/2025;FACTURA CLIENTE A;1500,00;5000,00
        12/01/2025;12/01/2025;COMPRA JETBRAINS RIDER;-249,00;4751,00
      """;

        var parser = new BbvaCsvStatementParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        //Act
        var transactions = new List<GestorIA.Domain.Models.Transaction>();
        await foreach (var tx in parser.ParseAsync(stream))
        {
            transactions.Add(tx);
        }

        //Assert
        Assert.Equal(2, transactions.Count);
        Assert.Equal(1500.00m, transactions[0].Amount);
        Assert.Equal(GestorIA.Domain.Models.TransactionType.Income, transactions[0].Type);
        Assert.Equal(-249.00m, transactions[1].Amount);
        Assert.Equal(GestorIA.Domain.Models.TransactionType.Expense, transactions[1].Type);
    }
}