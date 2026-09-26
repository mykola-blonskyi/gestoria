# SPEC-002 — IRPF Annual Calculation Engine (Modelo 100)

**Status:** Draft · **Phase:** 1 · **Theory refs:** §3.3–3.7, §4, §5.2, §7.1–7.2, §10, §15.4, §15.6

## 1. Purpose
Implement steps 1–9 of the annual algorithm (Theory §15.4) as a pure, deterministic function with a full trace. Replaces the prototype `IrpfTaxCalculator` (combined 19–47 % scale, no regional part, no mínimo, per-bracket rounding).

## 2. Interface

```csharp
public sealed record AnnualInput(
    TaxpayerProfile Profile,
    Ledger Ledger,                 // confirmed rows only
    TaxYearConfig Config,
    FilingMode Mode);              // Individual | Conjunta | Both (compare)

public sealed record AnnualResult(
    FilingMode Mode,
    CasillaSheet Casillas,         // SPEC-008
    Money Resultado,               // casilla 0670; >0 a ingresar, <0 a devolver
    CalculationTrace Trace,
    JointComparison? Joint,        // when Mode == Both
    IReadOnlyList<CreditOutcome> Credits,   // SPEC-006
    IReadOnlyList<Warning> Warnings,
    string ConfigHash);

public interface IIrpfAnnualCalculator { AnnualResult Run(AnnualInput input); }
```

## 3. Calculation steps (each step = one `TraceStep` with inputs, formula, output)

