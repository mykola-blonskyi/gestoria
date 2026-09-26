using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// The subset of SPEC-001's TaxpayerProfile the estimator reads. Employment is projected for the whole tax year, zero when there is none.
public sealed record TaxpayerProfile(string Region, EmploymentIncome Employment, AutonomoRegistration Activity);

// Modelo 036/037 facts. PreviousYear selects the casilla 13 minoración band and is stated, never inferred from Alta.
public sealed record AutonomoRegistration(DateOnly Alta, PreviousYear PreviousYear);

// Cumulative figures from 1 January to the end of a closed quarter, with Modelo130Input's gastos semantics (RETA cuota included).
public sealed record QuarterToDate(Quarter Quarter, Money IngresosYtd, Money GastosYtd);

// What the activity is expected to invoice and spend over the part of the tax year the actuals do not cover.
// Gastos exclude the RETA cuota, which the estimator derives from the tramo tables.
public sealed record ActivityProjection(Money Ingresos, Money Gastos);

// Actuals to date and the forward projection in one type (#2). Retenciones states who pays: business rule 3b, never defaulted from the profile.
public sealed record ActivityPicture(IReadOnlyList<QuarterToDate> Actuals, ActivityProjection Projection, Retenciones Retenciones);

public sealed record SetAsideInput(TaxpayerProfile Profile, ActivityPicture Activity, TaxYearConfig Config, Quarter AsOf);

public sealed record Modelo130Projection(Quarter Quarter, Money AIngresar, DueWindow DueWindow);

// #2, #10: the seam the whole set-aside feature is built around. HoldBackShare is a share of gross receipts, the rest are
// the components it is built from, each reported even when zero so a zero always carries its reason in Warnings.
public sealed record SetAsideResult(
    Rate HoldBackShare,
    Modelo130Projection NextPayment,
    Money MonthlyCuotaSs,
    Money AnnualTrueUpGap,
    Money IvaToSetAside,
    CalculationTrace Trace,
    IReadOnlyList<Warning> Warnings,
    string ConfigHash);

