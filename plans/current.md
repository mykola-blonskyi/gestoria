# Current Plan

## Goal

Phase 0 — Foundation. Turn the scaffold into a config-driven, testable base for the tax engine.

**v1.0 is Modelo 100, 130 and 303 for employees and autónomos, Valencia only** (ADR-0013).

Build order follows deadlines (ADR-0014). The author registers as an autónomo in January 2027, so Modelo 130 and 303 for Q1 2027 fall due 20 April 2027, alongside Renta 2026. Quarterly path first, employee annual second, autónomo annual last because it waits for April 2028. Full roadmap: `plans/DEVELOPMENT_PLAN.md` §5.1.

---

## Phase 0 — Foundation (target: 2 weeks)

- [x] ADR-0001…0007 written (`docs/adr/`)
- [x] SPEC-001…013 drafted (`docs/specs/`)
- [x] `config/tax-years/2025.example.json` with scales, minimums, employment relief, activity params, casilla map
- [ ] Verify the values the first goldens touch against the AEAT Manual, not the vault: `escalaEstatal`, `regions.VC.escalaAutonomica`, `minimos.contribuyente`, `minimos.descendientes`, `minimos.menor3`, `trabajo.otrosGastos`, and the seven values in `trabajo.reduccion`
- [ ] Move `sources` from file level down to per value, so each scale cites the BOE article it came from
- [ ] Drop the Madrid `_todo` block (ADR-0013) and rename `2025.example.json` to `2025.json`
- [ ] Fill `seguridadSocial.tramos` before January: the registration decision on contribution base and tarifa plana depends on it (ADR-0014)
- [ ] Fill `modelo130.lines`, `modelo130.minoracion`, `modelo303.lines` and `modelo349` before the Q1 2027 forms
- [ ] Confirm ROI/VIES is on the Modelo 036 filed in January; without it, EU invoices carry Spanish IVA they should not
- [ ] Master key escrow and a rehearsed MinIO restore, before the first real document is uploaded (ADR-0015)
- [ ] Start `config/tax-years/2026.json` and `2027.json`. Both are release blockers; BOE publishes 2027 around December 2026
- [ ] Write `config/tax-years/schema.json` (SPEC-007) and a startup validator
- [ ] Add projects: `src/GestorIA.Engine`, `src/GestorIA.Application`, `tests/GestorIA.Engine.Tests`; register in `GestorIA.slnx`
- [ ] `Directory.Build.props`: nullable, warnings-as-errors, banned `double`/`float` for money (ADR-0004)
- [ ] `services/ocr` skeleton: FastAPI `/health`, `pyproject.toml`, `uv.lock`, `ruff`, `pytest`
- [ ] CI: GitHub Actions running `dotnet test` + `pytest`
- [x] Fill `.claude/CLAUDE.local.md`, `knowledge/*`
- [x] Repo under git; prototype deleted; `dotnet build` and `dotnet test` green (2026-09-18)

---

## Phase 1 — Core engine (starts after Phase 0 exit criteria)

- [ ] **First:** enter G1, G2, G3, G6, G7 into the AEAT simulator; record inputs, outputs and run date as fixtures (ADR-0011)
- [ ] Add G8b (salary + savings income reaching the same 6,500 € cap as G8) and enter it into the simulator
- [ ] Find published worked examples for G4, G5, G9 and G10; the simulator cannot express them (SPEC-011 §1)
- [ ] `Money`, `Rate`, `TaxYear`, `Region`, `Nif` value objects (SPEC-001)
- [ ] `ScaleCalculator` + property tests
- [ ] `EmploymentIncomeCalculator` → golden #1, #2, #8
- [ ] `MinimoCalculator` → golden #7
- [ ] `ActivityIncomeCalculator` → golden #3, #4
- [ ] `Modelo130Calculator` → golden #3, #5; `Modelo303Calculator`; `Modelo349Calculator`
- [ ] `SavingsIncomeCalculator` → golden #6
- [ ] `IrpfAnnualCalculator` + `CalculationTrace`
- [ ] `Modelo100Mapper` (SPEC-008); `FilingObligationChecker` → golden #10
- [ ] Reconciliation log vs AEAT simulator → `reports/investigations/2025-renta-reconciliation.md`

---

## Risks

- Estimates assume ~15–20 h/week while learning C#; re-plan after Phase 1 exit.
- `2025.json` values must be re-verified against the AEAT Manual before goldens are trusted.
- The existing `Transaction` model (signed amount ⇒ income/expense) conflicts with SPEC-001 ledger design; migrate rather than extend. It survives only as the parser's output type.
- The first production config is `2026.json`, which does not exist yet and cannot until BOE publishes the 2026 values. `2025.json` is the test corpus.
- ~28 weeks to 20 April 2027, when Modelo 130, Modelo 303 and Renta 2026 all come due, against a 31-week plan that only Phase 4 has been cut from. Re-estimate before trusting any milestone date.
- Whether Modelo 130 is required at all is unknown until the client mix is settled (SPEC-003 §1). If ≥ 70 % of activity income carries retención, it is not required, and one of the three April filings disappears.
