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
rn        = ingresos − ss − config.trabajo.otrosGastos (2000)   [+ mobility/disability extras per config]
rnr       = max(0, rn − ReduccionTrabajo(rn, otrasRentas))
```
`ReduccionTrabajo` piecewise per `config.trabajo.reduccion` (thresholds t1/t2/t3 = 14,852 / 17,673.52 / 19,747.50; coefficients k1/k2 = 1.75 / 1.14; fixed 7,302 for 2025). **Guard:** if `otrasRentas > config.trabajo.reduccion.otherIncomeCap` (6,500) → 0 (golden #8). `otrasRentas` = all non-employment income (activity net, savings gross, rental net, imputed).

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
retActividad = Σ FacturaEmitida.RetencionAmount
pagos130   = Σ Pago130.AmountPaid for Year
```
Golden #3, #4.

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
Minimo = MinimoCalculator(profile)                                // golden #7
CIE = max(0, Cuota(Estatal, BLG) − Cuota(Estatal, Minimo)) + Cuota(AhorroEstatal, BLA)
CIA = max(0, Cuota(Autonomica[region], BLG) − Cuota(Autonomica[region], Minimo)) + Cuota(AhorroAutonomica, BLA)
```
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
