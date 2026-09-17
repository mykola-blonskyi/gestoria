# 06: Progressive tax scales compute correctly at every input, including the boundaries

**Issue:** #6 — https://github.com/mykola-blonskyi/gestoria/issues/6

**What to build:** The one piece of arithmetic that every IRPF figure in this project eventually passes through. Given a scale and a base, produce the tax, summing each tranche's rate over the portion of the base that falls inside it.

It is specified separately from any calculator because its correctness is provable by properties rather than by examples, and because a boundary error here is invisible in a golden case and wrong in every other.

**Blocked by:** 01 (#3)

**Status:** ready-for-agent

- [ ] `Cuota(scale, base)` sums `rate × clamp(base − lower, 0, upper − lower)` across tranches, per SPEC-002 §6
- [ ] An open final tranche, expressed as `upTo: null`, is handled
- [ ] No rounding happens inside the calculation, per ADR-0004
- [ ] Property test: the result is monotonic non-decreasing in the base
- [ ] Property test: the result is continuous at every tranche boundary, with no step
- [ ] Property test: `Cuota(scale, 0)` is zero for every scale
- [ ] Example tests reproduce the state and Comunitat Valenciana figures in golden G1: 2,990.25 and 2,887.50 on a base of 26,050
- [ ] Property tests are written with FsCheck, per `docs/CONVENTIONS.md`
