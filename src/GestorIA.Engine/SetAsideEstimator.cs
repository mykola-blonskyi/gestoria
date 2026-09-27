using System.Globalization;
using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// The subset of SPEC-001's TaxpayerProfile the estimator reads. Employment is projected for the whole tax year, zero when there is none.
public sealed record TaxpayerProfile(string Region, EmploymentIncome Employment, AutonomoRegistration Activity);

// Modelo 036/037 facts as the taxpayer states them, never inferred from Alta: PreviousYear selects the casilla 13 minoración
// band, NewActivity the LIRPF art. 32.3 reduction in the annual return.
public sealed record AutonomoRegistration(DateOnly Alta, PreviousYear PreviousYear, NewActivity NewActivity);

// Cumulative figures from 1 January to the end of a closed quarter, with Modelo130Input's gastos semantics (RETA cuota included).
// CuotasSsYtd is the part of GastosYtd that is the RETA cuotas TGSS actually charged, whatever tramo the year settles on (#52).
public sealed record QuarterToDate(Quarter Quarter, Money IngresosYtd, Money GastosYtd, Money CuotasSsYtd);

// What the activity is expected to invoice and spend over the part of the tax year the actuals do not cover. Gastos exclude
// the RETA cuota, which the estimator derives from BaseCotizacion: the monthly base de cotización the taxpayer pays from the
// end of the actuals on. It lives here, not on AutonomoRegistration, because it prices exactly the months the projection
// covers and LGSS art. 308.1.a 3.ª lets the taxpayer change it during the year as the forecast of rendimientos changes. It is
// a base, not a cuota: the taxpayer picks a base in Import@ss, and a cuota would carry one year's tipo into the next.
public sealed record ActivityProjection(Money Ingresos, Money Gastos, Money BaseCotizacion);

// Actuals to date and the forward projection in one type (#2). Actuals are the closed quarters in order from the first quarter
// of activity. Retenciones states who pays: business rule 3b, never defaulted from the profile.
public sealed record ActivityPicture(IReadOnlyList<QuarterToDate> Actuals, ActivityProjection Projection, Retenciones Retenciones);

public sealed record SetAsideInput(TaxpayerProfile Profile, ActivityPicture Activity, TaxYearConfig Config, Quarter AsOf);

public sealed record Modelo130Projection(Quarter Quarter, Money AIngresar, DueWindow DueWindow);

// #2, #10: the seam the whole set-aside feature is built around. HoldBackShare is a share of the year's gross receipts, actual
// and projected; the rest are the components it is built from, each reported even when zero so a zero always carries its
// reason in Warnings. AnnualTrueUpGap is payable with the annual return, by the end of AnnualTrueUpPayableIn.
public sealed record SetAsideResult(
    Rate HoldBackShare,
    Modelo130Projection NextPayment,
    Money MonthlyCuotaSs,
    Money AnnualTrueUpGap,
    YearMonth AnnualTrueUpPayableIn,
    Money IvaToSetAside,
    CalculationTrace Trace,
    IReadOnlyList<Warning> Warnings,
    string ConfigHash);

// The single entry point of the set-aside estimator (#2). Actuals cover the closed quarters; the projection covers the rest (#15).
public static class SetAsideEstimator
{
    public static SetAsideResult Estimate(SetAsideInput input)
    {
        if (input.Activity.Retenciones is Retenciones.Withheld)
        {
            throw new NotSupportedException(
                "Spanish payers withhold retención and are charged IVA; the estimator covers only the SPEC-003 §0 profile of "
                    + "EU business and US clients, so its zero IVA would be wrong.");
        }

        var config = input.Config;
        var taxYear = config.TaxYear;
        var alta = input.Profile.Activity.Alta;
        var altaMonth = YearMonth.Of(alta);
        var asOfQuarterEnd = new YearMonth(taxYear, (int)input.AsOf * 3);

        if (asOfQuarterEnd.MonthsSince(altaMonth) < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.AsOf,
                Invariant($"Quarter {input.AsOf} of {taxYear} ends in {asOfQuarterEnd}, before the alta month {altaMonth} (alta {alta:yyyy-MM-dd})."));
        }

