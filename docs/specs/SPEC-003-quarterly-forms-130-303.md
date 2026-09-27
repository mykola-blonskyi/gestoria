# SPEC-003 — Quarterly Forms: Modelo 130 (IRPF advance) and Modelo 303 (IVA)


**Status:** Draft · **Phase:** 1 · **Theory refs:** §7.3, §7.4, §8.1–8.4, §12, §15.4

## 0. The v1.0 profile (2026-09-18)

The author registers in January 2027 as **Profesional**, with clients in the **EU and the US**. That settles several things this spec left conditional:

- **Modelo 130 is required.** Foreign payers have no Spanish withholding obligation, so 0 % of activity income carries retención, far below the 70 % exemption threshold in §1.
- **`FacturaEmitida.RetencionRate` is 0 for every foreign client.** The 7 % versus 15 % question in SPEC-001 applies only to Spanish business payers. Do not default the field from `AutonomoRegistration.RetencionRate`.
- **Modelo 303 will usually show a refund or `a compensar`.** EU B2B services are reverse charge and US services fall outside Spanish IVA territory, so IVA repercutido is near zero while IVA soportado on Spanish purchases is not.
- **Modelo 349 is a real quarterly obligation, not an aggregate.** See §2.1.
- **ROI and VIES registration is required before the first EU invoice**, via Modelo 036. Invoicing an EU business without it means charging Spanish IVA you should not have charged.

## 1. Modelo 130

