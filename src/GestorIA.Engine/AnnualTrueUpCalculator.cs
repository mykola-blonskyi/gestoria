using System.Globalization;
using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// Projected for the whole tax year. SeguridadSocial is the employee's own cotización, LIRPF art. 19.2.a.
public sealed record EmploymentIncome(Money Ingresos, Money SeguridadSocial);

// Projected for the whole tax year. Gastos include the titular's RETA cuota and exclude difícil justificación, as in Modelo130Input.
public sealed record ActivityIncome(Money Ingresos, Money Gastos, NewActivity NewActivity);

// LIRPF art. 32.3, as the taxpayer states it. The engine cannot see earlier years, so it never infers or defaults this.
public abstract record NewActivity
{
    private NewActivity() { }

    // Some economic activity was carried on in the year before this one started, or the period after the first positive one is over.
    public sealed record Established : NewActivity;

    // No economic activity in the year before the start date, ignoring any that ceased without ever reaching a positive net.
    // IngresosFromFormerEmployer is the part of this period's Ingresos paid by someone who paid the taxpayer employment income
    // in the year before the activity started.
    public sealed record Started(NewActivityPeriod Period, Money IngresosFromFormerEmployer) : NewActivity;
}

public enum NewActivityPeriod
{
    // No earlier period of the activity had a positive net, so this one is the first positive period if its net is positive.
    First,

    // The previous period was the first positive one.
    Following,
}

// Modelo130Advances is Σ Modelo130Result.AIngresar over the year's four quarters, paid and projected.
public sealed record AnnualTrueUpInput(EmploymentIncome Employment, ActivityIncome Activity, Money Modelo130Advances, string Region);

// LiabilityOnActivity is what the activity income adds to the annual cuota íntegra once stacked on the employment income;
// it is negative when an activity loss lowers the tax on the salary. MarginalRate is the state plus regional tranche rate at
// the stacked base. Gap is what the annual return wants beyond the Modelo 130 advances, never below zero, payable within
// DueWindow and so by the end of PayableIn.
public sealed record AnnualTrueUpResult(
    Money LiabilityOnActivity,
    Rate MarginalRate,
    Money ReduccionTrabajoLost,
    Money Gap,
    DueWindow DueWindow,
    YearMonth PayableIn,
    CalculationTrace Trace,
    IReadOnlyList<Warning> Warnings);

// SPEC-002 steps 1, 2, 4 and 6 run twice: on the employment income alone, and with the activity income stacked on it.
public static class AnnualTrueUpCalculator
{
    private const string Lirpf = "Ley 35/2006 (LIRPF)";

