using GestorIA.Api.SetAside;
using GestorIA.Engine;

namespace GestorIA.Api.Periods;

// The annual true-up alone (#71): the same figures AnnualTrueUpGap/AnnualTrueUpPayableIn carry on the set-aside estimate,
// plus the liability, marginal rate and reducción-lost figures the engine already computes, and their own trace.
public sealed record AnnualTrueUpView(
    int TaxYear,
    string ConfigHash,
    string LiabilityOnActivity,
    string MarginalRate,
    string ReduccionTrabajoLost,
    string Gap,
    DateOnly DueFrom,
    DateOnly DueBy,
    string PayableIn,
    IReadOnlyList<TraceStepView> Trace,
    IReadOnlyList<NoticeView> Notices)
{
    public static AnnualTrueUpView From(AnnualTrueUpResult result, int taxYear, string configHash) => new(
        taxYear,
        configHash,
        SetAsideEstimate.Euros(result.LiabilityOnActivity),
        SetAsideEstimate.Share(result.MarginalRate),
        SetAsideEstimate.Euros(result.ReduccionTrabajoLost),
        SetAsideEstimate.Euros(result.Gap),
        result.DueWindow.Start,
        result.DueWindow.End,
        result.PayableIn.ToString(),
        [.. result.Trace.Steps.Select(TraceStepView.From)],
        [.. result.Warnings.Select(w => new NoticeView(w.Code, w.Severity, w.Text))]);
}
