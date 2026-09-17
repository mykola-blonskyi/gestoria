# Conventions

## Where things live

| Content | Location | Notes |
|---|---|---|
| Source code, technical docs, ADRs, specs, plans | `~/workspace/gestoria` (this repo) | Never store tax theory or study notes here |
| Golden test inputs and expected values | `tests/golden/YYYY/G0N.json` | Fixture data, not theory. Belongs in the repo, in full |
| Tax theory, examples, notes (RU/EN) | `~/Documents/obsidian-notes/gestor` | Single source of truth for domain knowledge; cite as *Theory §x.y* |
| Yearly tax parameters (scales, minimums, thresholds) | `config/tax-years/YYYY.json` | Data, not knowledge — belongs in the repo; every 📅-marked value from the theory doc goes here |

> The "no theory in the repo" rule covers prose, not numbers. A golden test whose inputs live outside version control is not a regression test, so every golden fixture states its own inputs in full. A `// Theory §5.3` pointer carries the derivation.

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
- An ADR is `Accepted` only once the evidence it rests on is in hand. For a technology choice that means the technology has been exercised: code written against it, a service called, a benchmark run. For a scope or direction choice it means the facts about the user, the deadline and the domain have been stated by the person who owns them. Reasoning that has met neither is `Proposed`. Writing nine Accepted ADRs in one day, before any of them has been exercised, records guesses as decisions and then forbids editing them.
- Separate what was **stated** from what was **concluded**. ADR-0012 was superseded within the hour because "the author is not an autónomo" (stated) became "the autónomo domain has no user" (concluded) with nothing marking the join. A fact about the present is not a scope boundary. The ADR template has a `Premises` section for this; fill it.
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
- **Every commit, branch, tag and release is authored solely by the repository owner.** No `Co-authored-by` trailers, no AI or tool attribution, no `--author` or `GIT_AUTHOR_*` overrides, no second identity. This is enforced, not just written down: `.githooks/commit-msg` rejects attribution trailers and any author that is not the configured `user.email`.
- Enable the hooks once per clone: `git config core.hooksPath .githooks`. Hook path is local config, so a fresh clone starts unenforced until you run it.
- A change to any `config/tax-years/*.json` must come with a golden-test update or an explicit "no golden impact" note in the PR.

## Definition of done (any feature)

1. Spec section referenced in the PR.
2. Unit tests; for engine changes the 10 golden cases (SPEC-011) still pass. A new or changed golden records its oracle, its inputs and its run date (ADR-0011).
3. Explanation output (SPEC-010) updated if a number the user sees changed.
4. No tax theory copied into code comments beyond a `// Theory §7.3` pointer.
5. `plans/current.md` checkbox ticked.
