using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// AnnualRendimientoComputable is LGSS art. 308.1.c 1.ª's figure, the IRPF rendimiento neto plus the titular's own
// cuotas, annualized over the months of alta.
public sealed record MonthlyCuotaInput(DateOnly Alta, decimal AnnualRendimientoComputable, YearMonth Month);

// FullMonthCuota is the month's cuota before the month of alta is prorated: what TGSS debits for a whole month.
public sealed record MonthlyCuotaResult(decimal Cuota, decimal FullMonthCuota, CalculationTrace Trace, IReadOnlyList<Warning> Warnings);

public static class MonthlyCuotaCalculator
{
    // RD 2064/1995 art. 45.1, as worded since Ley 6/2017 (disposición final 2.ª.3): the monthly cuota is divided by thirty whatever the month's length.
    private const decimal DaysInMonthForProrating = 30m;

    public static MonthlyCuotaResult Cuota(MonthlyCuotaInput input, SeguridadSocialConfig config)
    {
        var altaMonth = YearMonth.Of(input.Alta);
        var monthsSinceAlta = input.Month.MonthsSince(altaMonth);

        if (monthsSinceAlta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Month, Invariant($"No cuota is due for {input.Month}, before the alta on {input.Alta:yyyy-MM-dd}."));
        }

        var tramos = config.Tramos;
        var tarifaPlana = config.TarifaPlana;
        var gastosGenericos = config.GastosGenericos;
        var steps = new List<TraceStep>();
        var monthlyNet = input.AnnualRendimientoComputable * (1m - gastosGenericos.Value) / 12m;

        steps.Add(new TraceStep(
            "ss.rendimiento-neto-mensual",
            TraceSection.SeguridadSocial,
            "Rendimiento neto mensual tras gastos genéricos",
            [new("rendimientoComputableAnual", Invariant($"{input.AnnualRendimientoComputable}")), new("gastosGenericos", gastosGenericos.ToString())],
            Invariant($"{input.AnnualRendimientoComputable} × (1 − {gastosGenericos}) / 12 = {monthlyNet}"),
            new TraceValue.Money(new Money(monthlyNet)),
            "LGSS art. 308.1.c 2.ª (boe.es consolidated RDL 8/2015, read 2026-09-27) deducts the gastos genéricos from the rendimiento computable "
                + "(config seguridadSocial.gastosGenericos); 3.ª spreads the result over the months of alta, and the input is already annualized over them, so / 12"));

        var tramo = tramos.For(monthlyNet);
        var upTo = tramo.NetUpTo is { } u ? Invariant($"{u.Amount}") : "open";

        steps.Add(new TraceStep(
            "ss.tramo",
            TraceSection.SeguridadSocial,
            "Tramo de cotización",
            [new("rendimientoNetoMensual", Invariant($"{monthlyNet}")), new("tramo", tramo.Name)],
            Invariant($"{tramo.NetFrom.Amount} < {monthlyNet} <= {upTo} → {tramo.Name}, cuota at base mínima = {tramo.CuotaMin.Amount}"),
            new TraceValue.Money(tramo.CuotaMin),
            "config seguridadSocial.tramos; cuotaMin is the cuota at the tramo's base mínima"));

        var monthsInForce = tarifaPlana.LastMonth(input.Alta).MonthsSince(altaMonth);
        var tarifaPlanaInForce = monthsSinceAlta <= monthsInForce;
        var fullMonth = tarifaPlanaInForce ? tarifaPlana.Amount.Amount : tramo.CuotaMin.Amount;

        steps.Add(new TraceStep(
            "ss.tarifa-plana",
            TraceSection.SeguridadSocial,
            tarifaPlanaInForce ? "Tarifa plana en vigor" : "Tarifa plana agotada",
            [new("alta", Invariant($"{input.Alta:yyyy-MM-dd}")), new("month", input.Month.ToString()), new("tarifaPlanaMonths", Invariant($"{monthsInForce}"))],
            tarifaPlanaInForce
                ? Invariant($"months since alta {monthsSinceAlta} <= {monthsInForce} → {tarifaPlana.Amount.Amount}")
                : Invariant($"months since alta {monthsSinceAlta} > {monthsInForce} → tramo cuota {tramo.CuotaMin.Amount}"),
            new TraceValue.Money(new Money(fullMonth)),
            "Ley 20/2007 art. 38 ter.1: the alta month plus the complete calendar months after it, eleven when the alta is on the 1st (itself a complete calendar month) and twelve otherwise, per Seguridad Social's own reading of the benefit as the first 12 months of alta (portal.seg-social.gob.es)"));

        var cuota = fullMonth;

        if (monthsSinceAlta == 0 && input.Alta.Day > 1)
        {
            var days = DateTime.DaysInMonth(input.Alta.Year, input.Alta.Month) - input.Alta.Day + 1;
            cuota = Round2(fullMonth * days / DaysInMonthForProrating);

            steps.Add(new TraceStep(
                "ss.prorrateo-mes-alta",
                TraceSection.SeguridadSocial,
                "Prorrateo del mes de alta",
                [new("cuotaMensual", Invariant($"{fullMonth}")), new("diasDeAlta", Invariant($"{days}"))],
                Invariant($"{fullMonth} × {days} / 30 = {cuota}"),
                new TraceValue.Money(new Money(cuota)),
                "RD 2064/1995 art. 45.1 (Ley 6/2017, disposición final 2.ª.3): charged per day of alta, monthly cuota divided by thirty; assumes one of the first three altas of the year (RD 84/1996 art. 46.2.a))"));
        }

        var warnings = new[]
        {
            new Warning(WarningCodes.SsRegularizacionAhead, WarningSeverity.Warning, "TGSS trues the cuota up annually against real net income; this monthly figure is provisional."),
        };

        return new MonthlyCuotaResult(cuota, fullMonth, new CalculationTrace(steps), warnings);
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}