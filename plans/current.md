# Current Plan

## Goal

Phase 0 — Foundation. Turn the scaffold into a config-driven, testable base for the tax engine.

**v1.0 is Modelo 100 for one employee in Valencia** (ADR-0012), due for Renta 2026 between April and June 2027. Full roadmap: `plans/DEVELOPMENT_PLAN.md` §5.1.

---

## Phase 0 — Foundation (target: 2 weeks)

- [x] ADR-0001…0007 written (`docs/adr/`)
- [x] SPEC-001…013 drafted (`docs/specs/`)
- [x] `config/tax-years/2025.example.json` with scales, minimums, employment relief, activity params, casilla map
- [ ] Verify the values the first goldens touch against the AEAT Manual, not the vault: `escalaEstatal`, `regions.VC.escalaAutonomica`, `minimos.contribuyente`, `minimos.descendientes`, `minimos.menor3`, `trabajo.otrosGastos`, and the seven values in `trabajo.reduccion`
- [ ] Move `sources` from file level down to per value, so each scale cites the BOE article it came from
- [ ] Drop the `_todo` blocks made moot by ADR-0012 (Madrid, SS tramos, 130 and 303 lines) and rename `2025.example.json` to `2025.json`
- [ ] Write `config/tax-years/schema.json` (SPEC-007) and a startup validator
- [ ] Add projects: `src/GestorIA.Engine`, `src/GestorIA.Application`, `tests/GestorIA.Engine.Tests`; register in `GestorIA.slnx`
- [ ] `Directory.Build.props`: nullable, warnings-as-errors, banned `double`/`float` for money (ADR-0004)
- [ ] `services/ocr` skeleton: FastAPI `/health`, `pyproject.toml`, `uv.lock`, `ruff`, `pytest`
- [ ] CI: GitHub Actions running `dotnet test` + `pytest`
- [x] Fill `.claude/CLAUDE.local.md`, `knowledge/*`
- [x] Repo under git; prototype deleted; `dotnet build` and `dotnet test` green (2026-09-18)

---

## Phase 1 — Core engine (starts after Phase 0 exit criteria)

- [ ] **First:** enter G1, G2, G6, G7 into the AEAT simulator; record inputs, outputs and run date as fixtures (ADR-0011)
- [ ] Restate G8 with savings income in place of autónomo income (ADR-0012), then enter it into the simulator
- [ ] `Money`, `Rate`, `TaxYear`, `Region`, `Nif` value objects (SPEC-001)
- [ ] `ScaleCalculator` + property tests
- [ ] `EmploymentIncomeCalculator` → golden #1, #2, #8
- [ ] `MinimoCalculator` → golden #7
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
- 30 weeks to the Renta 2026 window against a 31-week plan written before the ADR-0012 cuts. Re-estimate before trusting any milestone date.
