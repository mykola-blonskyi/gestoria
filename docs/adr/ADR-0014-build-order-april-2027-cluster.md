# ADR-0014: Build order follows the April 2027 deadline cluster

**Status:** Accepted · **Date:** 2026-09-18 · **Amends:** ADR-0013 (sequencing only; its scope decision stands)

## Context
ADR-0013 sequenced the work behind a single date. Renta 2026, due April to June 2027, was the only filing the author had to make, so the employee Modelo 100 path came first.

The author now plans to register as an autónomo in **January 2027**. Scope does not change. What changes is the calendar, and it turns one deadline into three that land in the same three weeks:

| Filing | Period | Due | Borrador? |
|---|---|---|---|
| Modelo 130 Q1 2027 | Jan–Mar 2027 | 1–20 April 2027, if required | No |
| Modelo 303 Q1 2027 | Jan–Mar 2027 | 1–20 April 2027 | No |
| Renta 2026, employee | 2026 | 2 April – 30 June 2027 | Yes |

ADR-0013 put the employee path first because it was the only dated filing. It is no longer the only one, and it is the only one of the three that AEAT already pre-fills. The quarterly forms have no borrador at all. A first-quarter autónomo produces them from nothing, in his first quarter, which is exactly where mistakes happen and exactly where this tool is worth most.

## Premises

| Premise | Stated or concluded | Source |
|---|---|---|
| The author registers as an autónomo in January 2027 | Stated | Author, 2026-09-18 |
| The author files Renta 2026 as an employee | Stated | Author, 2026-09-18 (Q6) |
| Three filings fall due within three weeks of April 2027 | Concluded, from the two above plus `config.calendar` | This ADR |
| Modelo 130 Q1 2027 is actually required | **Unknown.** Depends on client mix, SPEC-003 §1 | Open |
| The author is still the only user | Concluded. He is registering himself, so ADR-0010 holds | This ADR |

## Decision
Build the quarterly path first, in this order:

1. **The set-aside estimator** (SPEC-010 `SET_ASIDE_ESTIMATE`). Needed from January, needs almost no engine, and it is what stops a new autónomo spending money that belongs to Hacienda.
2. **Modelo 130 and Modelo 303 for Q1 2027**, due 20 April 2027.
3. **Renta 2026, employee path**, due 30 June 2027.
4. **The autónomo annual path**, which is not needed until Renta 2027 in April 2028.

Item 4 is the part worth noticing. The autónomo half of an annual return has a full year of slack, so G3's Modelo 100 figure of 9,548.00 is not urgent even though G3's Σ130 of 8,480.00 is due in April 2027. `ActivityIncomeCalculator` is still early work, because Modelo 130 needs activity income year to date.

## Alternatives
- **Keep ADR-0013's employee-first order.** It optimises for the filing AEAT already does for you, and leaves the two filings with no safety net until last.
- **Build everything in parallel.** One developer, three deadlines, one window. This is how all three arrive late.

## Consequences
- **Two configs are release blockers and neither exists.** `2026.json` for Renta 2026, and `2027.json` for the Q1 2027 quarterly forms. BOE publishes the 2027 values around December 2026, which is after the January registration and before the April deadlines. `plans/DEVELOPMENT_PLAN.md` Phase 6 lists `2026.json` as a proof of the year-switch path. It was never a proof. It is the production config, and now there are two.
- **`seguridadSocial.tramos` stops being a deferred `_todo`.** An autónomo picks a contribution base at registration from estimated net income, and tarifa plana is an 80 €/month decision taken in January. That is January work.
- **`retencionNuevo` has to be right from invoice number one.** 7 % for the first three years if the activity is Profesional, 15 % otherwise. Wrong retención on early invoices is expensive to unwind.
- Whether Modelo 130 is required at all is open. SPEC-003 §1 exempts an autónomo whose income subject to retención exceeds 70 %, which depends entirely on where the clients are.
- ADR-0010 survives. One user, own machine.