// The single entry point of the set-aside estimator (#2). A projection alone for now; folding in actuals to date is #15.
public static class SetAsideEstimator
{
    public static SetAsideResult Estimate(SetAsideInput input)
    {
        if (input.Activity.Actuals.Count > 0)
        {
            throw new NotSupportedException(
                "Actuals to date are not folded into the estimate yet (#15); pass a projection only.");
        }

        if (input.Activity.Retenciones is Retenciones.Withheld)
        {
            throw new NotSupportedException(
                "Spanish payers withhold retención and are charged IVA; the estimator covers only the SPEC-003 §0 profile of "
                    + "EU business and US clients, so its zero IVA would be wrong.");
        }

        if (input.Activity.Projection.Ingresos <= Money.Zero)
        {
            throw new ArgumentException("The hold-back share is a share of receipts and there are none.", nameof(input));
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

        var projection = input.Activity.Projection;
        var steps = new List<TraceStep>();

        var first = altaMonth.MonthsSince(new YearMonth(taxYear, 1)) > 0 ? altaMonth.Month : 1;
        // Enumerable.Range(start, count) yields start, start + 1, …: here the months from the first of activity to December.
        var activeMonths = Enumerable.Range(first, 13 - first).Select(month => new YearMonth(taxYear, month)).ToList();
        var n = activeMonths.Count;
        steps.Add(new TraceStep(
            "set-aside.months-of-activity",
            TraceSection.Actividad,
            "Meses de alta en el ejercicio",
            [new("alta", Invariant($"{alta:yyyy-MM-dd}")), new("taxYear", Invariant($"{taxYear}"))],
            Invariant($"{activeMonths[0]} … {new YearMonth(taxYear, 12)} = {n} meses"),
            n,
            "The months of alta in the tax year: TGSS averages the rendimiento over them and the projection is spread across them"));

        var expectedAnnualNet = (projection.Ingresos - projection.Gastos).Amount * 12m / n;
        steps.Add(new TraceStep(
            "set-aside.rendimiento-computable",
            TraceSection.SeguridadSocial,
            "Rendimiento computable para la cuota SS, anualizado",
            [new("ingresos", Show(projection.Ingresos)), new("gastos", Show(projection.Gastos)), new("meses", Invariant($"{n}"))],
            Invariant($"({Show(projection.Ingresos)} − {Show(projection.Gastos)}) × 12 / {n} = {expectedAnnualNet}"),
            expectedAnnualNet,
            "LGSS art. 308.1.c (boe.es consolidated RDL 8/2015): the IRPF rendimiento neto plus the titular's own cuotas, so the projection before the RETA cuota, "
                + "averaged over the months of alta. Difícil justificación is not deducted, which over-reserves (#2)"));

        var tramos = config.SeguridadSocial.Tramos;
        var tarifaPlana = config.SeguridadSocial.TarifaPlana;
        var cuotas = activeMonths
            .Select(month => (Month: month, Result: MonthlyCuotaCalculator.Cuota(new MonthlyCuotaInput(alta, expectedAnnualNet, month), tramos, tarifaPlana)))
            .ToList();

        var quarterCuotas = cuotas.Where(c => (c.Month.Month + 2) / 3 == (int)input.AsOf).ToList();
        var chosen = quarterCuotas.MaxBy(c => c.Result.Cuota);
        steps.AddRange(chosen.Result.Trace.Steps);
        steps.Add(new TraceStep(
            "set-aside.cuota-ss-month",
            TraceSection.SeguridadSocial,
            "Cuota SS mensual a reservar este trimestre",
            [new("quarter", input.AsOf.ToString())],
            Invariant($"max({string.Join(", ", quarterCuotas.Select(c => Invariant($"{c.Month} {c.Result.Cuota}")))}) = {chosen.Result.Cuota} ({chosen.Month})"),
            chosen.Result.Cuota,
            "#2, bias conservative: the highest month of the quarter, so a prorated month of alta never understates the monthly outflow"));

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

        var carry = Modelo130Carry.StartOfYear;
        var quarters = new List<Modelo130Result>();

        // Enum.GetValues<Quarter>() lists every value of the enum in numeric order, Q1 to Q4.
        foreach (var quarter in Enum.GetValues<Quarter>())
        {
            var k = activeMonths.Count(month => month.Month <= (int)quarter * 3);
            var cuotasToDate = new Money(cuotas.Take(k).Sum(c => c.Result.Cuota));
            var ingresosYtd = new Money(projection.Ingresos.Amount * k / n).Round2();
            var gastosYtd = (new Money(projection.Gastos.Amount * k / n) + cuotasToDate).Round2();
            steps.Add(new TraceStep(
                Invariant($"set-aside.{quarter}.to-date"),
                TraceSection.Actividad,
                Invariant($"Ingresos y gastos proyectados desde el 1 de enero, {quarter}"),
                [new("mesesHastaFinTrimestre", Invariant($"{k}")), new("meses", Invariant($"{n}")), new("cuotasSs", Show(cuotasToDate))],
                Invariant($"ingresos {Show(projection.Ingresos)} × {k} / {n} = {Show(ingresosYtd)}; gastos {Show(projection.Gastos)} × {k} / {n} + {Show(cuotasToDate)} = {Show(gastosYtd)}"),
                ingresosYtd.Amount,
                "An assumption of the estimate, not a rule: the projection is spread evenly over the months of alta, and gastos include the RETA cuotas to date as Modelo 130 casilla 02 does. Rounded to cents because both enter casillas"));

            var result = Modelo130Calculator.Pago(
                new Modelo130Input(quarter, ingresosYtd, gastosYtd, input.Activity.Retenciones, input.Profile.Activity.PreviousYear),
                carry,
                config);

            // "with" copies a record and changes only the listed properties, like { ...step, id } in TypeScript.
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
            "Σ a ingresar (casilla 19, never below zero) over the four quarters, each chained to the next through casillas 05 and 15"));

        var trueUp = AnnualTrueUpCalculator.Gap(
            new AnnualTrueUpInput(
                input.Profile.Employment,
                new ActivityIncome(projection.Ingresos, projection.Gastos + annualTgss),
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

        var ivaWarning = new Warning(
            WarningCodes.SetAsideEstimate,
            WarningSeverity.Info,
            "No IVA to set aside: invoices to EU businesses are reverse charge, so the client accounts for the IVA, and services to "
                + "US clients fall outside Spanish IVA territory; Modelo 303 will usually be a refund rather than a payment. This "
                + "assumes the SPEC-003 §0 client mix (EU business and US clients only).");

        var outflows = modelo130Year + trueUp.Gap + annualTgss;
        var exact = outflows.Amount / projection.Ingresos.Amount;

        // Math.Ceiling rounds toward positive infinity; scaled by 10,000 it rounds the share up to a hundredth of a percent.
        var share = Math.Min(1m, Math.Ceiling(exact * 10000m) / 10000m);
        steps.Add(new TraceStep(
            "set-aside.hold-back-share",
            TraceSection.Resultado,
            "Parte de cada cobro que no es tuya",
            [new("modelo130Year", Show(modelo130Year)), new("gap", Show(trueUp.Gap)), new("cuotasSs", Show(annualTgss)), new("ingresos", Show(projection.Ingresos))],
            Invariant($"min(1, ⌈({Show(modelo130Year)} + {Show(trueUp.Gap)} + {Show(annualTgss)}) / {Show(projection.Ingresos)}⌉) = min(1, ⌈{exact}⌉) = {share}"),
            share,
            "#2: of the gross receipts (invoice bases; these clients pay no IVA), what belongs to AEAT and TGSS: the year's Modelo 130 advances, "
                + "what the annual return wants beyond them (never below zero, so a refund is not counted on) and the year's TGSS cuotas. "
                + "Rounded up to a hundredth of a percent and capped at the whole payment: conservative"));

        var next = quarters[(int)input.AsOf - 1];

        return new SetAsideResult(
            new Rate(share),
            new Modelo130Projection(next.Quarter, next.AIngresar, next.DueWindow),
            new Money(chosen.Result.Cuota),
            trueUp.Gap,
            Money.Zero,
            new CalculationTrace(steps),
            [ivaWarning, .. chosen.Result.Warnings, .. trueUp.Warnings],
            config.ConfigHash);
    }

    private static string Show(Money money) => Invariant($"{money.Amount}");
}
