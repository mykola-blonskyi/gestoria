using GestorIA.Domain.Common;
using GestorIA.Domain.Models;

namespace GestorIA.Domain.Services;

public class IrpfTaxCalculator
{
    // Ошибки домена
    public static class Errors
    {
        public static readonly Error InvalidYear = new("IRPF.InvalidYear", "Расчет доступен только для налоговых периодов начиная с 2024 года.");
        public static readonly Error NegativeIncome = new("IRPF.NegativeIncome", "Сумма доходов не может быть отрицательной.");
        public static readonly Error NegativeExpenses = new("IRPF.NegativeExpenses", "Сумма вычитаемых расходов не может быть отрицательной.");
    }

    // Прогрессивная шкала IRPF (Транши/Tramos 2025-2026 гг.)
    private static readonly (decimal Limit, decimal Rate)[] IrpfBrackets =
    [
        (12450.00m, 0.19m), // До €12,450 — 19%
        (20200.00m, 0.24m), // От €12,450 до €20,200 — 24%
        (35200.00m, 0.30m), // От €20,200 до €35,200 — 30%
        (60000.00m, 0.37m), // От €35,200 до €60,000 — 37%
        (300000.00m, 0.45m),// От €60,000 до €300,000 — 45%
        (decimal.MaxValue, 0.47m) // Свыше €300,000 — 47%
    ];

    public Result<IrpfCalculationResult> CalculateAnnualIrpf(
        int year,
        IEnumerable<Transaction> transactions,
        decimal socialSecurityPaid)
    {
        if (year < 2024)
            return Result<IrpfCalculationResult>.Failure(Errors.InvalidYear);

        var txList = transactions.ToList();

        decimal totalIncome = txList
            .Where(t => t.Type == TransactionType.Income)
            .Sum(t => t.Amount);

        decimal totalExpenses = txList
            .Where(t => t.Type == TransactionType.Expense && t.IsDeductible)
            .Sum(t => Math.Abs(t.Amount));

        if (totalIncome < 0)
            return Result<IrpfCalculationResult>.Failure(Errors.NegativeIncome);

        if (totalExpenses < 0)
            return Result<IrpfCalculationResult>.Failure(Errors.NegativeExpenses);

        // 1. Rendimiento Neto Previo
        decimal netYield = totalIncome - totalExpenses - socialSecurityPaid;

        if (netYield <= 0)
        {
            // Если нет чистой прибыли, налог к уплате равен 0
            return Result<IrpfCalculationResult>.Success(new IrpfCalculationResult(
                Year: year,
                TotalIncome: totalIncome,
                TotalDeductibleExpenses: totalExpenses,
                SocialSecurityPaid: socialSecurityPaid,
                NetYield: netYield,
                GeneralDeduction: 0m,
                TaxableBase: 0m,
                TotalTaxLiability: 0m,
                EffectiveTaxRate: 0m,
                BracketBreakdowns: Array.Empty<IrpfBracketBreakdown>()
            ));
        }

        // 2. Gastos de difícil justificación: 5% от чистого дохода (максимум €2,000)
        decimal generalDeduction = Math.Min(netYield * 0.05m, 2000.00m);
        decimal taxableBase = netYield - generalDeduction;

        // 3. Расчет по траншам IRPF
        var breakdowns = new List<IrpfBracketBreakdown>();
        decimal totalTax = 0m;
        decimal previousLimit = 0m;

        foreach (var (limit, rate) in IrpfBrackets)
        {
            if (taxableBase <= previousLimit) break;

            decimal taxableInBracket = Math.Min(taxableBase, limit) - previousLimit;
            decimal taxForBracket = Math.Round(taxableInBracket * rate, 2, MidpointRounding.AwayFromZero);

            totalTax += taxForBracket;

            breakdowns.Add(new IrpfBracketBreakdown(
                LowerBound: previousLimit,
                UpperBound: limit == decimal.MaxValue ? taxableBase : limit,
                TaxableAmountInBracket: taxableInBracket,
                RatePercentage: rate * 100,
                TaxForBracket: taxForBracket
            ));

            previousLimit = limit;
        }

        decimal effectiveRate = totalIncome > 0
            ? Math.Round((totalTax / totalIncome) * 100, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return Result<IrpfCalculationResult>.Success(new IrpfCalculationResult(
            Year: year,
            TotalIncome: totalIncome,
            TotalDeductibleExpenses: totalExpenses,
            SocialSecurityPaid: socialSecurityPaid,
            NetYield: netYield,
            GeneralDeduction: generalDeduction,
            TaxableBase: taxableBase,
            TotalTaxLiability: totalTax,
            EffectiveTaxRate: effectiveRate,
            BracketBreakdowns: breakdowns
        ));
    }
}