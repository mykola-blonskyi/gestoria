# Local Project Instructions

## Project Context

GestorIA — a tax engine for Spanish residents (employees and autónomos under *estimación directa simplificada*). From bank statements, nóminas and invoices it calculates Modelo 130/303 per quarter and Modelo 100 per year, explains every figure step by step, and flags tax credits the user may have missed.

Start here, in this order:

1. `plans/DEVELOPMENT_PLAN.md` — phases, exit criteria, current phase (Phase 0 as of 2026-09-17)
2. `plans/current.md` — the active checklist; tick items as you complete them
3. `docs/architecture.md`, `docs/decisions.md` → `docs/adr/`
4. `docs/specs/SPEC-001…013` — implement against these; SPEC-011 defines the golden tests
5. `knowledge/business-rules.md` — 15 invariants the code must enforce
6. `docs/CONVENTIONS.md` — folder map, naming, money/date rules, definition of done

Tax theory (the *why* behind the rules) lives **outside** the repo in the Obsidian vault `~/Documents/obsidian-notes/gestor`. Specs cite it as `Theory §x.y`. Do not copy theory text into code or docs; a `// Theory §7.3` pointer is enough.

---

## Constraints

- Every monetary value is `decimal` (C#) / `Decimal` (Python). Never `double`/`float` for money (ADR-0004).
- No tax number in code. Scales, minimums, thresholds, rates, casilla numbers, credit rules come from `config/tax-years/YYYY.json` (ADR-0003, SPEC-007).
- The Engine (`src/GestorIA.Engine`) is pure: no I/O, no DB, no HTTP, no DateTime.Now. It receives profile + ledger + config and returns result + trace.
- No deductible expense without a linked, confirmed invoice; nothing unconfirmed enters a calculation (business rules 1–2).
- The 10 golden cases (SPEC-011) must pass to the cent before any engine change is merged.
- No real personal data in fixtures, logs or the repo (SPEC-013).
- Region set for v1: Valencia (`VC`) + Madrid (`MD`). Forms: 100, 130, 303 only.

---

## Architecture Notes

- Stack: C# / .NET 10 (`Domain → Engine → Application → Infrastructure → Api`), PostgreSQL 16 + EF Core, Python 3.12 / FastAPI + PaddleOCR in `services/ocr`, React 19 + TypeScript SPA in `web/` (Phase 5).
- API ↔ OCR: REST with a shared JSON Schema (`services/ocr/schema/extraction-result.schema.json`); DTOs on both sides are generated from it (ADR-0005).
- Test projects live in `tests/` (as in `GestorIA.slnx`), not in `src/`.
- The existing `IrpfTaxCalculator` / `Transaction` in `src/GestorIA.Domain` are a prototype. Keep them compiling until the new engine passes the goldens, then delete (SPEC-011 §4, SPEC-001 §7).
- Spanish tax terms stay as identifiers (`CuotaIntegra`, `RendimientoNeto`, `Retencion`); comments and docs in English.

---

## Coding Conventions

- C#: records for immutable data, `readonly record struct Money`, nullable enabled, warnings as errors, xUnit + FluentAssertions, FsCheck for property tests, Stryker.NET for mutation testing on the Engine.
- Python: `uv` for deps and lockfile, `ruff` for lint/format, `pytest`, Pydantic models, type hints everywhere, amounts serialised as strings with 2 decimals.
- Branch per spec (`feat/SPEC-004-transaction-classifier`); commit prefixes `feat(engine):`, `fix(ocr):`, `docs(adr):`.
- A change to `config/tax-years/*.json` requires a golden-test update or an explicit "no golden impact" note.
- When introducing a C# or Python idiom for the first time, add a one-line comment explaining it — the owner is learning both languages (primary language: TypeScript).

---

## Deployment Notes

- v1.0 is **hosted**: single VPS, Docker Compose (`api`, `postgres`, `ocr`, `caddy`), Caddy for TLS, OCR has no public port (ADR-0008). Encrypted blobs and encrypted backups are release blockers.
- Dev: the same `docker compose up` locally; OCR runs on CPU. A supported end-user local-only mode is v1.x (backlog), so keep `IFileStorage` and single-user auth mode intact.
- Background extraction jobs: in-process `Channel<T>` + `BackgroundService` behind `IExtractJobQueue`; job state in Postgres, re-enqueue `Pending` on startup (ADR-0009). No broker in v1.
- LLM structuring in OCR is a feature flag (`LLM_STRUCTURING=off|ollama|cloud`), off by default; when on, only masked raw text of one document is sent, never images.
- Secrets via env only; `.env.example` documents them.

---

## Known Limitations

- v1 does not support estimación objetiva (módulos), IS, IRNR, foral regimes (PV/NC), wealth tax, Modelo 720/721 filing, or e-filing to AEAT — it produces a casilla sheet for manual entry in Renta WEB.
- Criterio de caja is a flag with a warning; calculations still follow devengo.
- Loss carry-forward across years and the foreign-tax credit are computed/flagged but not fully modelled.
- Regional holidays are not in the calendar yet (national only).
- `config/tax-years/2025.example.json` still has `_todo` blocks (Madrid scale, SS tramos, 130/303 line numbers).
