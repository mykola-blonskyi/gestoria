# ADR-0013: v1.0 covers employee and autónomo scope; OCR and Madrid stay out

**Status:** Accepted · **Date:** 2026-09-18 · **Supersedes:** ADR-0012 · **Sequencing amended by:** [ADR-0014](ADR-0014-build-order-april-2027-cluster.md)

> The scope decision below stands. The sequencing clause does not: the author registers as an autónomo in January 2027, so three filings now fall due in April 2027 and the quarterly path is built first. See ADR-0014.

## Context
ADR-0012 cut the autónomo domain from v1.0. It rested on one stated fact and one unstated inference.

The fact: the author is not an autónomo. The inference: therefore the autónomo domain has no user and belongs in v1.x. The inference was not the author's, and it was wrong. The autónomo scope stays, and the author is sourcing test data for it.

This is worth recording as a failure mode rather than a typo. A fact about the present ("I am not X") is not a scope boundary ("X will never matter here"). The gap between the two is where the error lived.

## Premises

| Premise | Stated or concluded | Source |
|---|---|---|
| The author is an employee, not an autónomo | Stated | Author, 2026-09-18 |
| The autónomo scope stays in v1.0 | Stated | Author, 2026-09-18 |
| The author will source test data for the autónomo cases | Stated | Author, 2026-09-18 |
| The OCR service and Madrid stay out | Stated | Author, 2026-09-18 (Q9) |
| Renta 2026 is the only filing with a date the author must meet | Concluded, from the two facts above | This ADR |
| Whether the author will register as an autónomo | Resolved 2026-09-18: yes, January 2027. See ADR-0014 | Author |

## Decision
v1.0 covers the full employee and autónomo scope: **Modelo 100, Modelo 130 and Modelo 303**, Valencia only, data entered by hand.

Two cuts from ADR-0012 survive, because they were decided separately and have not been reversed:

- The OCR service stays out of v1.0. About a dozen documents a year does not pay for the longest phase in the plan, written in a second language.
- Madrid stays out. A fake region in a test fixture proves the region model is data-driven.

**Sequencing follows deadlines, not scope.** The author's own first filing is Renta 2026, due April to June 2027, as an employee. That is the only date Hacienda has set for this project. So the employee Modelo 100 path is built first and the autónomo path follows it, because the autónomo path has no deadline until the author registers. Restoring scope does not mean restoring the original order.

## Alternatives
- **Keep the cut, add autónomo in v1.x.** Rejected by the author. It was also weaker than it looked: the engine boundary is identical either way, so the cut bought sequencing rather than architecture, and sequencing can be had without cutting anything.
- **Keep autónomo but drop Modelo 303.** An autónomo profesional files 303 quarterly and it is not optional. Correctness is the product, so a tool that produces a 130 and stays silent about IVA is worse than no tool.

## Consequences
- SPEC-003 and SPEC-004 return to Draft. SPEC-005 stays Deferred with the OCR service.
- Goldens G3, G4, G5 and G9 return as gates. All ten gate v1.0, plus G8b below.
- **G8b stays.** G8 reaches the 6,500 € other-income cap through autónomo income, and restoring scope restores that case as written. The savings variant earned its place while the cut was in force: an employee with 8,000 € of dividends loses the same relief by a different route, and the engine must catch both. Keeping it costs one fixture.
- Three `_todo` blocks return to `config/tax-years/2025.example.json`: `modelo130.lines` and `minoracion`, `modelo303.lines`, and `seguridadSocial.tramos`. The Madrid block stays gone.
- The AEAT simulator cannot express Modelo 130 or Modelo 303, so those goldens cannot use it as an oracle. Published worked examples are a better source than the project's own theory document for exactly those cases, so SPEC-011 gains a third oracle tier, `published-example`, ranked between the simulator and the vault.
- Phase 1 restores `ActivityIncomeCalculator`, `Modelo130Calculator` and `Modelo303Calculator`.
- The plan returns to roughly its original size minus Phase 4. Re-estimation is still open.

## Open
Resolved. The author registers in January 2027, which gives the autónomo path the earlier deadline and reorders the build (ADR-0014). ADR-0010's single-user premise holds, because he is registering himself.
