# 08: A quarter's Modelo 130 payment can be projected before the quarter ends

**Issue:** #8 — https://github.com/mykola-blonskyi/gestoria/issues/8

**What to build:** The cumulative year-to-date Modelo 130 calculation, run against either real figures or a projection, so the amount due on 20 April is known in January rather than discovered in April.

Modelo 130 is cumulative from 1 January and a loss-making quarter pays nothing and carries forward, so a naive per-quarter calculation is wrong in exactly the quarters that matter most.

**Blocked by:** 03 (#4), 05 (#5)

**Status:** ready-for-agent

- [ ] Year-to-date income minus year-to-date expenses gives the base, floored at zero
- [ ] The payment is the configured rate on that base, less retenciones year to date, less Modelo 130 already paid this year, less the minoración band
- [ ] A negative result pays zero and carries forward to the next quarter of the same year, per business rule 14
- [ ] Difícil justificación is applied only when `config.modelo130.applyDj` says so
- [ ] Retención year to date is zero for this taxpayer's profile, because EU and US payers do not withhold, and the trace says so rather than leaving a silent zero, per SPEC-003 §0
- [ ] Golden G5 passes: a loss-making Q3 pays zero and the carry-over lands in Q4
- [ ] Golden G3's quarterly half passes: four quarters over 42,400 net sum to 8,480.00
- [ ] Golden fixtures record `oracle`, `oracleRef` and the run date, per SPEC-011 §1, and the fixture format established here is what later goldens follow
