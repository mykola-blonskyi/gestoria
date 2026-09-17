# 11: The estimator handles every scenario, not just the simple one

**Issue:** #15 — https://github.com/mykola-blonskyi/gestoria/issues/15

**What to build:** Widen the tracer bullet from #10 to the cases that actually happen: an estimate mid-year that uses real invoices as well as a projection, a taxpayer whose employment income lifts the marginal rate above the Modelo 130 rate, and the full set of warnings.

#10 proves the path works. This ticket makes it trustworthy.

**Blocked by:** 10 (#10)

**Status:** ready-for-agent

- [ ] A mid-year estimate uses both actuals to date and the forward projection, and the trace shows which contributed what
- [ ] A taxpayer whose activity income stacks on employment income gets a non-zero `AnnualTrueUpGap` with the month it falls due
- [ ] Crossing the other-income cap reports the `ReduccionTrabajo` as lost **and** the amount lost, per business rule 6 and golden G8
- [ ] `tarifa plana` in force and the month after it lapses are both covered
- [ ] A loss-making quarter carried forward is reflected in the estimate
- [ ] Running the same input twice produces an identical result and an identical trace, per SPEC-002 §8
- [ ] Warnings use the existing SPEC-010 §3 catalogue keys; no new keys are invented
- [ ] The estimate errs toward over-reserving where a choice exists, and says so in the trace
- [ ] Every golden records `oracle`, `oracleRef` and the run date
