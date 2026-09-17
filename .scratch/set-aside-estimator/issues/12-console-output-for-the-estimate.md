# 12: The estimate can be read without opening a test runner

**Issue:** #11 — https://github.com/mykola-blonskyi/gestoria/issues/11

**What to build:** A console entry point that loads a profile and a projection from a file, runs the estimator, and prints the result and its trace as something legible.

This is deliberately the smallest possible surface. It is **not** the interface decision, which `docs/decisions.md` defers to M3 in March 2027, when loading a real quarter gives evidence about whether typing is the bottleneck. This ticket exists so that M1 has a demo that is not a green test.

**Blocked by:** 10 (#10)

**Status:** ready-for-agent

- [ ] A console entry point reads a profile and projection from a file outside the repository, per SPEC-013, and prints the estimate
- [ ] The trace prints as the readable step-by-step breakdown SPEC-002 §6 requires, not as raw JSON
- [ ] Warnings print where they cannot be missed, particularly the true-up gap and any lost `reducción por trabajo`
- [ ] Amounts print with two decimals and a euro sign, per SPEC-010 §5
- [ ] The `ConfigHash` and the tax year are printed, so an answer can be traced back to the configuration that produced it
- [ ] No tax figure is computed in this ticket; it displays engine output only
