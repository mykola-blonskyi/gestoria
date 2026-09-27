using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// AnnualRendimientoComputable is LGSS art. 308.1.c 1.ª's figure, the IRPF rendimiento neto plus the titular's own
// cuotas, annualized over the months of alta. BaseCotizacion is the monthly base the taxpayer chose (308.1.a 1.ª and 3.ª).
public sealed record MonthlyCuotaInput(DateOnly Alta, decimal AnnualRendimientoComputable, YearMonth Month, Money BaseCotizacion);

// Cuota is what TGSS debits for the month, tarifa plana while it lasts and otherwise the cuota at the chosen base, and
// FullMonthCuota the same before the month of alta is prorated. Floor and Ceiling are what TGSS keeps for the month at least
// and at most once the year's rendimientos are known (LGSS art. 308.1.c 3.ª–4.ª), prorated as the debit is.
public sealed record MonthlyCuotaResult(decimal Cuota, decimal FullMonthCuota, decimal Floor, decimal Ceiling, CalculationTrace Trace, IReadOnlyList<Warning> Warnings)
{
    // Math.Clamp(value, min, max) is Math.min(Math.max(value, min), max) in TypeScript.
    public decimal Kept => Math.Clamp(Cuota, Floor, Ceiling);
}

public static class MonthlyCuotaCalculator
{
    // RD 2064/1995 art. 45.1, as worded since Ley 6/2017 (disposición final 2.ª.3): the monthly cuota is divided by thirty whatever the month's length.
    private const decimal DaysInMonthForProrating = 30m;

    // What TGSS debits for the month, which the tramo does not change: the estimator needs it before it knows the tramo.
    public static decimal Debit(DateOnly alta, YearMonth month, Money baseCotizacion, SeguridadSocialConfig config)
    {
        var debit = DebitOf(alta, month, baseCotizacion, config);
        return debit.Prorated(debit.FullMonth);
    }

    public static MonthlyCuotaResult Cuota(MonthlyCuotaInput input, SeguridadSocialConfig config)
    {
        var tramos = config.Tramos;
        var gastosGenericos = config.GastosGenericos;
        var steps = new List<TraceStep>();
        var debit = DebitOf(input.Alta, input.Month, input.BaseCotizacion, config);
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

        var tipo = config.TipoCotizacion;
        var chosen = debit.ChosenCuota;

        steps.Add(new TraceStep(
            "ss.base-cotizacion",
            TraceSection.SeguridadSocial,
            "Cuota por la base de cotización elegida",
            [new("baseCotizacion", Invariant($"{input.BaseCotizacion.Amount}")), new("tipoCotizacion", tipo.ToString())],
            Invariant($"{input.BaseCotizacion.Amount} × {tipo} = {chosen}"),
            new TraceValue.Money(new Money(chosen)),
            "LGSS art. 308.1.a 1.ª and 3.ª (boe.es consolidated RDL 8/2015, read 2026-09-27): the taxpayer chooses the monthly base de cotización and changes it as the forecast "
                + "of rendimientos changes; 308.1.b: the cuota is that base times the tipo of the year (config seguridadSocial.tipoCotizacion), rounded half up to the cent, "
                + "the rounding that reproduces every cuotaMin of the tramos table"));

        var floor = tramo.CuotaMin.Amount;
        var ceiling = config.CuotaAt(tramo.BaseMax);
        var kept = Math.Clamp(chosen, floor, ceiling);
        var bounds = Invariant($"base mínima {tramo.BaseMin.Amount} → {floor}, base máxima {tramo.BaseMax.Amount} × {tipo} = {ceiling}; ");

        steps.Add(new TraceStep(
            "ss.regularizacion",
            TraceSection.SeguridadSocial,
            "Cuota que TGSS mantiene al regularizar",
            [new("cuotaBaseCotizacion", Invariant($"{chosen}")), new("baseMinima", Invariant($"{tramo.BaseMin.Amount}")), new("baseMaxima", Invariant($"{tramo.BaseMax.Amount}"))],
            bounds + (chosen < floor ? Invariant($"{chosen} < {floor} → topped up to {floor}")
                : chosen > ceiling ? Invariant($"{chosen} > {ceiling} → refunded down to {ceiling}")
                : Invariant($"{floor} <= {chosen} <= {ceiling} → {chosen}, neither topped up nor refunded")),
            new TraceValue.Money(new Money(kept)),
            "LGSS art. 308.1.c 3.ª–4.ª (boe.es consolidated RDL 8/2015, read 2026-09-27): once the year's rendimientos are known, a cotización below the cuota at the "
                + "tramo's base mínima is topped up to it, one above the cuota at its base máxima is refunded down to it, and one between stands. The top-up is paid, "
                + "and the refund made, in a later year. Config seguridadSocial.tramos, cuotaMin being the cuota at the base mínima"));

        steps.Add(new TraceStep(
            "ss.tarifa-plana",
            TraceSection.SeguridadSocial,
            debit.TarifaPlanaInForce ? "Tarifa plana en vigor" : "Tarifa plana agotada",
            [new("alta", Invariant($"{input.Alta:yyyy-MM-dd}")), new("month", input.Month.ToString()), new("tarifaPlanaMonths", Invariant($"{debit.TarifaPlanaMonths}"))],
            debit.TarifaPlanaInForce
                ? Invariant($"months since alta {debit.MonthsSinceAlta} <= {debit.TarifaPlanaMonths} → {debit.FullMonth}, not regularised")
                : Invariant($"months since alta {debit.MonthsSinceAlta} > {debit.TarifaPlanaMonths} → cuota at the chosen base {chosen}"),
            new TraceValue.Money(new Money(debit.FullMonth)),
            "Ley 20/2007 art. 38 ter.1: the alta month plus the complete calendar months after it, eleven when the alta is on the 1st (itself a complete calendar month) and twelve otherwise, per Seguridad Social's own reading of the benefit as the first 12 months of alta (portal.seg-social.gob.es). "
                + "Art. 38 ter.6 (boe.es BOE-A-2007-13409, read 2026-09-27): the cuota reducida is not regularised"));

        var cuota = debit.Prorated(debit.FullMonth);

        if (debit.DaysOfAlta is { } days)
        {
            steps.Add(new TraceStep(
                "ss.prorrateo-mes-alta",
                TraceSection.SeguridadSocial,
                "Prorrateo del mes de alta",
                [new("cuotaMensual", Invariant($"{debit.FullMonth}")), new("diasDeAlta", Invariant($"{days}"))],
                Invariant($"{debit.FullMonth} × {days} / 30 = {cuota}"),
                new TraceValue.Money(new Money(cuota)),
                "RD 2064/1995 art. 45.1 (Ley 6/2017, disposición final 2.ª.3): charged per day of alta, monthly cuota divided by thirty; assumes one of the first three altas of the year (RD 84/1996 art. 46.2.a))"));
        }

        var warnings = new[]
        {
            new Warning(WarningCodes.SsRegularizacionAhead, WarningSeverity.Warning, "TGSS trues the cuota up annually against real net income; this monthly figure is provisional."),
        };

        return debit.TarifaPlanaInForce
            ? new MonthlyCuotaResult(cuota, debit.FullMonth, cuota, cuota, new CalculationTrace(steps), warnings)
            : new MonthlyCuotaResult(cuota, debit.FullMonth, debit.Prorated(floor), debit.Prorated(ceiling), new CalculationTrace(steps), warnings);
    }

