using GestorIA.Api.SetAside;
using GestorIA.Engine;

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
    IReadOnlyList<CasillaView> Casillas,
    IReadOnlyList<TraceStepView> Trace)
{
    public static QuarterResultView From(Modelo130Result result, int taxYear, string configHash) => new(
        taxYear,
        configHash,
        result.Quarter,
        SetAsideEstimate.Euros(result.AIngresar),
        result.DueWindow.Start,
        result.DueWindow.End,
        [.. result.Casillas.Select(kv => new CasillaView(kv.Key, SetAsideEstimate.Euros(kv.Value)))],
        [.. result.Trace.Steps.Select(TraceStepView.From)]);
}
