using GestorIA.Api.SetAside;
using GestorIA.Engine;
using GestorIA.Infrastructure.Transactions;

namespace GestorIA.Api.Periods;

public sealed record CasillaView(string Key, string Amount);

// One quarter's Modelo 130 (#71): the same figures GET /profiles/{id}/set-aside/estimate's NextPayment carries, plus every
// casilla the calculator computed along the way, keyed the way TaxYearView.Modelo130Lines names them.
public sealed record QuarterResultView(
    int TaxYear,
    string ConfigHash,
    Quarter Quarter,
    string AIngresar,
    DateOnly DueFrom,
    DateOnly DueBy,
    Modelo130FilingView Filing,
    IReadOnlyList<CasillaView> Casillas,
    IReadOnlyList<TraceStepView> Trace,
    LedgerView Ledger)
{
    public static QuarterResultView From(Modelo130Result result, int taxYear, string configHash, LedgerEstimate ledger) => new(
        taxYear,
        configHash,
        result.Quarter,
        SetAsideEstimate.Euros(result.AIngresar),
        result.DueWindow.Start,
        result.DueWindow.End,
        SetAsideEstimate.Filing(result.Filing),
        [.. result.Casillas.Select(kv => new CasillaView(kv.Key, SetAsideEstimate.Euros(kv.Value)))],
        [.. ledger.Steps.Concat(result.Trace.Steps).Select(TraceStepView.From)],
        LedgerView.From(ledger.Counts));
}
