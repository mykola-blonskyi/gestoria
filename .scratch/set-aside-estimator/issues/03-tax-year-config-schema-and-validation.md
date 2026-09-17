# 03: Tax-year configuration schema and validation

**Issue:** #4 — https://github.com/mykola-blonskyi/gestoria/issues/4

**What to build:** A tax-year configuration file can be trusted. A JSON Schema says what the file must look like, and validation runs in CI so a bad rate is caught by a machine rather than by a wrong tax return months later.

A wrong rate here is indistinguishable from a wrong answer, so this ticket's job is to refuse files rather than to be forgiving. Reading a surviving file into a typed model is #14.

**Blocked by:** 01 (#3)

**Status:** ready-for-agent

- [ ] A JSON Schema at `config/tax-years/schema.json` describes the structure in SPEC-007
- [ ] Validation runs in CI against every file in `config/tax-years/`
- [ ] Tranches that are not strictly increasing are rejected
- [ ] Rates outside [0,1] are rejected
- [ ] A missing or empty `sources` entry is rejected
- [ ] A year whose `regions` omit a region v1.0 needs is rejected
- [ ] Corrupting a rate to 1.5 in a copy of the config fails validation with a message naming the offending path
- [ ] The existing `2025.example.json` validates as-is, or every failure is recorded as a known gap rather than silently tolerated
