using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

public enum Quarter
{
    Q1 = 1,
    Q2 = 2,
    Q3 = 3,
    Q4 = 4,
}

// An abstract record with a private constructor admits only the records nested inside it: a closed union, like a TypeScript discriminated union.
public abstract record Retenciones
{
    private Retenciones() { }

    // SPEC-003 §0, business rule 3b: EU and US payers have no Spanish withholding obligation.
    public sealed record ForeignPayersOnly : Retenciones;

    public sealed record Withheld(Money YearToDate) : Retenciones;
}

// Selects the casilla 13 minoración band.
public abstract record PreviousYear
{
    private PreviousYear() { }

    public sealed record NoActivity : PreviousYear;

    public sealed record RendimientoNeto(Money Amount) : PreviousYear;
}

// Figures from 1 January to the end of the quarter. GastosYtd includes the cuota SS and excludes difícil justificación.
public sealed record Modelo130Input(Quarter Quarter, Money IngresosYtd, Money GastosYtd, Retenciones Retenciones, PreviousYear PreviousYear);

// What the earlier quarters of the same year hand to this one: casilla 05 and the casilla 19 negatives not yet deducted.
public sealed record Modelo130Carry(Money PagosAnteriores, Money NegativosPendientes)
{
    public static readonly Modelo130Carry StartOfYear = new(Money.Zero, Money.Zero);
}

public sealed record Modelo130Result(Quarter Quarter, Money Resultado, Modelo130Carry Carry, DueWindow DueWindow, CalculationTrace Trace)
{
    public Money AIngresar => Resultado > Money.Zero ? Resultado : Money.Zero;
}

// SPEC-003 §1, Theory §7.3. Casilla numbers and rules follow the AEAT modelo 130 instructions.
// Casilla 16 (deducción por vivienda) is not modelled; leaving it out can only overstate the payment.
public static class Modelo130Calculator
{
    private const string Instrucciones = "AEAT, instrucciones del modelo 130";

    public static Modelo130Result Pago(Modelo130Input input, Modelo130Carry carry, TaxYearConfig config)
    {
        var modelo130 = config.Modelo130;
        var steps = new List<TraceStep>();

        var dj = DificilJustificacion(input, modelo130.ApplyDj, config.Irpf.Actividad.DificilJustificacion, steps);

        var rendimientoNeto = input.IngresosYtd - input.GastosYtd - dj;
        steps.Add(Step(
            "m130.rendimiento-neto",
            "Rendimiento neto desde el 1 de enero (casilla 03)",
            [new("ingresosYtd", Show(input.IngresosYtd)), new("gastosYtd", Show(input.GastosYtd)), new("dificilJustificacion", Show(dj))],
            Invariant($"{Show(input.IngresosYtd)} − {Show(input.GastosYtd)} − {Show(dj)} = {Show(rendimientoNeto)}"),
            rendimientoNeto,
            $"{Instrucciones}, casillas 01–03"));

        var pagoBruto = (Positive(rendimientoNeto) * modelo130.Rate).Round2();
        steps.Add(Step(
            "m130.pago-bruto",
            "Porcentaje sobre el rendimiento neto positivo (casilla 04)",
            [new("rendimientoNeto", Show(rendimientoNeto)), new("rate", modelo130.Rate.ToString())],
            Invariant($"{modelo130.Rate} × max(0, {Show(rendimientoNeto)}) = {Show(pagoBruto)}"),
            pagoBruto,
            $"config modelo130.rate; {Instrucciones}, casilla 04: a negative casilla 03 counts as zero"));

        steps.Add(Step(
            "m130.pagos-anteriores",
            "Pagos fraccionados de trimestres anteriores (casilla 05)",
            [new("quarter", input.Quarter.ToString())],
            Invariant($"Σ positive casilla 07 of earlier quarters this year = {Show(carry.PagosAnteriores)}"),
            carry.PagosAnteriores,
            $"{Instrucciones}, casilla 05: negative casilla 07 amounts are not summed"));

        var retenciones = RetencionesYtd(input.Retenciones, steps);

        var casilla07 = pagoBruto - carry.PagosAnteriores - retenciones;
        steps.Add(Step(
            "m130.casilla-07",
            "Pago fraccionado previo (casilla 07)",
            [new("casilla04", Show(pagoBruto)), new("casilla05", Show(carry.PagosAnteriores)), new("casilla06", Show(retenciones))],
            Invariant($"{Show(pagoBruto)} − {Show(carry.PagosAnteriores)} − {Show(retenciones)} = {Show(casilla07)}"),
            casilla07,
            $"{Instrucciones}, casilla 07"));

        var minoracion = Minoracion(input.PreviousYear, modelo130.Minoracion, steps);

        var casilla14 = casilla07 - minoracion;
        var negativosDeducidos = carry.NegativosPendientes < Positive(casilla14) ? carry.NegativosPendientes : Positive(casilla14);
        steps.Add(Step(
            "m130.negativos-anteriores",
            "Resultados negativos de trimestres anteriores (casilla 15)",
            [new("casilla14", Show(casilla14)), new("negativosPendientes", Show(carry.NegativosPendientes))],
            Invariant($"casilla 14 = {Show(casilla07)} − {Show(minoracion)} = {Show(casilla14)}; deducted = {Show(negativosDeducidos)}, at most a positive casilla 14"),
            negativosDeducidos,
            $"{Instrucciones}, casillas 14–15; business rule 14"));

        var resultado = casilla14 - negativosDeducidos;
        var next = new Modelo130Carry(
            carry.PagosAnteriores + Positive(casilla07),
            carry.NegativosPendientes - negativosDeducidos + Positive(-resultado));
        steps.Add(Step(
            "m130.resultado",
            resultado < Money.Zero ? "Resultado negativo: nada a ingresar, se deduce en trimestres siguientes" : "Resultado a ingresar (casilla 19)",
            [new("casilla14", Show(casilla14)), new("casilla15", Show(negativosDeducidos))],
            Invariant($"{Show(casilla14)} − {Show(negativosDeducidos)} = {Show(resultado)}; negatives still pending for later quarters = {Show(next.NegativosPendientes)}"),
            resultado,
            $"{Instrucciones}, casillas 17 and 19; business rule 14: a negative result pays zero and carries to later quarters of the same year"));

        return new Modelo130Result(input.Quarter, resultado, next, config.Calendar.Modelo130[(int)input.Quarter - 1], new CalculationTrace(steps));
    }

