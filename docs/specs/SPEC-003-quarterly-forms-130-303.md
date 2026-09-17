# SPEC-003 — Quarterly Forms: Modelo 130 (IRPF advance) and Modelo 303 (IVA)

> Deferred on 2026-09-18. Modelo 130 and Modelo 303 arise from economic activity. v1.0's user is an employee, so neither is filed. The spec keeps its number and its content; `docs/CONVENTIONS.md` forbids renumbering.

**Status:** Deferred to v1.x (ADR-0012) · **Phase:** 1 · **Theory refs:** §7.3, §7.4, §8.1–8.4, §12, §15.4

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
Intra-EU sales   = base only (0 % IVA) → Modelo 349 list (v1: produce the list, no form)
Exports          = base in "exentas" line
result           = repercutido − soportado + compensación previous quarter (negative carried, "a compensar")
```
Output mirrors 130: numbered lines from config, trace, due window. Annual Modelo 390 = aggregation of four 303 results (v1: totals only).

## 3. Invoice amount reconstruction (used by SPEC-004 matcher)
Given a bank credit `X` from a client and a candidate invoice: `Total = Base × (1 + IvaRate) − Base × RetencionRate`. Match if `|X − Total| ≤ 0.01`. Golden #9: 1,060 → 1,000 / 210 / 150.

## 4. Acceptance
- Golden #3, #5, #9.
- Q4 130 sum over a year with no negative quarter equals `0.20 × annual net` exactly.
- 303 with a reverse-charge SaaS invoice nets to zero on that invoice.
