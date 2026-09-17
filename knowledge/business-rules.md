# Business Rules

Invariants the code must enforce. Each points to its spec and to the theory section that justifies it (vault). Not a tax tutorial.

---

## Rule 1 — No expense without a supporting document

A bank transaction classified as a deductible expense counts in any calculation **only** when linked to a confirmed `FacturaRecibida`. Unlinked = candidate, shown in the review queue. — SPEC-004 §3, Theory §15.2.

## Rule 2 — Nothing unconfirmed enters the ledger

Extracted fields below the confidence threshold, and any `UNCLEAR` transaction, require explicit user confirmation. The engine only ever sees confirmed rows. — SPEC-005 §4.8, SPEC-001 §5.

## Rule 3 — Devengo (accrual) attribution by default

Activity income and expenses belong to the year/quarter of the invoice date, not the payment date. *Criterio de caja* is a profile flag (warning-only in v1). — SPEC-001 §6, Theory §7.1.

## Rule 4 — Reconstruct client payments from the invoice, never guess from the amount

A bank credit of 1,060 € is income 1,000 / IVA 210 / retención 150 only because the matching invoice says so. — SPEC-003 §3, SPEC-004 §3.1, Theory §7.4.

## Rule 5 — The annual certificate beats the sum of payslips

If `CertificadoRetenciones` disagrees with Σ nóminas, use the certificate and raise a warning. — SPEC-002 step 1, Theory §15.1.

## Rule 6 — Employment relief is lost entirely when other income > 6,500 €

`ReduccionTrabajo` is zero if non-employment income exceeds the cap, however small the salary. — SPEC-002 step 1, golden #8, Theory §5.2.

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