### Obligation
Required if `Profile.Activity != null` and (ActivityKind == Empresarial OR share of last year's income subject to retención < `config.modelo130.retencionExemptionShare` (0.70)). First year: assume required unless user overrides.

### Calculation (cumulative year-to-date)
```
ingresosYTD  = Σ FacturaEmitida.Base, AccrualDate in [1 Jan, quarter end]
gastosYTD    = Σ deductible expenses + cuotaSS, same window     (same rules as SPEC-002 step 2)
dj           = min(config.irpf.actividad.dificilJustificacion.pct × max(0, ingresosYTD − gastosYTD), config.irpf.actividad.dificilJustificacion.max)
netYTD       = ingresosYTD − (gastosYTD + dj)                  (casilla 02 = gastosYTD + dj)
base         = max(0, netYTD)
casilla07    = config.modelo130.rate (0.20) × base
             − Σ pagos130 already paid this year      (casilla 05: the positive casilla 07 of earlier quarters, not the amounts paid)
             − Σ retenciones YTD
casilla12    = max(0, casilla07)                     (07 + 11; casilla 11 is agricultural estimación objetiva and does not apply)
casilla14    = casilla12 − minoración                (config: net income last year ≤ 12,000 → banded reduction per quarter)
casilla15    = min(pendingNegative, casilla14) if casilla14 > 0, else 0
pago         = casilla14 − casilla15
             − mortgage deduction min(config.modelo130.mortgageRate × ingresosYTD, config.modelo130.mortgageCap)
result       = max(0, pago); pendingNegative += max(0, −pago)   // only a minoración above casilla 12 makes pago negative; deducted in later quarters of the same year
```
Golden #3 (Σ = 8,080), #5 (loss-making quarter → 0 and carry-over).

Difícil justificación is always in casilla 02, measured on the net to date and capped at the annual maximum, because v1 supports only estimación directa simplificada (glossary). RD 439/2007 art. 30.2ª sets it at 5 % of the net, at most 2,000 a year; art. 110.1.a takes the pago fraccionado as 20 % of that rendimiento neto under estimación directa in any modality; and the AEAT instructions put it in casilla 02 for the simplified modality (checked 2026-09-26, #33). Until #33 a `config.modelo130.applyDj` flag, set to `false`, left it out, which reproduced Theory §7.3 Example B (Σ 8,480) but not the law. If estimación directa normal is ever supported, the regime belongs on the taxpayer profile and gates both this and SPEC-002 step 2.

Three points the AEAT instructions for the form settle (casillas 05, 07, 12, 13, 15, 19; checked 2026-09-26, #8). Casilla 05 sums the positive casilla 07 of earlier quarters, which comes before the minoración, so each quarter's minoración is kept rather than clawed back by the next quarter. Casilla 12 enters a negative casilla 07 as zero: a quarter whose loss to date or retenciones undercut earlier payments pays nothing and carries nothing, because the cumulative base already carries the loss into the next quarter (G5: Q3 pays 0, Q4 pays 4,052, Σ 8,080). Only a minoración above casilla 12 leaves a negative result, which later quarters of the same year deduct up to a positive casilla 14 (RD 439/2007 art. 110.3.c). With no activity in the previous year, the previous year's net counts as zero, which takes the lowest minoración band.

The mortgage rate is `config.modelo130.mortgageRate` (0.02 for 2025, Theory §7.3), not a literal. It was written as `2 %` here until 2026-09-21, which put a tax number in a spec formula and from there into code, against ADR-0003. `mortgageCap` is per quarter.

Output: `Modelo130Result { Quarter, Lines: { "01": ingresosYTD, "02": gastosYTD + dj, "03": netYTD, … "19": resultado }, Trace, DueWindow }`. Line numbers come from `config.modelo130.lines`.

### Deadlines
From `config.calendar`: Q1 1–20 Apr, Q2 1–20 Jul, Q3 1–20 Oct, Q4 1–30 Jan (next year); if direct debit, warn 5 days earlier; a last day that is a Saturday, a Sunday or a national or regional holiday moves to the next working day (Orden EHA/672/2007 art. 7; Ley 39/2015 art. 30.2, 30.5 and 30.6; `FilingDeadline`, #40). National días inhábiles in `calendar.holidays`, the region's own in `regions.XX.holidays`. Local (municipal) holidays also count but are not modelled, so a deadline one of them would move can show early, never late. The console and the trace say so.

## 2. Modelo 303

```
IVA repercutido  = Σ FacturaEmitida.IvaAmount (by rate 21/10/4), Standard regime only, quarter by AccrualDate
IVA soportado    = Σ FacturaRecibida.IvaAmount where IvaDeductible && Kind == Completa (× DeductibleShare)
Reverse charge   = services bought from EU suppliers: devengado and deducible in the intracomunitarias lines (10/11, 36/37); services bought from
                   suppliers outside the EU (e.g. US SaaS): devengado in "otras operaciones con inversión del sujeto pasivo" (12/13), LIVA art. 84.Uno.2.º
Intra-EU sales   = services to EU businesses: base only (0 % IVA) in casilla 59 → Modelo 349 clave S (see §2.1)
Services to US   = not subject to Spanish IVA by the location rules: base in casilla 120, not in an "exentas" line (casilla 60 is exported goods)
result           = repercutido − soportado + compensación previous quarter (negative carried, "a compensar")
```
Output mirrors 130: numbered lines from `config.modelo303.lines` (#58; the design in force per Orden EHA/3786/2008 anexo I as amended by Orden HAC/819/2024 and, from 2T 2026, Orden HAC/27/2026), trace, due window. Annual Modelo 390 = aggregation of four 303 results (v1: totals only; verify whether 390 is still required for this profile, the exemptions have moved in recent years).

### 2.1 Modelo 349
Supplying services to EU businesses under reverse charge triggers the recapitulative declaration. With EU clients confirmed (§0), this is a filing the author makes every quarter, not a list to read.

Content: per EU client, the VIES VAT number and the total base for the period under clave S (services supplied); and per EU supplier, clave I for services acquired, which Orden EHA/769/2010 art. 2.d also requires. US clients and suppliers do not appear; 349 is intra-community only. The claves are `config.modelo349.claves`.

Filing period is quarterly by default. It becomes monthly when intra-community supplies exceed `config.modelo349.quarterlyFilingCap` (50,000 €) in the quarter or in any of the four before it (Orden EHA/769/2010 art. 10.2, as worded by Orden HAC/174/2020). The cap is per quarter, not per year.

v1.0 produces the form, not just the totals (2026-09-18). Output mirrors 130 and 303: `Modelo349Result { Quarter, Lines, Trace, DueWindow }`. `config.modelo349.lines` holds the positions of the tipo-2 record fields (#58). A quarter with no intra-EU operations produces no filing, and saying so is part of the output.

**Reverse charge depends on the client's VAT number being valid in VIES**, not on the author believing it is. If it is not valid at the time of invoicing, Spanish IVA is chargeable and both the 303 and the 349 are wrong. So `Client.InVies` cannot be a user-entered boolean; it is a verified fact with a date. Whether v1.0 calls the VIES service or records a manual check is open.

## 3. Invoice amount reconstruction (used by SPEC-004 matcher)
Given a bank credit `X` from a client and a candidate invoice: `Total = Base × (1 + IvaRate) − Base × RetencionRate`. Match if `|X − Total| ≤ 0.01`. Golden #9: 1,060 → 1,000 / 210 / 150.

## 4. Acceptance
- Golden #3, #5, #9.
- Q4 130 sum over a year with no negative quarter equals `0.20 × annual net` exactly.
- 303 with a reverse-charge SaaS invoice nets to zero on that invoice.
