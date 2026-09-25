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
- [x] Record where each value came from. Landed as the `provenance` block keyed by JSON Pointer at the granularity of the verifiable unit (SPEC-007 §1.1), not as a per-value `{value, source, verified}` wrapper — that form roughly triples the file and buries changed numbers among changed dates. Every entry reads `kind: "theory"` today; sorting by `verified` ascending is the January queue
- [ ] Drop the Madrid `_todo` block (ADR-0013) and rename `2025.example.json` to `2025.json`
- [x] Fill `seguridadSocial.tramos` before January: the registration decision on contribution base and tarifa plana depends on it (ADR-0014). Filled with real 2025 BOE data (Orden PJC/178/2025), `tarifaPlana` confirmed against Ley 20/2007 art. 38 ter (#5)
- [x] Fill `modelo130.minoracion` (RD 439/2007 art. 110.3.c). First-activity-year case is unresolved — the article does not address a taxpayer with no `ejercicio anterior` (#5)
- [ ] Fill `modelo130.lines`, `modelo303.lines` and `modelo349` before the Q1 2027 forms
- [ ] Confirm ROI/VIES is on the Modelo 036 filed in January; without it, EU invoices carry Spanish IVA they should not
- [ ] Master key escrow and a rehearsed MinIO restore, before the first real document is uploaded (ADR-0015)
- [ ] Start `config/tax-years/2026.json` and `2027.json`. Both are release blockers; BOE publishes 2027 around December 2026
- [x] Write `config/tax-years/schema.json` (SPEC-007), validated in CI: schema plus cross-field rules in C# and mutation tests that prove both run (#4)
- [ ] Run the same validation at startup, from the typed loader (#14). Issue #4 covers CI only
- [x] Add projects: `src/GestorIA.Engine`, `tests/GestorIA.Engine.Tests`; register in `GestorIA.slnx` (#17). `src/GestorIA.Application` is not created yet and has no caller
- [x] `Directory.Build.props`: nullable, warnings-as-errors, banned `double`/`float` for money (ADR-0004). Two mechanisms, because the analyzer catches member access but not declarations: `BannedApiAnalyzers` plus the `NoBinaryFloatsInEngine` guard test (#17, fixed in #18 where `BannedSymbols.txt` had shipped empty)
- [ ] `services/ocr` skeleton: FastAPI `/health`, `pyproject.toml`, `uv.lock`, `ruff`, `pytest`
- [ ] CI: GitHub Actions runs `dotnet test` (#17). `pytest` waits for the `services/ocr` skeleton
- [x] Fill `.claude/CLAUDE.local.md`, `knowledge/*`
- [x] Repo under git; prototype deleted; `dotnet build` and `dotnet test` green (2026-09-18)

---

## Phase 1 — Core engine (starts after Phase 0 exit criteria)

- [ ] **First:** enter G1, G2, G3, G6, G7 into the AEAT simulator; record inputs, outputs and run date as fixtures (ADR-0011)
- [ ] Add G8b (salary + savings income reaching the same 6,500 € cap as G8) and enter it into the simulator
- [ ] Find published worked examples for G4, G5, G9 and G10; the simulator cannot express them (SPEC-011 §1)
- [x] `Money`, `Rate` value objects (SPEC-001, #13)
- [ ] `TaxYear`, `Region`, `Nif` value objects (SPEC-001)
- [x] `ScaleCalculator` + property tests (#6)
- [x] `MonthlyCuotaCalculator`: TGSS cuota with the tarifa plana window and the prorated month of alta (#7)
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
