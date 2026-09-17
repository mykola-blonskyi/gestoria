# 04: Tax-year configuration loads into a typed model that names itself

**Issue:** #14 — https://github.com/mykola-blonskyi/gestoria/issues/14

**What to build:** The engine reads a validated tax-year file into an immutable typed model, and every result can name the exact configuration that produced it.

Split out of #4, which now covers only refusing files that should never be read. This ticket is about reading the ones that survive.

**Blocked by:** 02 (#13), 03 (#4)

**Status:** ready-for-agent

- [ ] Loading a year returns an immutable `TaxYearConfig` with money as `Money` and rates as `Rate`, not raw decimals
- [ ] The SHA-256 of the source file is exposed as `ConfigHash`, so a result can name the configuration behind it
- [ ] An unknown year or an unknown region raises `ConfigNotFoundException` and never falls back to a default, per SPEC-007 §3
- [ ] Per-value provenance from SPEC-007 §1.1 is representable, and a value sourced only from the theory document is distinguishable from one verified against the AEAT Manual
- [ ] Every value the estimator reads round-trips out of the 2025 configuration with its type intact
