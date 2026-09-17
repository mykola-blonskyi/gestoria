using GestorIA.Domain.Models;
using GestorIA.Domain.Services;
using Xunit;

namespace GestorIA.Domain.Tests;

public class IrpfCalculatorTests
{
    private readonly IrpfTaxCalculator _calculator = new();

    [Fact]
    public void CalculateAnnualIrpf_InvalidYear_ReturnsFailureResult()
    {
        // Act
        var result = _calculator.CalculateAnnualIrpf(2023, [], 0m);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(IrpfTaxCalculator.Errors.InvalidYear.Code, result.Error.Code);
    }

    [Fact]
    public void CalculateAnnualIrpf_ValidIncomeAndExpenses_CalculatesCorrectTax()
    {
        // Arrange: Доход €45,000, Вычитаемые расходы €5,000, Cuota de autónomos €3,600
        var transactions = new List<Transaction>
        {
            new() { Description = "Factura Cliente A", Amount = 45000.00m, Date = new DateOnly(2025, 3, 1) },
            new() { Description = "Equipamiento ПО", Amount = -5000.00m, Date = new DateOnly(2025, 4, 1), IsDeductible = true }
        };

        // Act
        var result = _calculator.CalculateAnnualIrpf(2025, transactions, 3600.00m);

        // Assert
        Assert.True(result.IsSuccess);
        var val = result.Value!;

        Assert.Equal(45000.00m, val.TotalIncome);
        Assert.Equal(5000.00m, val.TotalDeductibleExpenses);
        Assert.Equal(36400.00m, val.NetYield); // 45000 - 5000 - 3600
        Assert.Equal(1820.00m, val.GeneralDeduction); // 5% от 36,400
        Assert.Equal(34580.00m, val.TaxableBase); // 36400 - 1820

        // Проверка корректности разбиения на 3 транша (до €34,580)
        Assert.Equal(3, val.BracketBreakdowns.Count);
        Assert.True(val.TotalTaxLiability > 0m);
    }
}