# GestorIA — Development Plan

**Version:** 1.0 · **Date:** 2026-09-17 · **Owner:** solo developer (learning C# and Python; primary language TypeScript)

## 1. Goal and scope

Build a full tax engine for Spanish residents (employees and *autónomos* under *estimación directa simplificada*) that:

1. ingests bank statements, nóminas, issued/received invoices, AEAT *datos fiscales* and registration data (Theory §15.1);
2. classifies transactions and links them to supporting documents (Theory §15.2);
3. calculates Modelo 130 / 303 per quarter and Modelo 100 per year, including regional scales and credits, joint vs individual comparison (Theory §15.4);
4. explains every figure in plain language and lists possibly-missed credits and missing documents (Theory §15.5);
5. exposes all of this through a web application.

Out of scope for v1: *estimación objetiva* (módulos), corporate tax (IS), non-residents (IRNR), Basque/Navarre foral regimes, wealth tax, Modelo 720/721 filing (only reminders), actual e-filing to AEAT (we produce a casilla-by-casilla sheet, the user files in Renta WEB).

## 2. Guiding constraints

| Constraint | Consequence |
|---|---|
| Correctness is the product | Golden tests (SPEC-011) gate every merge; engine is pure and deterministic |
| Yearly legal changes | Nothing 📅 is hard-coded — `config/tax-years/YYYY.json` (ADR-0003) |
| Solo dev, two new languages | Thin vertical slices; each phase ends with something runnable; Python surface kept small |
| Zero-cost infrastructure for now | Self-hostable stack, free-tier cloud LLM as optional fallback only |
| Personal financial data | Privacy-by-design from Phase 1 (SPEC-013); hosted on a single VPS in v1 (ADR-0008), so encryption at rest, encrypted backups and GDPR export/delete are release blockers |
| Regions | v1 = Valencia (`VC`) + Madrid (`MD`); Madrid proves the region model is data-driven |
| Background work | In-process `Channel<T>` queue with Postgres as job-state source of truth (ADR-0009); no broker in v1 |

## 3. Architecture summary

See `docs/architecture.md`. Two deployable services plus a SPA:

- **`GestorIA.Api`** (C#) — hosts the engine, classifier, rule engine, persistence, REST API.
- **`gestoria-ocr`** (Python) — stateless document extraction: file in → structured JSON + confidence out.
- **`web`** (React/TS) — upload, review/confirm, results and explanations.

## 4. Current state (2026-09-17)

The repo contains an early prototype: `src/GestorIA.Domain/Services/IrpfTaxCalculator.cs` (hard-coded combined scale 19–47 %, no regional split, no mínimo, per-bracket rounding), `Transaction` model, a BBVA CSV parser and two test classes. Phase 0/1 below replaces this prototype with the config-driven engine described in SPEC-002; keep it until the golden tests pass on the new engine, then delete.

## 5. Phases

Each phase lists deliverables, the specs it implements, and **exit criteria**. Estimates are for a solo developer at ~15–20 h/week and are deliberately conservative because two languages are being learned at the same time.

### Phase 0 — Foundation (≈ 1–2 weeks)

Deliverables
- ADR-0001…0007, all SPECs in draft (done — this commit).
- Add `GestorIA.Engine` and `GestorIA.Application` projects to `GestorIA.slnx`; `tests/GestorIA.Engine.Tests`; `Directory.Build.props` with nullable, warnings-as-errors, banned `double`/`float` for money.
- Python service skeleton (`services/ocr`: FastAPI `GET /health`, `pyproject.toml`, `ruff`, `pytest`).
- `config/tax-years/2025.json` completed from Theory §4–§7 (rename from `2025.example.json`).
- CI (GitHub Actions): `dotnet build/test`, `pytest`, `npm test`.

Exit criteria
- `dotnet test` and `pytest` run green in CI.
- `2025.json` validates against the SPEC-007 JSON Schema.
- Learning checkpoint (C#): records, `decimal`, LINQ, DI, xUnit — enough to read SPEC-002 fluently.

### Phase 1 — Core tax engine (≈ 4–6 weeks) · SPEC-001, SPEC-002, SPEC-007, SPEC-011

The pure calculation library. No I/O, no database, no HTTP.

Deliverables
- `GestorIA.Domain`: value objects (`Money`, `Rate`, `TaxYear`, `Region`, `Nif`), entities (`TaxpayerProfile`, `FamilyMember`, `Nomina`, `FacturaEmitida`, `FacturaRecibida`, `BankTransaction`, `Pago130`).
- `GestorIA.Engine`:
  - `ScaleCalculator.Cuota(scale, base)` — layered progressive scale.
  - `EmploymentIncomeCalculator` (rendimiento neto, reducción por trabajo incl. the 6,500 € "other income" guard — golden #8).
  - `ActivityIncomeCalculator` (devengo attribution, deductible expenses with proportions/depreciation, difícil justificación cap — golden #4).
  - `SavingsIncomeCalculator` (capital mobiliario, FIFO gains/losses, offsetting rules).
  - `PropertyIncomeCalculator` (rental income with reducción, imputación de rentas).
  - `MinimoCalculator` (personal/familiar, split rules, 31-Dec snapshot — golden #7).
  - `IrpfAnnualCalculator` orchestrating steps 1–9 of Theory §15.4, producing a `CalculationTrace`.
  - `JointReturnComparer` (conjunta vs individual).
  - `Modelo130Calculator`, `Modelo303Calculator` (cumulative logic, carry-over — golden #5).
  - `Modelo100Mapper` (aggregate → casilla numbers, SPEC-008).
  - Filing-obligation checker (golden case #10).
- `tests/GestorIA.Engine.Tests`: the 10 golden cases + property-based tests for `ScaleCalculator` + rounding tests.
- Delete the prototype `IrpfTaxCalculator` once goldens pass.

Exit criteria
- All 10 golden cases pass **to the cent**.
- Three additional profiles reconciled manually against AEAT *Renta WEB Open Simulador*; deviations documented in `reports/investigations/2025-renta-reconciliation.md`.
- Mutation testing (Stryker.NET) score ≥ 80 % on `GestorIA.Engine`.
- `CalculationTrace` can be pretty-printed into the step-by-step form used in Theory §5.3 / §7.2.

### Phase 2 — Rules: deductions and explanations (≈ 3–4 weeks) · SPEC-006, SPEC-010

Deliverables
- Data-driven credit rule engine: rules as JSON (`config/tax-years/2025.json → deducciones[]`) with a small condition DSL. Evaluator returns `Applied | NotApplied(reason) | Possible(missingDocuments[])`.
- National credits (Theory §11.1) and Valencia regional credits (§11.2) encoded; Madrid added to prove the model generalises.
- Explanation generator: templated plain-language messages keyed by trace step and rule outcome (Theory §15.5), i18n-ready (ES/EN/RU).
- "Money to set aside" estimator for autónomos.

Exit criteria
- Rule outcomes unit-tested per rule.
- Adding a new regional credit requires **zero C# changes** (JSON only) — verified by a test.
- Explanation output reviewed against Theory §15.5 checklist.

### Phase 3 — Application layer, persistence, API (≈ 3–4 weeks) · SPEC-001, SPEC-009, SPEC-013

Deliverables
- `GestorIA.Application`: use cases (`CreateProfile`, `ImportDocument`, `ConfirmTransaction`, `RunQuarter`, `RunAnnual`), DTOs, validation (FluentValidation).
- `GestorIA.Infrastructure`: EF Core + PostgreSQL 16, migrations, repositories, file storage abstraction (local disk / S3-compatible), encryption at rest for document blobs.
- `GestorIA.Api`: ASP.NET Core minimal API, OpenAPI, versioning (`/api/v1`), auth (single-user local mode first; OIDC-ready), structured logging (Serilog), health checks.
- Docker image for the API; `docker-compose.yml` with PostgreSQL.

Exit criteria
- End-to-end integration test: create profile → post nóminas as JSON → run annual → response equals golden #1.
- OpenAPI document generates a TypeScript client used by the SPA in Phase 5.
- No PII in logs (automated check).

### Phase 4 — Document ingestion and OCR service (≈ 5–7 weeks) · SPEC-004, SPEC-005

Deliverables
- Parsers (C#) for machine-readable inputs: bank CSV/XLSX (BBVA — existing parser to be adapted —, Sabadell, Revolut, Wise), invoice JSON, AEAT datos fiscales XML/PDF text layer.
- Transaction classifier (C#, rule-based first pass per Theory §15.2) producing `Classified | NeedsInvoice | NeedsUserInput`.
- Invoice ↔ transaction matcher (base / IVA / retención reconstruction — golden #9).
- **`gestoria-ocr`** (Python): `POST /extract` accepting PDF/JPG/PNG; pipeline = pre-processing → PaddleOCR → layout heuristics → field extraction for `nomina`, `factura`, `bank_statement_pdf` → JSON with per-field confidence. Optional LLM structuring behind a feature flag with strict JSON-schema validation.
- C# `IDocumentExtractor` client with retry/circuit-breaker (Polly); `IExtractJobQueue` over an in-process bounded `Channel<T>` + `BackgroundService`, Postgres job state with re-enqueue on startup (ADR-0009).
- Human-in-the-loop review: every extracted field carries confidence; below threshold → user confirms.

Exit criteria
- Extraction benchmark on `tests/fixtures/documents/` (≥ 30 anonymised samples): field accuracy ≥ 95 % on text-layer PDFs, ≥ 85 % on phone photos; report stored in `reports/audits/`.
- Classifier precision ≥ 98 % on SOCIAL_SECURITY, AEAT_PAYMENT, OWN_TRANSFER.
- Full pipeline demo: 12 nóminas + 20 invoices + bank CSV → confirmed data → annual result with explanations.

### Phase 5 — Web application (≈ 4–5 weeks) · SPEC-012

Deliverables
- React + TypeScript + Vite SPA: onboarding wizard, document upload, review queue, quarterly dashboard, annual result with trace, credits panel, casilla sheet export (PDF/CSV), calendar, i18n (ES/EN/RU).

Exit criteria
- A new user can go from zero to an annual result in one session without reading docs (2 external testers).
- Lighthouse performance/accessibility ≥ 90.

### Phase 6 — Hardening and release (≈ 2–3 weeks)

Deliverables
- Security review checklist (SPEC-013), dependency scanning, backups, data export/delete (GDPR).
- `config/tax-years/2026.json` to prove the year-switch path.
- Deployment guide: single VPS with Docker Compose (`api`, `postgres`, `ocr`, `caddy`), encrypted backups, restore drill (ADR-0008).
- Observability: OpenTelemetry metrics, error tracking.

Exit criteria
- Runbook for "new tax year" completed and rehearsed.
- Release `v1.0.0` tagged; changelog.

### Later (v1.x backlog) — see `plans/backlog.md`

## 6. Milestone table

| # | Milestone | Target | Proof |
|---|---|---|---|
| M0 | Skeleton + CI green | Week 2 | CI badge |
| M1 | Engine passes 10 golden cases | Week 8 | `dotnet test` output |
| M2 | Credits engine + explanations | Week 12 | JSON-only rule added in test |
| M3 | API end-to-end golden #1 | Week 16 | Integration test |
| M4 | OCR + classifier benchmark met | Week 23 | `bench` report |
| M5 | SPA usable end-to-end | Week 28 | User test notes |
| M6 | v1.0.0 | Week 31 | Tag |

## 7. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Wrong tax figures | Users under/overpay | Golden tests, AEAT simulator reconciliation, explicit "not advice" UI, trace visible |
| Casilla numbers shift yearly | Wrong sheet | Mapping lives in `YYYY.json` (SPEC-008); yearly runbook |
| OCR quality on photos | Bad inputs → bad outputs | Confidence thresholds + mandatory human confirmation; no expense without an invoice |
| Learning two languages slows delivery | Schedule | Vertical slices, engine first (pure C#), Python surface minimal and isolated |
| PII leakage | Legal/trust | SPEC-013; hosted data is the owner's liability — encryption at rest, encrypted backups, no PII in logs, LLM fallback off by default |
| Scope creep | Never ships | v1 = Valencia + Madrid, forms 100/130/303 only |

## 8. Learning track (parallel to phases)

| Phase | C# topics | Python topics |
|---|---|---|
| 0–1 | records, `decimal`, LINQ, pattern matching, xUnit, DI, immutability | venv/pyproject, typing, pytest |
| 2 | JSON (`System.Text.Json`), expression evaluation, generics | — |
| 3 | ASP.NET minimal APIs, EF Core, async/await, middleware | — |
| 4 | `HttpClient`, Polly, channels/background services | FastAPI, Pydantic, PaddleOCR, OpenCV basics, Decimal |
| 5 | — | — (TypeScript) |
| 6 | OpenTelemetry, Docker multi-stage builds | packaging, Docker |