### Step 1 — Employment (`EmploymentIncomeCalculator`)
```
ingresos  = Σ nómina.TotalDevengado − Σ nómina.Exentas (+ en especie)      // if Certificado exists → use it, warn on mismatch
ss        = Σ nómina.SsTrabajador (+ union fees ≤ config.trabajo.unionFeeCap, + colegio profesional if mandatory)
ret       = Σ nómina.IrpfRetenido
rnPrevio  = ingresos − ss                                        // art. 19.2 a–e only
rn        = rnPrevio − min(config.trabajo.otrosGastos (2000), max(0, rnPrevio))   [+ mobility/disability extras per config]
rnr       = max(0, rn − ReduccionTrabajo(rnPrevio, otrasRentas))
```
`ReduccionTrabajo` piecewise per `config.trabajo.reduccion` (thresholds t1/t2/t3 = 14,852 / 17,673.52 / 19,747.50; coefficients k1/k2 = 1.75 / 1.14; fixed 7,302 for 2025). It is measured on `rnPrevio`, **before** the 2,000 of otros gastos: LIRPF art. 20 as in force for 2025 defines the rendimiento neto for this purpose as the íntegro less the expenses of art. 19.2 a) to e), and the AEAT Manual práctico Renta 2025 (cap. 3, fase 3) repeats it. **Guard:** if `otrasRentas > config.trabajo.reduccion.otherIncomeCap` (6,500) → 0 (golden #8). `otrasRentas` = the algebraic sum of all non-employment income, each at its net amount (after its expenses, before its own reductions): activity net, savings net, rental net, imputed income and gains, per the AEAT Manual práctico Renta 2025, cap. 3, fase 3. The activity net is therefore tested against the cap before any LIRPF art. 32 reduction, including the art. 32.3 reduction for a new activity (#30).

### Step 2 — Activity (`ActivityIncomeCalculator`)
```
ingresos   = Σ FacturaEmitida.Base where AccrualDate.Year == Year
gastos     = Σ Deductible(FacturaRecibida) + Σ cuotaSS(TGSS transactions)
Deductible(f) = f.Base × f.DeductibleShare  (+ IVA if not deductible for IVA purposes)
                asset → annual depreciation = Cost / UsefulLifeYears × months/12 (config.actividad.depreciation)
                home utilities → f.Base × Profile.Activity.HomeOfficeShare × config.actividad.homeUtilitiesFactor (0.30)
previo     = ingresos − gastos
dj         = previo > 0 ? min(previo × config.actividad.dificilJustificacion.pct, config.actividad.dificilJustificacion.max) : 0
rnActividad = previo − dj
reduccionInicio = profile says new activity, first positive period or the one after it (never inferred)
                  and ingresosFromFormerEmployer ≤ ingresos × config.actividad.inicioActividad.formerEmployerShare (0.50)
                ? config.actividad.inicioActividad.pct (0.20) × min(max(0, rnActividad), config.actividad.inicioActividad.maxRendimiento (100,000)) : 0
rnActividadReducido = rnActividad − reduccionInicio          // enters BIG; the step 1 otrasRentas cap still uses rnActividad
retActividad = Σ FacturaEmitida.RetencionAmount
pagos130   = Σ Pago130.AmountPaid for Year
```
Golden #3, #4, G13.

`reduccionInicio` is LIRPF art. 32.3 (AEAT Manual práctico Renta 2025, cap. 7, fase 3). A new activity is one started with no economic activity at all in the year before its start date, ignoring any that ceased without a positive net; the reduction applies in the first period whose net is positive and in the period after it, and not in a period where more than half the ingresos come from someone who paid the taxpayer employment income in the year before the start. It is an annual-return reduction: Modelo 130 casilla 03 applies only art. 32.1 (AEAT instrucciones del modelo 130). Art. 32.2.1º–2º does not apply to the v1.0 profile (SPEC-003 §0), because it needs at least 70 % of ingresos under retención and foreign payers withhold none; art. 32.2.3º (rentas no exentas below 12,000) is not modelled, which can only overstate the tax.

The annual true-up's `AnnualTrueUpResult.MarginalRate` is the state plus regional tranche rate at the stacked base liquidable: a rate on the base, not on activity receipts. While `reduccionInicio` applies and `rnActividad` is below `maxRendimiento`, one more euro of activity net adds only `1 − inicioActividad.pct` of a euro to the base, so a consumer that applies the rate to receipts (#10, #15) must scale it by that factor. The MARGINAL_VS_EFFECTIVE warning does.

### Step 3 — Savings and property
- Capital mobiliario: Σ interest/dividends gross; retención Σ separately.
- Ganancias/pérdidas: FIFO per asset; net; loss offsetting limited to `config.ahorro.lossOffsetPct` (25 %) of positive capital mobiliario; remainder carried forward `carryForwardYears` (v1: computed, stored as warning + value).
- Rental: income − deductible expenses (interest, IBI, community, repairs, depreciation `inmuebles.depreciationPct`) → reducción `inmuebles.rentalReductionPct` (v1: 50 % default + flags for 60/70/90).
- Imputación: `cadastralValue × (imputacionPct | imputacionPctRevisado) × daysAvailable/365` for non-habitual, non-rented properties.

### Step 4 — Bases
```
BIG = rnr + rnActividad + rentalNet + imputed + otherGeneralGains
BIA = capitalMobiliario + netAssetGains
```

### Step 5 — Reductions
```
BLG = max(0, BIG − reductions)   // pension plans (cap), conjunta reduction (3,400 / 2,150)
BLA = max(0, BIA − remainder)     // only what BIG could not absorb
```

### Step 6 — Cuotas
```
Cuota(scale, base) = Σ over tranches: rate_i × clamp(base − lower_i, 0, upper_i − lower_i)
MinimoEstatal    = MinimoCalculator(profile, config.irpf.minimos)                    // golden #7
MinimoAutonomico = MinimoCalculator(profile, config.regions[region].minimosOverride ?? config.irpf.minimos)
CIE = max(0, Cuota(Estatal, BLG) − Cuota(Estatal, MinimoEstatal)) + Cuota(AhorroEstatal, BLA)
CIA = max(0, Cuota(Autonomica[region], BLG) − Cuota(Autonomica[region], MinimoAutonomico)) + Cuota(AhorroAutonomica, BLA)
```
Each scale is measured against its own mínimo (LIRPF art. 56.3 and 74.1). A region that approved amounts of its own uses them for its scale; VC's are Ley 13/1997 art. 2 bis, a mínimo del contribuyente of 6,105 against the state 5,550 for 2025 (#29). The state scale always keeps the state mínimo.
The savings scale in config is stored as combined rates plus `estatalShare`, so both casillas 0545/0546 and the total can be produced. Golden #1, #2, #6.

### Step 7 — Cuota líquida
```
CL = max(0, CIE − deduccionesEstatales) + max(0, CIA − deduccionesAutonomicas)   // via SPEC-006 evaluator
```

### Step 8 — Result
```
Resultado = CL − foreignTaxCredit − ret − retActividad − retAhorro − pagos130 (− other pagos a cuenta)
```

### Step 9 — Conjunta
If spouse present and `Mode == Both`: run Individual for each spouse and Conjunta once; `JointComparison { IndividualSum, Conjunta, Recommended, Delta, Explanation }`.

## 4. Filing obligation (`FilingObligationChecker`)
Per Theory §3.7 thresholds in `config.obligacion`: single payer ≤ 22,000 → not required; ≥ 2 payers and second+ > 1,500 → threshold 15,876; activity income any amount → required. Golden #10. Informational — the engine always calculates.

## 5. Rounding
No rounding inside steps. `Modelo100Mapper` rounds each casilla to 2 dp `AwayFromZero`. `Resultado` is computed from rounded casillas to match AEAT presentation; the unrounded value is kept in the trace.

## 6. Trace format
```
TraceStep { Id, Section (Trabajo|Actividad|Ahorro|Inmuebles|Bases|Minimo|Cuota|Deducciones|Resultado),
            Title, Inputs: {name: value}, Formula: string (template with resolved numbers),
            Output: Money|Rate|Bool, References: { theory: "§7.2", casillas: [0224], ledgerRowIds: [...] } }
```
`Trace.Render(Culture)` must reproduce the tabular breakdown of Theory §5.3 / §7.2.

## 7. Warnings the engine must raise (non-exhaustive)
- Certificado vs Σ nóminas mismatch (> 1 €).
- Simplificada invoices included in gastos.
- Reducción por trabajo lost due to other income (golden #8) — with the amount lost.
- Two payers → probably "a ingresar" (Theory §3.6).
- Region has no scale in config → fail fast, never default to estatal.

## 8. Acceptance
- SPEC-011 golden cases 1–4, 6–8, 10 pass exactly.
- Property tests: `Cuota` monotonic non-decreasing; continuous at tranche boundaries; `Cuota(scale, 0) == 0`.
- Deterministic: same input twice → identical `Trace` JSON.
