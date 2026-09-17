# 07: A new autónomo can see what he owes TGSS each month

**Issue:** #7 — https://github.com/mykola-blonskyi/gestoria/issues/7

**What to build:** Given the registration date and an expected annual net income, the monthly social security cuota for any month, with tarifa plana applied while it is in force and the income-banded cuota after it lapses.

This is the first number in this feature a person can act on, and it is the one that is due first: TGSS takes it monthly from January, regardless of whether any invoice has been paid.

**Blocked by:** 03 (#4), 05 (#5)

**Status:** ready-for-agent

- [ ] Given an alta date and an expected annual net income, return the cuota for a given month
- [ ] Tarifa plana applies for its configured window from the alta date, at the configured amount
- [ ] After tarifa plana lapses, the cuota comes from the tramo matching the expected net income
- [ ] A registration mid-month is handled the way TGSS handles it, and the choice is stated in the trace rather than assumed silently
- [ ] The result carries a `CalculationTrace` step naming the tramo selected and the income that selected it
- [ ] A warning is raised that TGSS trues up annually against real income, so this figure is provisional, per `SS_REGULARIZACION_AHEAD` in SPEC-010
- [ ] Worked example: alta 15 January 2027, expected net 30,000 → 80 € per month through the tarifa plana window, then the banded cuota