    public static AnnualTrueUpResult Gap(AnnualTrueUpInput input, TaxYearConfig config)
    {
        var region = config.Regions.For(input.Region);
        var irpf = config.Irpf;
        var steps = new List<TraceStep>();

        var trabajo = Trabajo(input.Employment, irpf.Trabajo.OtrosGastos, steps);
        var actividad = Actividad(input.Activity, irpf.Actividad.DificilJustificacion, steps);
        var actividadReducida = actividad - ReduccionInicioActividad(input.Activity, actividad, irpf.Actividad.InicioActividad, steps);

        // The other-income cap is tested on the activity net before its art. 32.3 reduction (AEAT Manual práctico Renta 2025, cap. 3, fase 3).
        var reduccionSolo = Reduccion("solo", "Reducción por trabajo, trabajo solo", trabajo, Money.Zero, irpf.Trabajo.Reduccion, steps);
        var reduccionStacked = Reduccion("stacked", "Reducción por trabajo, con la actividad", trabajo, actividad, irpf.Trabajo.Reduccion, steps);
        var reduccionLost = (reduccionSolo - reduccionStacked).Round2();

        var (_, solo) = CuotaIntegra("solo", "trabajo solo", trabajo.RendimientoNeto - reduccionSolo, Money.Zero);
        var (stackedBase, stacked) = CuotaIntegra("stacked", "trabajo más actividad", trabajo.RendimientoNeto - reduccionStacked, actividadReducida);

        var liability = stacked - solo;
        steps.Add(new TraceStep(
            "renta.liability-on-activity",
            TraceSection.Resultado,
            "Cuota íntegra que añade la actividad",
            [new("cuotaStacked", Show(stacked)), new("cuotaSolo", Show(solo))],
            Invariant($"{Show(stacked)} − {Show(solo)} = {Show(liability)}"),
            liability.Amount,
            "Set-aside estimator spec (#2), marginal rate: the activity income is taxed at the rates it reaches on top of the employment income, including any reducción por trabajo it destroys"));

        var estatalRate = ScaleCalculator.MarginalRate(irpf.EscalaEstatal, stackedBase.Amount);
        var autonomicaRate = ScaleCalculator.MarginalRate(region.EscalaAutonomica, stackedBase.Amount);
        var marginalRate = new Rate(estatalRate.Value + autonomicaRate.Value);
        steps.Add(new TraceStep(
            "renta.marginal-rate",
            TraceSection.Cuota,
            "Tipo marginal en la base con la actividad",
            [new("baseLiquidable", Show(stackedBase)), new("region", input.Region)],
            Invariant($"{estatalRate} estatal + {autonomicaRate} autonómico = {marginalRate}"),
            marginalRate.Value,
            $"{Lirpf} art. 63.1 and 74.1: the rate of the tranche of each scale that the next euro of base falls in"));

        steps.Add(new TraceStep(
            "renta.modelo130-advances",
            TraceSection.Resultado,
            "Pagos fraccionados del Modelo 130 del ejercicio",
            [new("modelo130Advances", Show(input.Modelo130Advances))],
            Invariant($"Σ Modelo 130 a ingresar over the year = {Show(input.Modelo130Advances)}"),
            input.Modelo130Advances.Amount,
            "Modelo130Calculator at config modelo130.rate. Retenciones on activity invoices are not credited here; they are zero for foreign payers (SPEC-003 §0)"));

        var gap = Positive(liability - input.Modelo130Advances).Round2();
        var window = config.Calendar.Renta;
        var payableIn = new YearMonth(config.TaxYear + window.End.YearOffset, window.End.Month);
        steps.Add(new TraceStep(
            "renta.gap",
            TraceSection.Resultado,
            "Lo que la declaración anual pedirá además del Modelo 130",
            [new("liabilityOnActivity", Show(liability)), new("modelo130Advances", Show(input.Modelo130Advances)), new("renta", Invariant($"{window.Start} … {window.End}"))],
            Invariant($"max(0, {Show(liability)} − {Show(input.Modelo130Advances)}) = {Show(gap)}, due {window.Start} … {window.End} after tax year {config.TaxYear}, so by the end of {payableIn}"),
            gap.Amount,
            "config calendar.renta. Assumes the employer's retenciones settle the tax on the employment income alone, so only the activity's share is left. "
                + "Conservative (#2): a refund is not counted on, no deducciones (SPEC-006) are applied, and the LIRPF art. 32.2.3º reduction of the activity net is not applied"));

        var warnings = new List<Warning>();

        if (reduccionLost > Money.Zero)
        {
            warnings.Add(new Warning(
                WarningCodes.ReduccionTrabajoLost,
                WarningSeverity.Warning,
                $"Activity net income of {Euros(actividad)} is above the {Euros(irpf.Trabajo.Reduccion.OtherIncomeCap)} cap on income other than employment, so the reducción por trabajo of {Euros(reduccionLost)} is lost entirely."));
        }

        if (gap > Money.Zero)
        {
            // A positive gap needs a positive liability, which only a positive activity net produces, so the division is safe.
            var effectiveRate = liability.Amount / actividad.Amount;
            var taxed = input.Employment.Ingresos > Money.Zero
                ? $"Stacked on the employment income, the {Euros(actividad)} of activity net income adds {Euros(liability)} of tax"
                : $"With no employment income, the {Euros(actividad)} of activity net income is taxed {Euros(liability)}";

            warnings.Add(new Warning(
                WarningCodes.MarginalVsEffective,
                WarningSeverity.Warning,
                $"Modelo 130 advances total {Euros(input.Modelo130Advances)} this year, {Percent(config.Modelo130.Rate.Value)} of the activity net income less any minoración. "
                    + $"{taxed}: an effective rate of {Percent(effectiveRate)}, and {Percent(marginalRate.Value)} on its last euro. "
                    + Invariant($"The annual return will want {Euros(gap)} more, payable by {payableIn.Year:D4}-{window.End.Month:D2}-{window.End.Day:D2}.")));
        }

        return new AnnualTrueUpResult(liability.Round2(), marginalRate, reduccionLost, gap, window, payableIn, new CalculationTrace(steps), warnings);

        // A local function: declared inside Gap, it reads Gap's own variables (irpf, region, steps) without taking them as parameters.
        (Money Base, Money Cuota) CuotaIntegra(string scenario, string label, Money trabajoReducido, Money rendimientoActividad)
        {
            var rendimientos = trabajoReducido + rendimientoActividad;
            var baseLiquidable = Positive(rendimientos);
            steps.Add(new TraceStep(
                $"renta.{scenario}.base-liquidable",
                TraceSection.Bases,
                $"Base liquidable general, {label}",
                [new("rendimientoTrabajoReducido", Show(trabajoReducido)), new("rendimientoActividad", Show(rendimientoActividad))],
                Invariant($"max(0, {Show(trabajoReducido)} + {Show(rendimientoActividad)}) = {Show(baseLiquidable)}"),
                baseLiquidable.Amount,
                $"{Lirpf} art. 48 and 50: a negative balance is not taxed"));

            var estatal = ScalePart(
                $"renta.{scenario}.cuota-estatal",
                $"Cuota íntegra estatal, {label}",
                irpf.EscalaEstatal,
                baseLiquidable,
                Min(irpf.Minimos.Contribuyente, baseLiquidable),
                $"{Lirpf} art. 56.2 and 63.1; config irpf.escalaEstatal, irpf.minimos.contribuyente",
                steps);
            var autonomica = ScalePart(
                $"renta.{scenario}.cuota-autonomica",
                $"Cuota íntegra autonómica ({input.Region}), {label}",
                region.EscalaAutonomica,
                baseLiquidable,
                Min(region.Minimos.Contribuyente, baseLiquidable),
                $"{Lirpf} art. 56.3 and 74.1, business rule 9; config regions.{input.Region}. The mínimo is the region's own (minimosOverride), or the state's where the region approved none",
                steps);

            return (baseLiquidable, estatal + autonomica);
        }
    }

