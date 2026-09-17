# Conventions

## Where things live

| Content | Location | Notes |
|---|---|---|
| Source code, technical docs, ADRs, specs, plans | `~/workspace/gestoria` (this repo) | Never store tax theory, study notes or worked examples here |
| Tax theory, examples, notes (RU/EN) | `~/Documents/obsidian-notes/gestor` | Single source of truth for domain knowledge; cite as *Theory §x.y* |
| Yearly tax parameters (scales, minimums, thresholds) | `config/tax-years/YYYY.json` | Data, not knowledge — belongs in the repo; every 📅-marked value from the theory doc goes here |

> `Claude outputs/` currently holds copies of the theory documents. Per the rule above they belong in the vault; keep the folder out of git (`.gitignore`) or delete the copies.

## Folder map

| Folder | Purpose |
|---|---|
| `plans/` | `DEVELOPMENT_PLAN.md` (roadmap), `current.md` (active phase checklist), `backlog.md` |
| `docs/` | `architecture.md`, `decisions.md` (ADR index), `CONVENTIONS.md`, `TODO.md`, `onboarding.md`, `adr/`, `specs/` |
| `knowledge/` | Repo-level domain summary: `domain-model.md`, `business-rules.md`, `glossary.md` — pointers and invariants only, no theory text |
| `src/` | .NET projects; `GestorIA.slnx` sits in the repo root |
| `tests/` | .NET test projects (`GestorIA.*.Tests`) **and** cross-cutting fixtures: `golden/`, `fixtures/`, `ocr-contract/` |
| `services/ocr/` | Python OCR service (separate runtime, own Dockerfile) |
| `config/tax-years/` | Yearly tax parameters as JSON (SPEC-007) |
| `reports/` | Historical records: `investigations/` (AEAT-simulator reconciliation), `audits/` (OCR benchmark, security), `reviews/`, `summaries/` |
| `prompts/` | Team prompts; LLM structuring prompts for `services/ocr` go under `prompts/ocr/` |
| `graph/` | Architecture and dependency analysis outputs |
| `snippets/`, `examples/`, `boilerplates/` | Reusable templates |

## Naming

- Specs: `SPEC-NNN-kebab-title.md`, numbered once, never renumbered.
- ADRs: `ADR-NNNN-kebab-title.md`; status `Proposed → Accepted → Superseded by ADR-x`. Accepted ADRs are not edited.
- .NET projects: `GestorIA.<Layer>` (`Domain`, `Engine`, `Application`, `Infrastructure`, `Api`); tests `GestorIA.<Layer>.Tests` under `tests/`.
- Python service: package `gestoria_ocr`.
- Domain vocabulary keeps the **Spanish tax terms** as identifiers (`CuotaIntegra`, `BaseLiquidableGeneral`, `RendimientoNeto`, `Retencion`) — they map 1:1 to AEAT forms and the theory doc. Comments/docs are English.

## Money and dates

- All monetary values are `decimal` (C#) / `Decimal` (Python). `double`/`float` for money is a build error (Roslyn banned-API rule, Phase 0).
- Rounding: intermediate results are kept unrounded; AEAT rounding (2 dp, half away from zero) is applied only at the casilla boundary (SPEC-002 §5, ADR-0004).
- Dates are `DateOnly`; tax-year attribution follows *devengo* (accrual) unless the profile chose *criterio de caja* (SPEC-001 §6).

## Git

- `main` is always green (`dotnet test` + `pytest` pass).
- Branch per spec: `feat/SPEC-004-transaction-classifier`.
- Commit messages: `feat(engine): ...`, `fix(ocr): ...`, `docs(adr): ...`.
- A change to any `config/tax-years/*.json` must come with a golden-test update or an explicit "no golden impact" note in the PR.

## Definition of done (any feature)

1. Spec section referenced in the PR.
2. Unit tests; for engine changes the 10 golden cases (SPEC-011) still pass.
3. Explanation output (SPEC-010) updated if a number the user sees changed.
4. No tax theory copied into code comments beyond a `// Theory §7.3` pointer.
5. `plans/current.md` checkbox ticked.
