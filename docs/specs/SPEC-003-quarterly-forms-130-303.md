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
gastosYTD    = Σ deductible expenses + cuotaSS, same window     (same rules as SPEC-002 step 2; difícil justificación applied only if config.modelo130.applyDj)
netYTD       = ingresosYTD − gastosYTD
base         = max(0, netYTD)
pago         = config.modelo130.rate (0.20) × base
             − Σ retenciones YTD
             − Σ pagos130 already paid this year
             − minoración (config: net income last year < 12,000 → banded reduction)
             − mortgage deduction min(2 % × ingresosYTD, config.modelo130.mortgageCap per quarter)
result       = max(0, pago); carryNegative = min(0, pago)   // carried to next quarter of same year
```
Golden #3 (Σ = 8,480), #5 (loss-making quarter → 0 and carry-over).

Output: `Modelo130Result { Quarter, Lines: { "01": ingresosYTD, "02": gastosYTD, "03": net, … "19": resultado }, Trace, DueWindow }`. Line numbers come from `config.modelo130.lines`.

### Deadlines
From `config.calendar`: Q1 1–20 Apr, Q2 1–20 Jul, Q3 1–20 Oct, Q4 1–30 Jan (next year); if direct debit, warn 5 days earlier; weekend/holiday shift to next working day (national holidays in config; regional holidays v1.x).

## 2. Modelo 303

```
IVA repercutido  = Σ FacturaEmitida.IvaAmount (by rate 21/10/4), Standard regime only, quarter by AccrualDate
IVA soportado    = Σ FacturaRecibida.IvaAmount where IvaDeductible && Kind == Completa (× DeductibleShare)
Reverse charge   = for ReverseChargeEU purchases: add both repercutido and soportado (net 0), report in intracomunitarias lines
Intra-EU sales   = base only (0 % IVA) → Modelo 349 (see §2.1)
Exports          = base in "exentas" line
result           = repercutido − soportado + compensación previous quarter (negative carried, "a compensar")
```
Output mirrors 130: numbered lines from config, trace, due window. Annual Modelo 390 = aggregation of four 303 results (v1: totals only; verify whether 390 is still required for this profile, the exemptions have moved in recent years).

### 2.1 Modelo 349
Supplying services to EU businesses under reverse charge triggers the recapitulative declaration. With EU clients confirmed (§0), this is a filing the author makes every quarter, not a list to read.

Content: per EU client, the VIES VAT number and the total base for the period. US clients do not appear; 349 is intra-community only.

Filing period is quarterly by default and monthly above a volume threshold. Verify the current threshold against AEAT before Q1 2027, and put it in `config.modelo349`.

Decide before Q1 2027 whether v1.0 produces the 349 itself or only the per-client totals the author transcribes. The data is a by-product of the 303 calculation either way.

## 3. Invoice amount reconstruction (used by SPEC-004 matcher)
Given a bank credit `X` from a client and a candidate invoice: `Total = Base × (1 + IvaRate) − Base × RetencionRate`. Match if `|X − Total| ≤ 0.01`. Golden #9: 1,060 → 1,000 / 210 / 150.

## 4. Acceptance
- Golden #3, #5, #9.
- Q4 130 sum over a year with no negative quarter equals `0.20 × annual net` exactly.
- 303 with a reverse-charge SaaS invoice nets to zero on that invoice.
