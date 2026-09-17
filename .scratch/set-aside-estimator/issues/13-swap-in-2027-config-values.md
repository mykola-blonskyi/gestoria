# 13: The estimator answers for 2027, the year it is actually needed for

**Issue:** #12 — https://github.com/mykola-blonskyi/gestoria/issues/12

**What to build:** A `config/tax-years/2027.json` carrying the real 2027 parameters, so the estimate a person acts on in January 2027 is computed from 2027 law rather than from 2025 values used as scaffolding.

**This ticket cannot start until BOE publishes**, which is expected around December 2026. Everything before it is built against 2025 values so that the code is finished and tested before the numbers exist. This is the only item in the feature gated by something outside the project's control, and it sits on the critical path to M1.

**Blocked by:** 10 (#10). Also gated on BOE publishing the 2027 values, expected around December 2026

**Status:** ready-for-agent

- [ ] `config/tax-years/2027.json` exists and validates against the schema
- [ ] Social security tramos, tarifa plana, the Modelo 130 rate and minoración bands, the state scale and the Comunitat Valenciana regional scale all carry 2027 values with per-value provenance
- [ ] A diff table of every value that changed from 2025 accompanies the change, per the new-tax-year runbook in SPEC-007 §4
- [ ] 2025 goldens still pass against the 2025 configuration, per SPEC-011 §5
- [ ] The estimator produces an answer for 2027 that differs from the 2025 answer only where a parameter changed
- [ ] The runbook step of re-running simulator-backed goldens against the new year is followed, per ADR-0011