    private static MonthlyDebit DebitOf(DateOnly alta, YearMonth month, Money baseCotizacion, SeguridadSocialConfig config)
    {
        var altaMonth = YearMonth.Of(alta);
        var monthsSinceAlta = month.MonthsSince(altaMonth);

        if (monthsSinceAlta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, Invariant($"No cuota is due for {month}, before the alta on {alta:yyyy-MM-dd}."));
        }

        var tarifaPlana = config.TarifaPlana;
        var monthsInForce = tarifaPlana.LastMonth(alta).MonthsSince(altaMonth);
        var inForce = monthsSinceAlta <= monthsInForce;
        var chosen = config.CuotaAt(baseCotizacion);
        var fullMonth = !inForce ? chosen
            : tarifaPlana.Amount?.Amount ?? throw new ConfigNotFoundException(Invariant(
                $"seguridadSocial.tarifaPlana.amount, the cuota for {month} under tarifa plana, is declared incomplete in this configuration: {tarifaPlana.DeclaredIncomplete}"));
        int? daysOfAlta = monthsSinceAlta == 0 && alta.Day > 1 ? DateTime.DaysInMonth(alta.Year, alta.Month) - alta.Day + 1 : null;

        return new MonthlyDebit(monthsSinceAlta, monthsInForce, inForce, chosen, fullMonth, daysOfAlta);
    }

    // DaysOfAlta is set only in a month of alta that does not start on the 1st, the one month TGSS charges by the day.
    private sealed record MonthlyDebit(int MonthsSinceAlta, int TarifaPlanaMonths, bool TarifaPlanaInForce, decimal ChosenCuota, decimal FullMonth, int? DaysOfAlta)
    {
        public decimal Prorated(decimal fullMonth) =>
            DaysOfAlta is { } days ? Math.Round(fullMonth * days / DaysInMonthForProrating, 2, MidpointRounding.AwayFromZero) : fullMonth;
    }
}