    private sealed record TrabajoNeto(Money RendimientoNetoArt20, Money RendimientoNeto);

    private static TrabajoNeto Trabajo(EmploymentIncome employment, Money otrosGastosConfig, List<TraceStep> steps)
    {
        var rnArt20 = employment.Ingresos - employment.SeguridadSocial;
        steps.Add(new TraceStep(
            "renta.trabajo.rendimiento-neto-previo",
            TraceSection.Trabajo,
            "Rendimiento neto del trabajo antes de otros gastos",
            [new("ingresos", Show(employment.Ingresos)), new("seguridadSocial", Show(employment.SeguridadSocial))],
            Invariant($"{Show(employment.Ingresos)} − {Show(employment.SeguridadSocial)} = {Show(rnArt20)}"),
            rnArt20.Amount,
            $"{Lirpf} art. 19.2.a. Art. 20 measures the reducción on this figure, before the art. 19.2.f otros gastos"));

        var otrosGastos = Min(otrosGastosConfig, Positive(rnArt20));
        var rendimientoNeto = rnArt20 - otrosGastos;
        steps.Add(new TraceStep(
            "renta.trabajo.rendimiento-neto",
            TraceSection.Trabajo,
            "Rendimiento neto del trabajo",
            [new("rendimientoNetoPrevio", Show(rnArt20)), new("otrosGastos", Show(otrosGastosConfig))],
            Invariant($"{Show(rnArt20)} − min({Show(otrosGastosConfig)}, max(0, {Show(rnArt20)})) = {Show(rendimientoNeto)}"),
            rendimientoNeto.Amount,
            $"config irpf.trabajo.otrosGastos; {Lirpf} art. 19.2.f, limited to the rendimiento íntegro less the other deductible expenses"));

        return new TrabajoNeto(rnArt20, rendimientoNeto);
    }

