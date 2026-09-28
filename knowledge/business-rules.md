# Business Rules

Invariants the code must enforce. Each points to its spec and to the theory section that justifies it (vault). Not a tax tutorial.

---

## Rule 1 — No expense without a supporting document

A bank transaction classified as a deductible expense counts in any calculation **only** when linked to a confirmed `FacturaRecibida`. Unlinked = candidate, shown in the review queue. — SPEC-004 §3, Theory §15.2.

Until documents can be stored (SPEC-005), no expense can be linked, so none counts. The estimate names each line that waits, and the overview says how many (#73).

## Rule 2 — Nothing unconfirmed enters the ledger

Extracted fields below the confidence threshold, and any `UNCLEAR` transaction, require explicit user confirmation. The engine only ever sees confirmed rows. — SPEC-005 §4.8, SPEC-001 §5.

A bank line is confirmed by the user or by a rule marked `certain` in `config/transaction-rules.json`, and only a debit rule into a class whose money enters no figure of the estimate may be certain: no credit, and no activity income, RETA cuota or deductible expense, is confirmed without the user. A suggested or unclear line waits in the review queue and counts nowhere; its quarter is not taken as actuals until it is reviewed, since unknown income is not zero income (SPEC-004 §3, #73).

## Rule 3 — Devengo (accrual) attribution by default

Activity income and expenses belong to the year/quarter of the invoice date, not the payment date. *Criterio de caja* is a profile flag (warning-only in v1). — SPEC-001 §6, Theory §7.1.

## Rule 3b — Retención depends on the payer, not on the issuer

`FacturaEmitida.RetencionRate` is 0 unless the payer is a Spanish business or professional obliged to withhold. An autónomo's own 7 % or 15 % rate describes what Spanish clients withhold from him, not a property of his invoices. For EU and US clients it is 0, and that is why Modelo 130 is required for this profile. — SPEC-003 §0, SPEC-001 §3.

## Rule 4 — Reconstruct client payments from the invoice, never guess from the amount

A bank credit of 1,060 € is income 1,000 / IVA 210 / retención 150 only because the matching invoice says so. — SPEC-003 §3, SPEC-004 §3.1, Theory §7.4.

## Rule 5 — The annual certificate beats the sum of payslips

If `CertificadoRetenciones` disagrees with Σ nóminas, use the certificate and raise a warning. — SPEC-002 step 1, Theory §15.1.

## Rule 6 — Employment relief is lost entirely when other income > 6,500 €

`ReduccionTrabajo` is zero if non-employment income exceeds the cap, however small the salary. Each other income counts at its net amount, after its expenses and before its own reductions (so activity income before any art. 32.3 reduction). For an employee the cap is reached through savings net, rental net or imputed income, not only through activity income. — SPEC-002 step 1, golden #8, Theory §5.2.

## Rule 7 — Difícil justificación is 5 % capped at 2,000 € and never negative

— SPEC-002 step 2, golden #4, Theory §7.1.

## Rule 8 — Family circumstances are evaluated at 31 December

Age, children, dependants, region of residence: the year-end snapshot decides. Child minimums split 50/50 between parents filing individually. — SPEC-001 §3, golden #7, Theory §4.4.

## Rule 9 — Two scales, applied to the same base, added together

State scale + the **actual** regional scale of the residence region; never the "reference" regional scale. Missing regional scale in config is a hard error. — SPEC-002 step 6, Theory §4.1–4.2.

## Rule 10 — Money is `decimal`; rounding only at the casilla boundary

— ADR-0004, SPEC-002 §5.

## Rule 11 — Every 📅 value comes from `config/tax-years/YYYY.json`

No tax number in code. — ADR-0003, SPEC-007.

## Rule 12 — Credits are data; outcomes are explained

Each credit rule yields `Applied | NotApplied(reason) | Possible(missing documents)`; unknown facts are warnings, never silent failures. — SPEC-006.

## Rule 13 — Always compute the joint-return alternative when a spouse exists

Recommend the cheaper option and explain the difference. — SPEC-002 step 9, Theory §3.5.

## Rule 14 — Modelo 130 is cumulative from 1 January; a negative quarter pays 0 and carries over

— SPEC-003 §1, golden #5, Theory §7.3.

## Rule 15 — Personal data never appears in logs, fixtures or the repo

— SPEC-013, SPEC-011 §3.

## Rule 16 — The RETA tramo is chosen on the rendimiento computable, not the IRPF net

Rendimiento computable = the IRPF rendimiento neto (Modelo 100 casilla 0224: after difícil justificación, before any LIRPF art. 32 reduction) + the titular's own cuotas (casilla 0186), less 7 % gastos genéricos, spread over the months of alta. The cuotas are the ones deducted, which are the ones TGSS debits in the year: for closed months what TGSS actually charged, a fact the taxpayer states (#52); for projected months the cuota of the base de cotización the taxpayer states they pay, a base and never defaulted (#55). Neither is the cuota of the tramo the year settles on. The tramo only bounds what TGSS keeps, and it does so over the whole year: TGSS compares one average of the year's provisional bases with the tramo's base mínima and base máxima, so it keeps the year's debits, closed and projected months pooled, clamped between the cuotas at those bases summed over the months, and tops up or refunds the difference in a later year. No month is clamped on its own; tarifa plana months are not regularised. — LGSS art. 308.1.a–c, RD 2064/1995 art. 46.2 reglas 1.ª, 3.ª and 5.ª, Ley 20/2007 art. 38 ter.6, AEAT "Información para determinar el rendimiento neto", #15, #39, #52, #55.
