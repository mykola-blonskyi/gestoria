namespace GestorIA.Domain.Models;

public enum TransactionType
{
    Income, // Ingreso (Доход) 
    Expense // Gasto (Расход)
}

public enum VatRate
{
    Zero = 0, // 0% (Exento)
    SuperReduced = 4, // 4%
    Reduced = 10, // 10%
    Standard = 21 // 21% (General)
}

public class Transaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly Date { get; init; }
    public DateOnly ValueDate { get; init; }
    public required string Description { get; init; }

    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public TransactionType Type => Amount >= 0 ? TransactionType.Income : TransactionType.Expense;

    public bool IsDeductible { get; set; } // Deductible en IRPF / IVA
    public VatRate VatRate { get; set; } = VatRate.Standard;
    public string? Category { get; set; } // e.g., "Suscripciones", "Equipamiento", "Suministros"
    public string? Counterparty { get; set; } // NIF/CIF или имя контрагента
}