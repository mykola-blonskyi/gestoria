using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// Projected for the whole tax year. SeguridadSocial is the employee's own cotización, LIRPF art. 19.2.a.
public sealed record EmploymentIncome(Money Ingresos, Money SeguridadSocial);

// Projected for the whole tax year. Gastos include the titular's RETA cuota and exclude difícil justificación, as in Modelo130Input.
public sealed record ActivityIncome(Money Ingresos, Money Gastos);

// Modelo130Advances is the sum of the year's Modelo 130 results, paid and projected, as Modelo130Calculator produces them.
public sealed record AnnualTrueUpInput(EmploymentIncome Employment, ActivityIncome Activity, Money Modelo130Advances, string Region);

// LiabilityOnActivity is what the activity income adds to the annual cuota íntegra once stacked on the employment income.
// MarginalRate is the state plus regional tranche rate at the stacked base. Gap is what the annual return wants beyond the
// Modelo 130 advances, never below zero, payable within DueWindow and so by the end of PayableIn.
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

        var reduccionSolo = Reduccion("solo", "Reducción por trabajo, trabajo solo", trabajo, Money.Zero, irpf.Trabajo.Reduccion, steps);
        var reduccionStacked = Reduccion("stacked", "Reducción por trabajo, con la actividad", trabajo, actividad, irpf.Trabajo.Reduccion, steps);
        var reduccionLost = (reduccionSolo - reduccionStacked).Round2();

        var (_, solo) = CuotaIntegra("solo", "trabajo solo", trabajo.RendimientoNeto - reduccionSolo);
        var (stackedBase, stacked) = CuotaIntegra("stacked", "trabajo más actividad", trabajo.RendimientoNeto - reduccionStacked + actividad);

        var liability = stacked - solo;
        steps.Add(Step(
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
        steps.Add(Step(
            "renta.marginal-rate",
            TraceSection.Cuota,
            "Tipo marginal en la base con la actividad",
            [new("baseLiquidable", Show(stackedBase)), new("region", input.Region)],
            Invariant($"{estatalRate} estatal + {autonomicaRate} autonómico = {marginalRate}"),
            marginalRate.Value,
            $"{Lirpf} art. 63.1 and 74.1: the rate of the tranche of each scale that the next euro of base falls in"));

        steps.Add(Step(
            "renta.modelo130-advances",
            TraceSection.Resultado,
            "Pagos fraccionados del Modelo 130 del ejercicio",
            [new("modelo130Advances", Show(input.Modelo130Advances))],
            Invariant($"Σ Modelo 130 results of the year = {Show(input.Modelo130Advances)}"),
            input.Modelo130Advances.Amount,
            "Modelo130Calculator at config modelo130.rate. Retenciones on activity invoices are not credited here; they are zero for foreign payers (SPEC-003 §0)"));

        var gap = Positive(liability - input.Modelo130Advances).Round2();
        steps.Add(Step(
            "renta.gap",
            TraceSection.Resultado,
            "Lo que la declaración anual pedirá además del Modelo 130",
            [new("liabilityOnActivity", Show(liability)), new("modelo130Advances", Show(input.Modelo130Advances))],
            Invariant($"max(0, {Show(liability)} − {Show(input.Modelo130Advances)}) = {Show(gap)}"),
            gap.Amount,
            "Assumes the employer's retenciones settle the tax on the employment income alone, so only the activity's share is left. "
                + "Conservative (#2): a refund is not counted on and no deducciones (SPEC-006) are applied"));

        var window = config.Calendar.Renta;
        var payableIn = new YearMonth(config.TaxYear + window.End.YearOffset, window.End.Month);
        steps.Add(Step(
            "renta.due",
            TraceSection.Resultado,
            "Plazo de la declaración anual",
            [new("taxYear", Invariant($"{config.TaxYear}")), new("renta", Invariant($"{window.Start} … {window.End}"))],
            Invariant($"{window.Start} … {window.End} after tax year {config.TaxYear} → payable by the end of {payableIn}"),
            gap.Amount,
            "config calendar.renta"));

        var warnings = new List<Warning>();

        if (reduccionLost > Money.Zero)
        {
            warnings.Add(new Warning(
                WarningCodes.ReduccionTrabajoLost,
                WarningSeverity.Warning,
                Invariant($"Activity net income of {actividad} is above the {irpf.Trabajo.Reduccion.OtherIncomeCap} cap on income other than employment, so the reducción por trabajo of {reduccionLost} is lost entirely.")));
        }

        if (gap > Money.Zero)
        {
            warnings.Add(new Warning(
                WarningCodes.MarginalVsEffective,
                WarningSeverity.Warning,
                Invariant($"Modelo 130 advances {config.Modelo130.Rate} of the activity net income, {input.Modelo130Advances} this year. ")
                    + Invariant($"Stacked on the employment income, the activity income adds {liability.Round2()} of tax at a marginal rate of {marginalRate}. ")
                    + Invariant($"The annual return will want {gap} more, payable by {payableIn.Year:D4}-{window.End.Month:D2}-{window.End.Day:D2}.")));
        }

        return new AnnualTrueUpResult(liability.Round2(), marginalRate, reduccionLost, gap, window, payableIn, new CalculationTrace(steps), warnings);

        // A local function: declared inside Gap, it reads Gap's own variables (irpf, region, steps) without taking them as parameters.
        (Money Base, Money Cuota) CuotaIntegra(string scenario, string label, Money rendimientos)
        {
            var baseLiquidable = Positive(rendimientos);
            steps.Add(Step(
                $"renta.{scenario}.base-liquidable",
                TraceSection.Bases,
                $"Base liquidable general, {label}",
                [new("rendimientos", Show(rendimientos))],
                Invariant($"max(0, {Show(rendimientos)}) = {Show(baseLiquidable)}"),
                baseLiquidable.Amount,
                $"{Lirpf} art. 48: a negative balance is not taxed"));

            var minimo = Min(irpf.Minimos.Contribuyente, baseLiquidable);
            var estatal = ScalePart(
                $"renta.{scenario}.cuota-estatal",
                $"Cuota íntegra estatal, {label}",
                irpf.EscalaEstatal,
                baseLiquidable,
                minimo,
                $"{Lirpf} art. 56.2 and 63.1; config irpf.escalaEstatal, irpf.minimos.contribuyente",
                steps);
            var autonomica = ScalePart(
                $"renta.{scenario}.cuota-autonomica",
                $"Cuota íntegra autonómica ({input.Region}), {label}",
                region.EscalaAutonomica,
                baseLiquidable,
                minimo,
                $"{Lirpf} art. 74.1, business rule 9; config regions.{input.Region}. The state mínimo stands in for the regional one, which the config does not hold; "
                    + "VC's own (Ley 13/1997 art. 2 bis) is higher, so this overstates the regional cuota",
                steps);

            return (baseLiquidable, estatal + autonomica);
        }
    }

    private sealed record TrabajoNeto(Money RendimientoNetoArt20, Money RendimientoNeto);

    private static TrabajoNeto Trabajo(EmploymentIncome employment, Money otrosGastosConfig, List<TraceStep> steps)
    {
        var rnArt20 = employment.Ingresos - employment.SeguridadSocial;
        steps.Add(Step(
            "renta.trabajo.rendimiento-neto-previo",
            TraceSection.Trabajo,
            "Rendimiento neto del trabajo antes de otros gastos",
            [new("ingresos", Show(employment.Ingresos)), new("seguridadSocial", Show(employment.SeguridadSocial))],
            Invariant($"{Show(employment.Ingresos)} − {Show(employment.SeguridadSocial)} = {Show(rnArt20)}"),
            rnArt20.Amount,
            $"{Lirpf} art. 19.2.a. Art. 20 measures the reducción on this figure, before the art. 19.2.f otros gastos"));

        var otrosGastos = Min(otrosGastosConfig, Positive(rnArt20));
        var rendimientoNeto = rnArt20 - otrosGastos;
        steps.Add(Step(
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

        steps.Add(Step(
            "renta.actividad.rendimiento-neto",
            TraceSection.Actividad,
            "Rendimiento neto de la actividad",
            [new("ingresos", Show(activity.Ingresos)), new("gastos", Show(activity.Gastos)), new("pct", dificilJustificacion.Pct.ToString()), new("max", Show(dificilJustificacion.Max))],
            Invariant($"previo = {Show(activity.Ingresos)} − {Show(activity.Gastos)} = {Show(previo)}; {Show(previo)} − min({dificilJustificacion.Pct} × max(0, {Show(previo)}), {Show(dificilJustificacion.Max)}) = {Show(rendimientoNeto)}"),
            rendimientoNeto.Amount,
            "SPEC-002 step 2, business rule 7; config irpf.actividad.dificilJustificacion. Gastos include the RETA cuota, a deductible expense of the titular (AEAT Manual práctico Renta 2025, cap. 7)"));

        return rendimientoNeto;
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
        steps.Add(Step(
            $"renta.trabajo.reduccion.{scenario}",
            TraceSection.Trabajo,
            title,
            [new("rendimientoNetoArt20", Show(rn)), new("otrasRentas", Show(otrasRentas)), new("otherIncomeCap", Show(config.OtherIncomeCap))],
            Invariant($"{formula}; limited to max(0, {Show(trabajo.RendimientoNeto)}) = {Show(applied)}"),
            applied.Amount,
            $"config irpf.trabajo.reduccion; {Lirpf} art. 20, where the third band starts from the second band's value at t2; business rule 6. "
                + "Otras rentas are the activity's rendimiento neto, the only income other than employment this calculation is given"));

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

        steps.Add(Step(
            id,
            TraceSection.Cuota,
            title,
            [new("baseLiquidable", Show(baseLiquidable)), new("minimo", Show(minimo))],
            Invariant($"scale({Show(baseLiquidable)}) − scale({Show(minimo)}) = {onBase} − {onMinimo} = {Show(cuota)}"),
            cuota.Amount,
            reference));

        return cuota;
    }

    private static TraceStep Step(string id, TraceSection section, string title, IReadOnlyList<TraceInput> inputs, string formula, decimal output, string reference) =>
        new(id, section, title, inputs, formula, output, reference);

    private static Money Positive(Money money) => money > Money.Zero ? money : Money.Zero;

    private static Money Min(Money a, Money b) => a < b ? a : b;

    private static string Show(Money money) => Invariant($"{money.Amount}");
}
