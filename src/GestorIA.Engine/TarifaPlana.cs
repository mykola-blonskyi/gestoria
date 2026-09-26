using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine;

public sealed record TarifaPlana(Money Amount, int Months)
{
    // Ley 20/2007 art. 38 ter.1 grants the reduced cuota "durante los doce meses naturales completos
    // siguientes" to the alta, ambiguous when the alta falls on the 1st: the alta month is then itself
    // a complete calendar month. Seguridad Social's own guide describes the same benefit as running
    // "durante los primeros 12 meses de alta" (portal.seg-social.gob.es/wps/portal/importass, read
    // 2026-09-27): on the 1st, the alta month is the first of the twelve and eleven more follow; on any
    // other day the alta month is prorated instead and the full twelve follow it.
    public YearMonth LastMonth(DateOnly alta) =>
        YearMonth.Of(alta).AddMonths(alta.Day == 1 ? Months - 1 : Months);
}