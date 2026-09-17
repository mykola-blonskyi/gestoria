# ADR-0003: Tax parameters are versioned JSON per year, never hard-coded

**Status:** Accepted · **Date:** 2026-09-17

## Context
Scales, minimums, thresholds, retención rates, IVA rates, autónomo cuota tramos, casilla numbers and credit rules change every year and per region (Theory marks these 📅). The current prototype (`IrpfTaxCalculator`) hard-codes a combined 19–47 % scale — every new year and every region would be a code change across the engine.

## Decision
All year-dependent values live in `config/tax-years/YYYY.json`, validated against a JSON Schema (SPEC-007), loaded into an immutable `TaxYearConfig` record. The engine receives the config as a parameter; it never reads files or dates itself. Regional data is nested under `regions.<code>` (ISO 3166-2:ES, e.g. `VC`, `MD`).

## Alternatives
- Hard-coded constants per year in C# classes (`TaxYear2025`): simple, but every change is a code deploy and regional variants explode.
- Database table of parameters: flexible, but harder to version/review in PRs and to reproduce a past calculation.

## Consequences
- "New tax year" is a runbook: copy last year's JSON, update values, update golden tests, tag.
- Credit rules (SPEC-006) are also data; adding a regional credit needs no C# change.
- Config is content-hashed; the hash is stored with every calculation result for reproducibility.
- The theory doc remains the human source; the JSON is the machine source — both cite AEAT/BOE references per value.
