using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine;

public sealed record TarifaPlana(Money Amount, int Months)
{
    // Ley 20/2007 art. 38 ter.1: the month of alta plus Months complete calendar months after it.
    public YearMonth LastMonth(DateOnly alta) => YearMonth.Of(alta).AddMonths(Months);
}