        var first = altaMonth.MonthsSince(new YearMonth(taxYear, 1)) > 0 ? altaMonth.Month : 1;
        // Enumerable.Range(start, count) yields start, start + 1, …: here the months from the first of activity to December.
        var activeMonths = Enumerable.Range(first, 13 - first).Select(month => new YearMonth(taxYear, month)).ToList();
        var n = activeMonths.Count;
        var firstQuarter = (first + 2) / 3;

        var actuals = input.Activity.Actuals;
        if (!actuals.Select(a => (int)a.Quarter).SequenceEqual(Enumerable.Range(firstQuarter, actuals.Count)))
        {
            throw new ArgumentException(
                Invariant($"Actuals are the closed quarters in order from Q{firstQuarter}, the first quarter of activity; got [{string.Join(", ", actuals.Select(a => a.Quarter))}]."),
                nameof(input));
        }

        var last = actuals.LastOrDefault();
        if (last is not null && last.Quarter > input.AsOf)
        {
            throw new ArgumentException(
                Invariant($"Actuals run to {last.Quarter}, past the as-of quarter {input.AsOf}; the next payment would already be {last.Quarter}'s."),
                nameof(input));
        }

        var projection = input.Activity.Projection;
        var actualThrough = last is null ? 0 : (int)last.Quarter * 3;
        var actualIngresos = last?.IngresosYtd ?? Money.Zero;
        var actualGastos = last?.GastosYtd ?? Money.Zero;
        var paidToDate = last?.CuotasSsYtd ?? Money.Zero;
        var projectedMonths = activeMonths.Where(m => m.Month > actualThrough).ToList();
        var projectedCount = projectedMonths.Count;

        if (projectedCount == 0 && (projection.Ingresos != Money.Zero || projection.Gastos != Money.Zero))
        {
            throw new ArgumentException("The actuals cover every month of the year, so there is none left for the projection to cover.", nameof(input));
        }

        var annualIngresos = actualIngresos + projection.Ingresos;
        if (annualIngresos <= Money.Zero)
        {
            throw new ArgumentException("The hold-back share is a share of receipts and there are none.", nameof(input));
        }

        var steps = new List<TraceStep>();

        steps.Add(new TraceStep(
            "set-aside.months-of-activity",
            TraceSection.Actividad,
            "Meses de alta en el ejercicio",
            [new("alta", Invariant($"{alta:yyyy-MM-dd}")), new("taxYear", Invariant($"{taxYear}"))],
            Invariant($"{activeMonths[0]} … {new YearMonth(taxYear, 12)} = {n} meses"),
            new TraceValue.Count(n),
            "The months of alta in the tax year: TGSS averages the rendimiento over them"));

        steps.Add(new TraceStep(
            "set-aside.actuals",
            TraceSection.Actividad,
            "Ingresos y gastos reales desde el 1 de enero",
            [.. actuals.SelectMany(a => new TraceInput[] { new(Invariant($"{a.Quarter}.ingresosYtd"), Show(a.IngresosYtd)), new(Invariant($"{a.Quarter}.gastosYtd"), Show(a.GastosYtd)) })],
            last is null
                ? "no closed quarter stated → 0; the projection covers every month of alta"
                : Invariant($"to the end of {last.Quarter} ({new YearMonth(taxYear, actualThrough)}): ingresos {Show(actualIngresos)}, gastos {Show(actualGastos)}"),
            new TraceValue.Money(actualIngresos),
            "#2, #15: what the taxpayer invoiced and spent, cumulative from 1 January as Modelo 130 casillas 01 and 02 take them, the RETA cuotas charged "
                + "included in gastos. For the quarters they cover they replace the projection, and earlier Modelo 130s are recomputed from them"));

        steps.Add(new TraceStep(
            "set-aside.projected-months",
            TraceSection.Actividad,
            "Meses que cubre la proyección",
            [new("ingresos", Show(projection.Ingresos)), new("gastos", Show(projection.Gastos))],
            projectedCount == 0 ? "none: the actuals cover the year = 0 meses" : Invariant($"{projectedMonths[0]} … {projectedMonths.Last()} = {projectedCount} meses"),
            new TraceValue.Count(projectedCount),
            "#15: the projection covers the months of alta after the last closed quarter, spread evenly over them. An assumption of the estimate, not a rule"));

