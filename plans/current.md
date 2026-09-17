# Current Plan

## Goal

Phase 0 — Foundation. Turn the scaffold + prototype into a config-driven, testable base for the tax engine. Full roadmap: `plans/DEVELOPMENT_PLAN.md`.

---

## Phase 0 — Foundation (target: 2 weeks)

- [x] ADR-0001…0007 written (`docs/adr/`)
- [x] SPEC-001…013 drafted (`docs/specs/`)
- [x] `config/tax-years/2025.example.json` with scales, minimums, employment relief, activity params, casilla map
- [ ] Complete `2025.json` (Madrid scale, SS tramos, 130/303 line numbers, holidays) and rename from `.example`
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
- [ ] `Money`, `Rate`, `TaxYear`, `Region`, `Nif` value objects (SPEC-001)
- [ ] `ScaleCalculator` + property tests
- [ ] `EmploymentIncomeCalculator` → golden #1, #2, #8
- [ ] `ActivityIncomeCalculator` → golden #3, #4
- [ ] `MinimoCalculator` → golden #7
- [ ] `SavingsIncomeCalculator` → golden #6
- [ ] `IrpfAnnualCalculator` + `CalculationTrace`
- [ ] `Modelo130Calculator` → golden #3, #5; `Modelo303Calculator`
- [ ] `Modelo100Mapper` (SPEC-008); `FilingObligationChecker` → golden #10
- [ ] Reconciliation log vs AEAT simulator → `reports/investigations/2025-renta-reconciliation.md`

---

## Risks

- Estimates assume ~15–20 h/week while learning C#; re-plan after Phase 1 exit.
- `2025.json` values must be re-verified against the AEAT Manual before goldens are trusted.
- The existing `Transaction` model (signed amount ⇒ income/expense) conflicts with SPEC-001 ledger design; migrate rather than extend. It survives only as the parser's output type.
- `2025.example.json` has four `_todo` holes (Madrid scale, SS tramos, 130 and 303 line numbers, minoración bands). Filling them is research against BOE, not coding, and Phase 0 currently gates on it.
