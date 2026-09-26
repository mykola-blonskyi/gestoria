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
public sealed record QuarterToDate(Quarter Quarter, Money IngresosYtd, Money GastosYtd);

// What the activity is expected to invoice and spend over the part of the tax year the actuals do not cover.
// Gastos exclude the RETA cuota, which the estimator derives from the tramo tables.
public sealed record ActivityProjection(Money Ingresos, Money Gastos);

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
        var actualMonths = activeMonths.Where(m => m.Month <= actualThrough).ToList();
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
            n,
            "The months of alta in the tax year: TGSS averages the rendimiento over them"));

        steps.Add(new TraceStep(
            "set-aside.actuals",
            TraceSection.Actividad,
            "Ingresos y gastos reales desde el 1 de enero",
            [.. actuals.SelectMany(a => new TraceInput[] { new(Invariant($"{a.Quarter}.ingresosYtd"), Show(a.IngresosYtd)), new(Invariant($"{a.Quarter}.gastosYtd"), Show(a.GastosYtd)) })],
            last is null
                ? "no closed quarter stated → 0; the projection covers every month of alta"
                : Invariant($"to the end of {last.Quarter} ({new YearMonth(taxYear, actualThrough)}): ingresos {Show(actualIngresos)}, gastos {Show(actualGastos)}"),
            actualIngresos.Amount,
            "#2, #15: what the taxpayer invoiced and spent, cumulative from 1 January as Modelo 130 casillas 01 and 02 take them, the RETA cuotas charged "
                + "included in gastos. For the quarters they cover they replace the projection, and earlier Modelo 130s are recomputed from them"));

        steps.Add(new TraceStep(
            "set-aside.projected-months",
            TraceSection.Actividad,
            "Meses que cubre la proyección",
            [new("ingresos", Show(projection.Ingresos)), new("gastos", Show(projection.Gastos))],
            projectedCount == 0 ? "none: the actuals cover the year = 0 meses" : Invariant($"{projectedMonths[0]} … {projectedMonths.Last()} = {projectedCount} meses"),
            projectedCount,
            "#15: the projection covers the months of alta after the last closed quarter, spread evenly over them. An assumption of the estimate, not a rule"));

        var netToDate = actualIngresos - actualGastos;
        var projectedNet = projection.Ingresos - projection.Gastos;
        var tramos = config.SeguridadSocial.Tramos;
        var tarifaPlana = config.SeguridadSocial.TarifaPlana;

        var withoutCuotas = (netToDate + projectedNet).Amount * 12m / n;
        var cuotasToDate = actualMonths
            .Select(month => (Month: month, Cuota: MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta, withoutCuotas, month), tramos, tarifaPlana).Cuota))
            .ToList();
        var addedBack = cuotasToDate.Sum(c => c.Cuota);
        steps.Add(new TraceStep(
            "set-aside.cuotas-ss-to-date",
            TraceSection.SeguridadSocial,
            "Cuotas SS ya deducidas en los gastos reales",
            [new("netoHastaHoy", Show(netToDate)), new("netoProyectado", Show(projectedNet)), new("meses", Invariant($"{n}"))],
            cuotasToDate.Count == 0
                ? "no actuals → 0"
                : Invariant($"at ({Show(netToDate)} + {Show(projectedNet)}) × 12 / {n} = {withoutCuotas}: {string.Join(" + ", cuotasToDate.Select(c => Invariant($"{c.Month} {c.Cuota}")))} = {addedBack}"),
            addedBack,
            "LGSS art. 308.1.c (boe.es consolidated RDL 8/2015, read 2026-09-26): the rendimiento computable is the IRPF net increased by the titular's own cuotas, "
                + "and the gastos to date already deduct them, so they are added back. Estimated with MonthlyCuotaCalculator at the tramo of the net before them, "
                + "which is exact while tarifa plana applies"));

        var expectedAnnualNet = (netToDate.Amount + addedBack + projectedNet.Amount) * 12m / n;
        steps.Add(new TraceStep(
            "set-aside.rendimiento-computable",
            TraceSection.SeguridadSocial,
            "Rendimiento computable para la cuota SS, anualizado",
            [new("netoHastaHoy", Show(netToDate)), new("cuotasSumadas", Invariant($"{addedBack}")), new("netoProyectado", Show(projectedNet)), new("meses", Invariant($"{n}"))],
            Invariant($"({Show(netToDate)} + {addedBack} + {Show(projectedNet)}) × 12 / {n} = {expectedAnnualNet}"),
            expectedAnnualNet,
            "LGSS art. 308.1.c: the IRPF rendimiento neto plus the titular's own cuotas, so the actuals with their cuotas added back and the projection before the RETA cuota, "
                + "averaged over the months of alta. Difícil justificación is not deducted, which over-reserves (#2)"));

        var cuotas = activeMonths
            .Select(month => (Month: month, Result: MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta, expectedAnnualNet, month), tramos, tarifaPlana)))
            .ToList();

        var quarterCuotas = cuotas.Where(c => (c.Month.Month + 2) / 3 == (int)input.AsOf).ToList();
        var chosen = quarterCuotas.MaxBy(c => c.Result.FullMonthCuota);
        steps.AddRange(chosen.Result.Trace.Steps);
        steps.Add(new TraceStep(
            "set-aside.cuota-ss-month",
            TraceSection.SeguridadSocial,
            "Cuota SS mensual a reservar este trimestre",
            [new("quarter", input.AsOf.ToString())],
            Invariant($"max({string.Join(", ", quarterCuotas.Select(c => Invariant($"{c.Month} {c.Result.FullMonthCuota}")))}) = {chosen.Result.FullMonthCuota} ({chosen.Month})"),
            chosen.Result.FullMonthCuota,
            "#2, bias conservative: the whole-month cuota, before the one-off proration of the month of alta, in the quarter's highest month. "
                + "So an alta late in the quarter shows what TGSS debits every month from the next one, not the prorated first charge, "
                + "and a quarter in which tarifa plana lapses shows the cuota after it"));

        // Sum adds up what the lambda selects from every element, like reduce((total, c) => total + c.Result.Cuota, 0).
        var annualTgss = new Money(cuotas.Sum(c => c.Result.Cuota));
        steps.Add(new TraceStep(
            "set-aside.cuota-ss-year",
            TraceSection.SeguridadSocial,
            "Cuotas SS del ejercicio",
            [new("meses", Invariant($"{n}"))],
            Invariant($"{string.Join(" + ", cuotas.Select(c => Invariant($"{c.Month} {c.Result.Cuota}")))} = {Show(annualTgss)}"),
            annualTgss.Amount,
            "MonthlyCuotaCalculator for each month of alta; the same tramo every month, tarifa plana while it lasts"));

        var lastTarifaPlanaMonth = tarifaPlana.LastMonth(alta);
        var lapse = lastTarifaPlanaMonth.AddMonths(1);
        var cuotaAfterLapse = MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta, expectedAnnualNet, lapse), tramos, tarifaPlana).FullMonthCuota;
        var lapseWhen = lapse.MonthsSince(new YearMonth(taxYear, 1)) < 0 ? "before this tax year"
            : lapse.Year > taxYear ? Invariant($"after this tax year, at the {taxYear} tramos and projected net")
            : "in this tax year";
        steps.Add(new TraceStep(
            "set-aside.tarifa-plana-lapse",
            TraceSection.SeguridadSocial,
            "Fin de la tarifa plana",
            [new("alta", Invariant($"{alta:yyyy-MM-dd}")), new("tarifaPlanaMonths", Invariant($"{tarifaPlana.Months}")), new("tarifaPlana", Show(tarifaPlana.Amount))],
            Invariant($"{altaMonth} + {tarifaPlana.Months} complete months → tarifa plana through {lastTarifaPlanaMonth}; from {lapse}, {lapseWhen}, the tramo cuota {cuotaAfterLapse}"),
            cuotaAfterLapse,
            "Ley 20/2007 art. 38 ter.1 (boe.es consolidated text, read 2026-09-26): from the alta through the twelve complete calendar months after it; config seguridadSocial.tarifaPlana. "
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
                    ingresosYtd.Amount,
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
                    ingresosYtd.Amount,
                    "#15: the actuals to the last closed quarter plus the projection spread evenly over the months after it, an assumption of the estimate, not a rule. "
                        + "Gastos add the projected RETA cuotas of those months, as Modelo 130 casilla 02 does. Rounded to cents because both enter casillas"));
            }

            var result = Modelo130Calculator.Pago(
                new Modelo130Input(quarter, ingresosYtd, gastosYtd, input.Activity.Retenciones, input.Profile.Activity.PreviousYear),
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
            modelo130Year.Amount,
            "Σ a ingresar (casilla 19, never below zero) over the quarters from the alta's on, each chained to the next through casillas 05 and 15, "
                + "so a loss to date lowers the cumulative net of the quarters after it and a minoración left over carries as a negative result. "
                + "A quarter that ends before the alta has no activity and so no pago fraccionado (RD 439/2007 art. 109.1), and no minoración to carry"));

        var cuotasProjectedYear = new Money(cuotas.Where(c => c.Month.Month > actualThrough).Sum(c => c.Result.Cuota));
        var annualGastos = actualGastos + projection.Gastos + cuotasProjectedYear;
        steps.Add(new TraceStep(
            "set-aside.annual-ingresos",
            TraceSection.Actividad,
            "Ingresos del ejercicio, reales y proyectados",
            [new("reales", Show(actualIngresos)), new("proyectados", Show(projection.Ingresos))],
            Invariant($"{Show(actualIngresos)} real + {Show(projection.Ingresos)} projected = {Show(annualIngresos)}"),
            annualIngresos.Amount,
            "#15: the actuals to the last closed quarter and the projection for the months after it"));
        steps.Add(new TraceStep(
            "set-aside.annual-gastos",
            TraceSection.Actividad,
            "Gastos del ejercicio, reales y proyectados, con las cuotas SS",
            [new("reales", Show(actualGastos)), new("proyectados", Show(projection.Gastos)), new("cuotasSsProyectadas", Show(cuotasProjectedYear))],
            Invariant($"{Show(actualGastos)} real + {Show(projection.Gastos)} projected + {Show(cuotasProjectedYear)} projected cuotas SS = {Show(annualGastos)}"),
            annualGastos.Amount,
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
            0m,
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

        if (quarterCuotas.Any(c => c.Month.MonthsSince(lapse) < 0))
        {
            warnings.Add(new Warning(
                WarningCodes.SetAsideEstimate,
                WarningSeverity.Warning,
                Invariant($"Tarifa plana ends with {lastTarifaPlanaMonth}: from {lapse} TGSS charges {Euros(new Money(cuotaAfterLapse))} a month instead of {Euros(tarifaPlana.Amount)}")
                    + (lapse.Year > taxYear ? Invariant($", at the {taxYear} tramos and this year's projected net.") : ".")));
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
            share,
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
