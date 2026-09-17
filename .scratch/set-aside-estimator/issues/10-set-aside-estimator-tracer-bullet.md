# 10: The taxpayer knows how much of each payment is not his

**Issue:** #10 — https://github.com/mykola-blonskyi/gestoria/issues/10

**What to build:** The seam this whole feature is built around, plus the simplest complete path through it. A payment lands; what fraction of it is not yours? In January there is no history, so the projection alone answers.

This is the tracer bullet: one scenario, end to end, real numbers. Widening it to mid-year estimates, the marginal-rate case and the full warning set is #15.

**Blocked by:** 07 (#7), 08 (#8), 09 (#9)

**Status:** ready-for-agent

- [ ] A single pure entry point takes the profile, an activity picture, the year configuration and a quarter, and returns the estimate. No I/O, no database, no clock
- [ ] The input and output types are defined as the seam contract, with the activity picture carrying both actuals to date and a forward projection in one type
- [ ] The result gives the hold-back share, the next Modelo 130 amount with its due window, the monthly TGSS cuota, the annual true-up gap and the IVA figure
- [ ] The IVA figure is zero **with its reason in the warnings**, not omitted, per SPEC-003 §0
- [ ] `retención` year to date is zero and the trace says why, rather than leaving an unexplained zero
- [ ] The result carries a `CalculationTrace` and the `ConfigHash`
- [ ] One golden passes end to end: first month of activity, projection only, no actuals
- [ ] The golden fixture records `oracle`, `oracleRef` and the run date