    private static Money Actividad(ActivityIncome activity, DificilJustificacionConfig dificilJustificacion, List<TraceStep> steps)
    {
        var previo = activity.Ingresos - activity.Gastos;
        var dj = Min(Positive(previo) * dificilJustificacion.Pct, dificilJustificacion.Max);
        var rendimientoNeto = previo - dj;

        steps.Add(new TraceStep(
            "renta.actividad.rendimiento-neto",
            TraceSection.Actividad,
            "Rendimiento neto de la actividad",
            [new("ingresos", Show(activity.Ingresos)), new("gastos", Show(activity.Gastos)), new("pct", dificilJustificacion.Pct.ToString()), new("max", Show(dificilJustificacion.Max))],
            Invariant($"previo = {Show(activity.Ingresos)} − {Show(activity.Gastos)} = {Show(previo)}; {Show(previo)} − min({dificilJustificacion.Pct} × max(0, {Show(previo)}), {Show(dificilJustificacion.Max)}) = {Show(rendimientoNeto)}"),
            rendimientoNeto.Amount,
            "SPEC-002 step 2, business rule 7; config irpf.actividad.dificilJustificacion. Gastos include the RETA cuota, a deductible expense of the titular (AEAT Manual práctico Renta 2025, cap. 7)"));

        return rendimientoNeto;
    }

    // Art. 32.3 reduces the net left after the art. 32.1 and 32.2 reductions, and the engine applies neither of those.
    private static Money ReduccionInicioActividad(ActivityIncome activity, Money rendimientoNeto, InicioActividadConfig config, List<TraceStep> steps)
    {
        var fromFormerEmployer = Money.Zero;
        string period;
        Money reduccion;
        string formula;

        if (activity.NewActivity is NewActivity.Started started)
        {
            fromFormerEmployer = started.IngresosFromFormerEmployer;
            period = started.Period == NewActivityPeriod.First
                ? "first period with a positive net (primer período impositivo en que sea positivo)"
                : "period after the first positive one (período impositivo siguiente)";

            var formerEmployerLimit = activity.Ingresos * config.FormerEmployerShare;
            if (fromFormerEmployer > formerEmployerLimit)
            {
                reduccion = Money.Zero;
                formula = Invariant($"ingresos from a former employer {Show(fromFormerEmployer)} > {config.FormerEmployerShare} × {Show(activity.Ingresos)} → 0");
            }
            else
            {
                reduccion = Min(Positive(rendimientoNeto), config.MaxRendimiento) * config.Pct;
                formula = Invariant($"{config.Pct} × min(max(0, {Show(rendimientoNeto)}), {Show(config.MaxRendimiento)}) = {Show(reduccion)}");
            }
        }
        else
        {
            period = "not a newly started activity, or past the period after its first positive one";
            reduccion = Money.Zero;
            formula = "established activity → 0";
        }

        steps.Add(new TraceStep(
            "renta.actividad.reduccion-inicio",
            TraceSection.Actividad,
            "Reducción por inicio de una actividad económica",
            [
                new("rendimientoNeto", Show(rendimientoNeto)),
                new("ingresos", Show(activity.Ingresos)),
                new("ingresosFromFormerEmployer", Show(fromFormerEmployer)),
                new("pct", config.Pct.ToString()),
                new("maxRendimiento", Show(config.MaxRendimiento)),
                new("formerEmployerShare", config.FormerEmployerShare.ToString()),
            ],
            Invariant($"{formula}; rendimiento neto reducido = {Show(rendimientoNeto)} − {Show(reduccion)} = {Show(rendimientoNeto - reduccion)}"),
            reduccion.Amount,
            $"{Lirpf} art. 32.3, {period}; config irpf.actividad.inicioActividad; AEAT Manual práctico Renta 2025, cap. 7, fase 3. "
                + "Art. 32.2.1º is ruled out for this profile (SPEC-003 §0): it requires every sale to go to one client or the taxpayer to be a TRADE (2.º b), "
                + "at least 70 % of ingresos under retención, which foreign payers never withhold (2.º f), and no employment income (2.º e). "
                + "Art. 32.2.3º, for rentas no exentas below 12,000 including the activity's, is not applied, and art. 32.1 (irregular income) has no input here; "
                + "leaving either out can only overstate the net"));

        return reduccion;
    }

