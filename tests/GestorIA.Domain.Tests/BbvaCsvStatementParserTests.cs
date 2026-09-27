using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Infrastructure.Parsers;
using Xunit;

namespace GestorIA.Domain.Tests;

public class BbvaCsvStatementParserTests
{
    private const string Header = "Fecha;Fecha Valor;Concepto;Importe;Saldo";

    private static IReadOnlyList<StatementLine> Parse(params string[] lines) => new BbvaCsvStatementParser().Parse(string.Join("\r\n", lines));

    private static IEnumerable<BankTransaction> Movements(params string[] lines) => Parse(lines).Select(line => line.Movement);

    private static IReadOnlyDictionary<string, string[]> Refusal(params string[] lines) =>
        Assert.Throws<InvalidStatementException>(() => Parse(lines)).Errors;

    [Fact]
    public void EveryMovementIsReadInTheFilesOrderWithItsSign()
    {
        var lines = Movements(
            Header,
            "10/01/2025;11/01/2025;FACTURA CLIENTE A;1.500,00;5.000,00",
            "12/01/2025;12/01/2025;COMPRA JETBRAINS RIDER;-249,00;4.751,00");

        Assert.Equal(
            [
                new BankTransaction(new(2025, 1, 10), new(2025, 1, 11), "FACTURA CLIENTE A", new Money(1500.00m), new Money(5000.00m)),
                new BankTransaction(new(2025, 1, 12), new(2025, 1, 12), "COMPRA JETBRAINS RIDER", new Money(-249.00m), new Money(4751.00m)),
            ],
            lines);
    }

    [Fact]
    public void AQuotedFieldMayHoldTheSeparatorAndAQuoteAndTheBalanceMayBeEmpty()
    {
        var line = Assert.Single(Movements(Header, "\"03/02/2025\";03/02/2025;\"RECIBO \"\"LUZ\"\"; FEBRERO\";-0,99;"));

        Assert.Equal("RECIBO \"LUZ\"; FEBRERO", line.Description);
        Assert.Equal(new Money(-0.99m), line.Amount);
        Assert.Null(line.Balance);
    }

    [Fact]
    public void EachMovementCarriesTheNumberOfItsLineInTheFile()
    {
        var lines = Parse("", Header, "10/01/2025;10/01/2025;A;1,00;", "", "11/01/2025;11/01/2025;B;2,00;");

        Assert.Equal([3, 5], lines.Select(line => line.Number));
    }

    [Fact]
    public void BlankLinesAreSkippedAndAHeaderOnlyStatementHoldsNothing()
    {
        Assert.Empty(Parse("", Header, "  ", ""));
    }

    [Fact]
    public void AFileWithoutTheHeaderIsRefusedAsAWhole()
    {
        var errors = Refusal("10/01/2025;10/01/2025;FACTURA;1,00;1,00");

        Assert.Equal(["file"], errors.Keys);
    }

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        Assert.Equal(["file"], Refusal("").Keys);
    }

    [Fact]
    public void EveryUnreadableLineIsNamedByItsNumberWithoutQuotingIt()
    {
        var errors = Refusal(
            Header,
            "10/01/2025;10/01/2025;OK;1,00;1,00",
            "2025-01-11;10/01/2025;SECRET-DESCRIPTION;1,00;1,00",
            "12/01/2025;12/01/2025;SECRET-DESCRIPTION;12,345;1,00",
            "13/01/2025;13/01/2025;SECRET-DESCRIPTION",
            "14/01/2025;14/01/2025;\"SECRET-DESCRIPTION;1,00;1,00");

        Assert.Equal(["line 3", "line 4", "line 5", "line 6"], errors.Keys.Order());
        Assert.Contains("Fecha must", errors["line 3"].Single());
        Assert.Contains("Importe must", errors["line 4"].Single());
        Assert.All(errors.Values.SelectMany(reasons => reasons), reason => Assert.DoesNotContain("SECRET", reason));
        Assert.All(errors.Values.SelectMany(reasons => reasons), reason => Assert.DoesNotContain("12,345", reason));
    }

    [Fact]
    public void AnAmountBeyondTwelveDigitsIsRefused()
    {
        Assert.Equal(["line 2"], Refusal(Header, "10/01/2025;10/01/2025;X;1.000.000.000.000,00;").Keys);
    }

    [Fact]
    public void AFileOfManyBadLinesListsTheFirstTwentyAndCountsTheRest()
    {
        var errors = Refusal([Header, .. Enumerable.Repeat("not a movement", 25)]);

        Assert.Equal(21, errors.Count);
        Assert.Contains("25 lines", errors["file"].Single());
    }
}