        var netToDate = actualIngresos - actualGastos;
        var projectedNet = projection.Ingresos - projection.Gastos;
        var seguridadSocial = config.SeguridadSocial;
        var tarifaPlana = seguridadSocial.TarifaPlana;
        var baseCotizacion = projection.BaseCotizacion;
        var cuotaAtBase = seguridadSocial.CuotaAt(baseCotizacion);

        // Sum adds up what the lambda selects from every element, like reduce((total, month) => total + debit(month), 0).
        var projectedDebits = new Money(projectedMonths.Sum(month => MonthlyCuotaCalculator.Debit(alta, month, baseCotizacion, seguridadSocial)));
        var previo = netToDate + projectedNet - projectedDebits;
        var dificilJustificacion = config.Irpf.Actividad.DificilJustificacion.On(previo);
        var rendimientoNeto = previo - dificilJustificacion;
        var computable = (rendimientoNeto + paidToDate + projectedDebits).Amount * 12m / n;

        var cuotas = activeMonths
            .Select(month => (Month: month, Result: MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta, computable, month, baseCotizacion), seguridadSocial)))
            .ToList();
        var projectedCuotas = cuotas.Where(c => c.Month.Month > actualThrough).ToList();

        steps.Add(new TraceStep(
            "set-aside.cuotas-ss-to-date",
            TraceSection.SeguridadSocial,
            "Cuotas SS pagadas, ya deducidas en los gastos reales",
            [.. actuals.Select(a => new TraceInput(Invariant($"{a.Quarter}.cuotasSsYtd"), Show(a.CuotasSsYtd)))],
            last is null ? "no closed quarter stated → 0" : Invariant($"paid to the end of {last.Quarter}, as stated: {Show(paidToDate)}"),
            new TraceValue.Money(paidToDate),
            "LGSS art. 308.1.c 1.ª (boe.es consolidated RDL 8/2015, read 2026-09-27): the rendimiento computable is the IRPF rendimiento neto increased by the importe of the "
                + "titular's own cuotas, the ones the gastos to date deduct. For the closed months those are the cuotas TGSS charged, stated with the actuals (#52); "
                + "what TGSS keeps of them once the year's tramo is known enters set-aside.cuota-ss-year"));

        steps.Add(new TraceStep(
            "set-aside.cuotas-ss-projected",
            TraceSection.SeguridadSocial,
            "Cuotas SS proyectadas",
            [new("baseCotizacion", Show(baseCotizacion)), new("cuotaBaseCotizacion", Invariant($"{cuotaAtBase}")), new("meses", Invariant($"{projectedCount}"))],
            projectedCount == 0
                ? "none: the actuals cover the year → 0"
                : Invariant($"at the chosen base {Show(baseCotizacion)}: {string.Join(" + ", projectedCuotas.Select(c => Invariant($"{c.Month} {c.Result.Cuota}")))} = {Show(projectedDebits)}"),
            new TraceValue.Money(projectedDebits),
            "#15, LGSS art. 308.1.a 1.ª and 3.ª and 308.1.b (boe.es consolidated RDL 8/2015, read 2026-09-27): the months after the last closed quarter at the monthly base "
                + "the taxpayer chose, tarifa plana while it lasts, whatever the tramo. These are what TGSS will debit and what IRPF deducts; a top-up or refund under "
                + "308.1.c 3.ª–4.ª lands in a later year's return (casillas 0196 and 0197, not modelled)"));

        steps.Add(new TraceStep(
            "set-aside.rendimiento-computable",
            TraceSection.SeguridadSocial,
            "Rendimiento computable para la cuota SS, anualizado",
            [
                new("netoHastaHoy", Show(netToDate)),
                new("netoProyectado", Show(projectedNet)),
                new("cuotasSsPagadas", Show(paidToDate)),
                new("cuotasSsProyectadas", Show(projectedDebits)),
                new("dificilJustificacion", Show(dificilJustificacion)),
                new("meses", Invariant($"{n}")),
            ],
            Invariant($"previo {Show(netToDate)} + {Show(projectedNet)} − {Show(projectedDebits)} = {Show(previo)}; ")
                + Invariant($"casilla 0224 {Show(previo)} − {Show(dificilJustificacion)} = {Show(rendimientoNeto)}; ")
                + Invariant($"({Show(rendimientoNeto)} + {Show(paidToDate)} paid + {Show(projectedDebits)} projected) × 12 / {n} = {computable}"),
            new TraceValue.Money(new Money(computable)),
            "LGSS art. 308.1.c 1.ª and 3.ª (boe.es consolidated RDL 8/2015, read 2026-09-27): the IRPF rendimiento neto increased by the titular's own cuotas, "
                + "spread over the months of alta. AEAT, Información para determinar el rendimiento neto (sede.agenciatributaria.gob.es, updated 18 Sep 2026, read 2026-09-27): "
                + "TGSS takes Modelo 100 casilla 0224 plus casilla 0186, the cuotas. Casilla 0224 is after the difícil justificación (RD 439/2007 art. 30.2ª, "
                + "config irpf.actividad.dificilJustificacion) and before every LIRPF art. 32 reduction and the other reductions Modelo 100 2025 takes after it (casillas 0225, 0232–0234 and 0236–0237), "
                + "so the art. 32.3 reduction does not lower it. Previo is the year's ingresos less its gastos, the actual ones with the cuotas charged "
                + "and the projected ones with the cuotas debited at the chosen base, and casilla 0186 is the same cuotas: paid for the closed months, debited at the chosen base for the rest. "
                + "None of them depends on the tramo, so neither does the computable. The regularisation of earlier years' RETA cuotas (casillas 0196 and 0197) is not modelled. "
                + "The 2.ª gastos genéricos are deducted in ss.rendimiento-neto-mensual"));

        var quarterCuotas = cuotas.Where(c => (c.Month.Month + 2) / 3 == (int)input.AsOf).ToList();
        var chosen = quarterCuotas.MaxBy(c => c.Result.FullMonthCuota);
        steps.AddRange(chosen.Result.Trace.Steps);
        steps.Add(new TraceStep(
            "set-aside.cuota-ss-month",
            TraceSection.SeguridadSocial,
            "Cuota SS mensual a reservar este trimestre",
            [new("quarter", input.AsOf.ToString())],
            Invariant($"max({string.Join(", ", quarterCuotas.Select(c => Invariant($"{c.Month} {c.Result.FullMonthCuota}")))}) = {chosen.Result.FullMonthCuota} ({chosen.Month})"),
            new TraceValue.Money(new Money(chosen.Result.FullMonthCuota)),
            "#2, bias conservative: what TGSS debits for a whole month, tarifa plana while it lasts and the cuota at the chosen base after it, before the one-off proration "
                + "of the month of alta, in the quarter's highest month. So an alta late in the quarter shows what TGSS debits every month from the next one, not the prorated first charge, "
                + "and a quarter in which tarifa plana lapses shows the cuota after it"));

        var debitedYear = paidToDate + projectedDebits;
        var floorYear = cuotas.Sum(c => c.Result.Floor);
        var ceilingYear = cuotas.Sum(c => c.Result.Ceiling);

        // Math.Clamp(value, min, max) is Math.min(Math.max(value, min), max) in TypeScript.
        var annualTgss = new Money(Math.Clamp(debitedYear.Amount, floorYear, ceilingYear));
        steps.Add(new TraceStep(
            "set-aside.cuota-ss-year",
            TraceSection.SeguridadSocial,
            "Cuotas SS del ejercicio",
            [
                new("cuotasSsPagadas", Show(paidToDate)),
                new("cuotasSsProyectadas", Show(projectedDebits)),
                new("cuotasSsBaseMinima", Invariant($"{floorYear}")),
                new("cuotasSsBaseMaxima", Invariant($"{ceilingYear}")),
                new("meses", Invariant($"{n}")),
            ],
            Invariant($"debited {Show(paidToDate)} paid + {Show(projectedDebits)} projected = {Show(debitedYear)}, between {floorYear} at the base mínima and {ceilingYear} at the base máxima → ")
                + (debitedYear.Amount < floorYear ? Invariant($"topped up to {floorYear}")
                    : debitedYear.Amount > ceilingYear ? Invariant($"refunded down to {ceilingYear}")
                    : Invariant($"{Show(debitedYear)} stands")),
            new TraceValue.Money(annualTgss),
            "What TGSS keeps for the year. LGSS art. 308.1.c 3.ª–4.ª (boe.es consolidated RDL 8/2015, read 2026-09-27) and RD 2064/1995 art. 46.2 (boe.es "
                + "BOE-A-1996-1579, version in force from 2024-08-01, read 2026-09-27): regla 3.ª sums the provisional bases of all the year's months less the "
                + "tarifa plana days (1.ª), divides by those days and multiplies by 30; regla 5.ª compares that one average with the tramo's base mínima and base "
                + "máxima. Between them nothing moves; below or above, TGSS reclaims or refunds the total of the differences between each month's base and the base "
                + "mínima or máxima, at the period's tipo (7.ª). A whole month counts 30 days, as art. 45.1 prorates the cuota, so the average lies below the base "
                + "mínima exactly when the year's debits total less than the cuotas at it: TGSS keeps the debits of the closed and projected months together, "
                + "clamped between the cuotas at the two bases summed over the months. Tarifa plana months enter all three sums at their debit, so they stay as "
                + "debited (Ley 20/2007 art. 38 ter.6). Clamping cuotas rather than bases can differ from TGSS by the rounding of each month's cuota, a few cents over the year"));

        var lastTarifaPlanaMonth = tarifaPlana.LastMonth(alta);
        var monthsInForce = lastTarifaPlanaMonth.MonthsSince(altaMonth);
        var lapse = lastTarifaPlanaMonth.AddMonths(1);
        var lapseWhen = lapse.MonthsSince(new YearMonth(taxYear, 1)) < 0 ? "before this tax year"
            : lapse.Year > taxYear ? Invariant($"after this tax year, at the {taxYear} tipo de cotización")
            : "in this tax year";
        steps.Add(new TraceStep(
            "set-aside.tarifa-plana-lapse",
            TraceSection.SeguridadSocial,
            "Fin de la tarifa plana",
            [new("alta", Invariant($"{alta:yyyy-MM-dd}")), new("tarifaPlanaMonths", Invariant($"{monthsInForce}")), new("tarifaPlana", tarifaPlana.Amount is { } amount ? Show(amount) : "not in this configuration")],
            Invariant($"{altaMonth} + {monthsInForce} complete months → tarifa plana through {lastTarifaPlanaMonth}; from {lapse}, {lapseWhen}, the cuota at the chosen base {cuotaAtBase}"),
            new TraceValue.Money(new Money(cuotaAtBase)),
            "Ley 20/2007 art. 38 ter.1 (boe.es consolidated text, read 2026-09-27): from the alta through the complete calendar months after it, eleven when the alta is on the 1st (itself a complete calendar month) and twelve otherwise, per Seguridad Social's own reading of the benefit as the first 12 months of alta (portal.seg-social.gob.es); config seguridadSocial.tarifaPlana. "
                + "The art. 38 ter.2 extension for a net below the SMI must be requested and is not assumed, which over-reserves"));

        var carry = Modelo130Carry.StartOfYear;
        var quarters = new List<Modelo130Result>();

        // Enum.GetValues<Quarter>() lists every value of the enum in numeric order, Q1 to Q4.
        foreach (var quarter in Enum.GetValues<Quarter>().Where(q => (int)q >= firstQuarter))
        {
            var quarterEnd = (int)quarter * 3;
            Money ingresosYtd;
            Money gastosYtd;

            if (quarterEnd <= actualThrough)
            {
                var actual = actuals.Single(a => a.Quarter == quarter);
                ingresosYtd = actual.IngresosYtd;
                gastosYtd = actual.GastosYtd;
                steps.Add(new TraceStep(
                    Invariant($"set-aside.{quarter}.to-date"),
                    TraceSection.Actividad,
                    Invariant($"Ingresos y gastos reales desde el 1 de enero, {quarter}"),
                    [new("ingresosYtd", Show(ingresosYtd)), new("gastosYtd", Show(gastosYtd))],
                    Invariant($"actuals as stated: ingresos {Show(ingresosYtd)}, gastos {Show(gastosYtd)}"),
                    new TraceValue.Money(ingresosYtd),
                    "The taxpayer's own figures for a closed quarter, RETA cuotas included in gastos as Modelo 130 casilla 02 takes them"));
            }
            else
            {
                var k = projectedMonths.Count(month => month.Month <= quarterEnd);
                var cuotasProjected = new Money(cuotas.Where(c => c.Month.Month > actualThrough && c.Month.Month <= quarterEnd).Sum(c => c.Result.Cuota));
                ingresosYtd = (actualIngresos + new Money(projection.Ingresos.Amount * k / projectedCount)).Round2();
                gastosYtd = (actualGastos + new Money(projection.Gastos.Amount * k / projectedCount) + cuotasProjected).Round2();
                steps.Add(new TraceStep(
                    Invariant($"set-aside.{quarter}.to-date"),
                    TraceSection.Actividad,
                    Invariant($"Ingresos y gastos desde el 1 de enero, reales y proyectados, {quarter}"),
                    [new("realesHasta", last?.Quarter.ToString() ?? "none"), new("mesesProyectadosHastaFinTrimestre", Invariant($"{k}")), new("mesesProyectados", Invariant($"{projectedCount}")), new("cuotasSsProyectadas", Show(cuotasProjected))],
                    Invariant($"ingresos {Show(actualIngresos)} real + {Show(projection.Ingresos)} × {k} / {projectedCount} = {Show(ingresosYtd)}; gastos {Show(actualGastos)} real + {Show(projection.Gastos)} × {k} / {projectedCount} + {Show(cuotasProjected)} = {Show(gastosYtd)}"),
                    new TraceValue.Money(ingresosYtd),
                    "#15: the actuals to the last closed quarter plus the projection spread evenly over the months after it, an assumption of the estimate, not a rule. "
                        + "Gastos add the projected RETA cuotas of those months, as Modelo 130 casilla 02 does. Rounded to cents because both enter casillas"));
            }

            var result = Modelo130Calculator.Pago(
                new Modelo130Input(quarter, ingresosYtd, gastosYtd, input.Activity.Retenciones, input.Profile.Activity.PreviousYear, input.Profile.Region),
                carry,
                config);

            steps.AddRange(result.Trace.Steps.Select(step => step with { Id = Invariant($"{quarter}.{step.Id}") }));
            carry = result.Carry;
            quarters.Add(result);
        }

        var modelo130Year = new Money(quarters.Sum(q => q.AIngresar.Amount));
        steps.Add(new TraceStep(
            "set-aside.modelo130-year",
            TraceSection.Modelo130,
            "Modelo 130 del ejercicio",
            [.. quarters.Select(q => new TraceInput(q.Quarter.ToString(), Show(q.AIngresar)))],
            Invariant($"{string.Join(" + ", quarters.Select(q => Show(q.AIngresar)))} = {Show(modelo130Year)}"),
            new TraceValue.Money(modelo130Year),
            "Σ a ingresar (casilla 19, never below zero) over the quarters from the alta's on, each chained to the next through casillas 05 and 15, "
                + "so a loss to date lowers the cumulative net of the quarters after it and a minoración left over carries as a negative result. "
                + "A quarter that ends before the alta has no activity and so no pago fraccionado (RD 439/2007 art. 109.1), and no minoración to carry"));

        var annualGastos = actualGastos + projection.Gastos + projectedDebits;
        steps.Add(new TraceStep(
            "set-aside.annual-ingresos",
            TraceSection.Actividad,
            "Ingresos del ejercicio, reales y proyectados",
            [new("reales", Show(actualIngresos)), new("proyectados", Show(projection.Ingresos))],
            Invariant($"{Show(actualIngresos)} real + {Show(projection.Ingresos)} projected = {Show(annualIngresos)}"),
            new TraceValue.Money(annualIngresos),
            "#15: the actuals to the last closed quarter and the projection for the months after it"));
        steps.Add(new TraceStep(
            "set-aside.annual-gastos",
            TraceSection.Actividad,
            "Gastos del ejercicio, reales y proyectados, con las cuotas SS",
            [new("reales", Show(actualGastos)), new("proyectados", Show(projection.Gastos)), new("cuotasSsProyectadas", Show(projectedDebits))],
            Invariant($"{Show(actualGastos)} real + {Show(projection.Gastos)} projected + {Show(projectedDebits)} projected cuotas SS = {Show(annualGastos)}"),
            new TraceValue.Money(annualGastos),
            "#15: the actual gastos already hold the cuotas charged to date; the RETA cuota is a deductible expense of the titular (AEAT Manual práctico Renta 2025, cap. 7)"));

        var trueUp = AnnualTrueUpCalculator.Gap(
            new AnnualTrueUpInput(
                input.Profile.Employment,
                new ActivityIncome(annualIngresos, annualGastos, input.Profile.Activity.NewActivity),
                modelo130Year,
                input.Profile.Region),
            config);
        steps.AddRange(trueUp.Trace.Steps);

        steps.Add(new TraceStep(
            "set-aside.iva",
            TraceSection.Resultado,
            "IVA a reservar",
            [new("payers", "EU businesses and US clients")],
            "EU B2B is reverse charge and US services are outside Spanish IVA territory, so no invoice carries IVA repercutido → 0",
            new TraceValue.Money(Money.Zero),
            "SPEC-003 §0. IVA soportado on Spanish purchases makes Modelo 303 a refund, which is not counted on"));

        var warnings = new List<Warning>
        {
            new(
                WarningCodes.SetAsideEstimate,
                WarningSeverity.Info,
                "No IVA to set aside: invoices to EU businesses are reverse charge, so the client accounts for the IVA, and services to "
                    + "US clients fall outside Spanish IVA territory; Modelo 303 will usually be a refund rather than a payment. This "
                    + "assumes the SPEC-003 §0 client mix (EU business and US clients only)."),
        };

        // A quarter month before the lapse was priced at the tarifa plana amount above, so MonthlyCuotaCalculator has already
        // refused a pending one and Amount has a value here.
        if (quarterCuotas.Any(c => c.Month.MonthsSince(lapse) < 0))
        {
            warnings.Add(new Warning(
                WarningCodes.SetAsideEstimate,
                WarningSeverity.Warning,
                Invariant($"Tarifa plana ends with {lastTarifaPlanaMonth}: from {lapse} TGSS charges {Euros(new Money(cuotaAtBase))} a month instead of {Euros(tarifaPlana.Amount!.Value)}")
                    + (lapse.Year > taxYear ? Invariant($", at the {taxYear} tipo de cotización.") : ".")));
        }

        warnings.AddRange(chosen.Result.Warnings);
        warnings.AddRange(trueUp.Warnings);

        var outflows = modelo130Year + trueUp.Gap + annualTgss;
        var exact = outflows.Amount / annualIngresos.Amount;

        // Math.Ceiling rounds toward positive infinity; scaled by 10,000 it rounds the share up to a hundredth of a percent.
        var share = Math.Min(1m, Math.Ceiling(exact * 10000m) / 10000m);
        steps.Add(new TraceStep(
            "set-aside.hold-back-share",
            TraceSection.Resultado,
            "Parte de cada cobro que no es tuya",
            [new("modelo130Year", Show(modelo130Year)), new("gap", Show(trueUp.Gap)), new("cuotasSs", Show(annualTgss)), new("ingresos", Show(annualIngresos))],
            Invariant($"min(1, ⌈({Show(modelo130Year)} + {Show(trueUp.Gap)} + {Show(annualTgss)}) / {Show(annualIngresos)}⌉) = min(1, ⌈{exact}⌉) = {share}"),
            new TraceValue.Rate(new Rate(share)),
            "#2: of the year's gross receipts, actual and projected (invoice bases; these clients pay no IVA), what belongs to AEAT and TGSS: the year's Modelo 130 advances, "
                + "what the annual return wants beyond them (never below zero, so a refund is not counted on) and the year's TGSS cuotas. "
                + "One share for the whole year, so what was already paid is not netted against what was set aside, which the estimator cannot see. "
                + "Rounded up to a hundredth of a percent and capped at the whole payment: conservative"));

        var next = quarters.Single(q => q.Quarter == input.AsOf);

        return new SetAsideResult(
            new Rate(share),
            new Modelo130Projection(next.Quarter, next.AIngresar, next.DueWindow),
            new Money(chosen.Result.FullMonthCuota),
            trueUp.Gap,
            trueUp.PayableIn,
            Money.Zero,
            new CalculationTrace(steps),
            warnings,
            config.ConfigHash);
    }

    private static string Show(Money money) => Invariant($"{money.Amount}");

    // SPEC-010 §5: text for the user shows two decimals and the euro sign.
    private static string Euros(Money money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";
}
