namespace GestorIA.Domain.Models;

public record IrpfBracketBreakdown(
    decimal LowerBound,
    decimal UpperBound,
    decimal TaxableAmountInBracket,
    decimal RatePercentage,
    decimal TaxForBracket
);

public record IrpfCalculationResult(
    int Year,
    decimal TotalIncome,              // Total Ingresos
    decimal TotalDeductibleExpenses,  // Total Gastos Deducibles
    decimal SocialSecurityPaid,       // Cuota de Autónomos
    decimal NetYield,                 // Rendimiento Neto (Income - Expenses - SS)
    decimal GeneralDeduction,         // Gastos de difícil justificación (5%)
    decimal TaxableBase,              // Base Imponible
    decimal TotalTaxLiability,        // Total Cuota Íntegra
    decimal EffectiveTaxRate,         // Эффективная ставка (%)
    IReadOnlyList<IrpfBracketBreakdown> BracketBreakdowns
);