    // LIRPF art. 20 as in force for 2025, limited so the rendimiento neto reducido is never negative.
    private static Money Reduccion(string scenario, string title, TrabajoNeto trabajo, Money otrasRentas, ReduccionTrabajoConfig config, List<TraceStep> steps)
    {
        var rn = trabajo.RendimientoNetoArt20;
        var (amount, formula) =
            otrasRentas > config.OtherIncomeCap ? (Money.Zero, Invariant($"otras rentas {Show(otrasRentas)} > {Show(config.OtherIncomeCap)} → 0"))
            : rn >= config.T3 ? (Money.Zero, Invariant($"{Show(rn)} >= {Show(config.T3)} → 0"))
            : rn <= config.T1 ? (config.Fixed, Invariant($"{Show(rn)} <= {Show(config.T1)} → {Show(config.Fixed)}"))
            : rn <= config.T2 ? Taper(config.Fixed, config.K1, rn, config.T1)
            : Taper(config.Fixed - new Money(config.K1 * (config.T2 - config.T1).Amount), config.K2, rn, config.T2);

        var applied = Min(amount, Positive(trabajo.RendimientoNeto));
        steps.Add(new TraceStep(
            $"renta.trabajo.reduccion.{scenario}",
            TraceSection.Trabajo,
            title,
            [new("rendimientoNetoArt20", Show(rn)), new("otrasRentas", Show(otrasRentas)), new("otherIncomeCap", Show(config.OtherIncomeCap))],
            Invariant($"{formula}; limited to max(0, {Show(trabajo.RendimientoNeto)}) = {Show(applied)}"),
            applied.Amount,
            $"config irpf.trabajo.reduccion; {Lirpf} art. 20, where the third band starts from the second band's value at t2; business rule 6. "
                + "Otras rentas are the activity's rendimiento neto before its art. 32.3 reduction (AEAT Manual práctico Renta 2025, cap. 3, fase 3), the only income other than employment this calculation is given"));

        return applied;
    }

    private static (Money Amount, string Formula) Taper(Money start, decimal k, Money rn, Money from)
    {
        var amount = start - new Money(k * (rn - from).Amount);
        return (amount, Invariant($"{Show(start)} − {k} × ({Show(rn)} − {Show(from)}) = {Show(amount)}"));
    }

    // SPEC-002 step 6: the scale on the base less the same scale on the part of the base the mínimo covers.
    private static Money ScalePart(string id, string title, Scale scale, Money baseLiquidable, Money minimo, string reference, List<TraceStep> steps)
    {
        var onBase = ScaleCalculator.Cuota(scale, baseLiquidable.Amount);
        var onMinimo = ScaleCalculator.Cuota(scale, minimo.Amount);
        var cuota = new Money(onBase - onMinimo);

        steps.Add(new TraceStep(
            id,
            TraceSection.Cuota,
            title,
            [new("baseLiquidable", Show(baseLiquidable)), new("minimo", Show(minimo))],
            Invariant($"scale({Show(baseLiquidable)}) − scale({Show(minimo)}) = {onBase} − {onMinimo} = {Show(cuota)}"),
            cuota.Amount,
            reference));

        return cuota;
    }

    private static Money Positive(Money money) => money > Money.Zero ? money : Money.Zero;

    private static Money Min(Money a, Money b) => a < b ? a : b;

    private static string Show(Money money) => Invariant($"{money.Amount}");

    // SPEC-010 §5: text for the user shows two decimals and the euro sign. "0.00" formats without changing the stored value.
    private static string Euros(Money money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";

    private static string Percent(decimal fraction) => (fraction * 100m).ToString("0.00", CultureInfo.InvariantCulture) + " %";
}