    // Business rule 7: 5 % of a positive rendimiento neto previo, capped, never negative.
    private static Money DificilJustificacion(Modelo130Input input, bool applyDj, DificilJustificacionConfig config, List<TraceStep> steps)
    {
        if (!applyDj)
        {
            steps.Add(Step(
                "m130.dificil-justificacion",
                "Difícil justificación no aplicada",
                [new("applyDj", "false")],
                "config modelo130.applyDj is false → 0",
                Money.Zero,
                "config modelo130.applyDj"));

            return Money.Zero;
        }

        var previo = input.IngresosYtd - input.GastosYtd;
        var pct = (Positive(previo) * config.Pct).Round2();
        var dj = pct < config.Max ? pct : config.Max;

        steps.Add(Step(
            "m130.dificil-justificacion",
            "Gastos de difícil justificación",
            [new("rendimientoNetoPrevio", Show(previo)), new("pct", config.Pct.ToString()), new("max", Show(config.Max))],
            Invariant($"min({config.Pct} × max(0, {Show(previo)}), {Show(config.Max)}) = {Show(dj)}"),
            dj,
            "config modelo130.applyDj, irpf.actividad.dificilJustificacion; business rule 7"));

        return dj;
    }

    private static Money RetencionesYtd(Retenciones retenciones, List<TraceStep> steps)
    {
        if (retenciones is Retenciones.Withheld withheld)
        {
            steps.Add(Step(
                "m130.retenciones",
                "Retenciones soportadas desde el 1 de enero (casilla 06)",
                [new("retencionesYtd", Show(withheld.YearToDate))],
                Invariant($"Σ retenciones on invoices to Spanish payers = {Show(withheld.YearToDate)}"),
                withheld.YearToDate,
                $"{Instrucciones}, casilla 06"));

            return withheld.YearToDate;
        }

        steps.Add(Step(
            "m130.retenciones",
            "Retenciones soportadas: cero, los pagadores son extranjeros",
            [new("payers", "EU and US only")],
            "no payer is Spanish, so none withholds → 0",
            Money.Zero,
            "SPEC-003 §0, business rule 3b: EU and US payers have no Spanish withholding obligation; RetencionRate is never defaulted from the profile"));

        return Money.Zero;
    }

    private static Money Minoracion(PreviousYear previousYear, IReadOnlyList<MinoracionBand> bands, List<TraceStep> steps)
    {
        var (net, why) = previousYear is PreviousYear.RendimientoNeto known
            ? (known.Amount, "rendimiento neto of the previous year")
            : (Money.Zero, "no activity in the previous year, which the AEAT instructions count as a rendimiento neto of zero");

        var band = bands.Where(b => net <= b.PrevYearNetUpTo).MinBy(b => b.PrevYearNetUpTo);
        var amount = band?.AmountPerQuarter ?? Money.Zero;

        steps.Add(Step(
            "m130.minoracion",
            "Minoración por rendimientos bajos del ejercicio anterior (casilla 13)",
            [new("previousYearNet", Show(net))],
            band is null
                ? Invariant($"{Show(net)} is above every band → 0")
                : Invariant($"{Show(net)} <= {Show(band.PrevYearNetUpTo)} → {Show(amount)} this quarter"),
            amount,
            $"config modelo130.minoracion; RD 439/2007 art. 110.3.c; {Instrucciones}, casilla 13; {why}"));

        return amount;
    }

    private static TraceStep Step(string id, string title, IReadOnlyList<TraceInput> inputs, string formula, Money output, string reference) =>
        new(id, TraceSection.Modelo130, title, inputs, formula, output.Amount, reference);

    private static Money Positive(Money money) => money > Money.Zero ? money : Money.Zero;

    private static string Show(Money money) => Invariant($"{money.Amount}");
}
