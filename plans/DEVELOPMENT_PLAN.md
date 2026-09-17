# GestorIA — Development Plan

**Version:** 1.0 · **Date:** 2026-09-17 · **Owner:** solo developer (learning C# and Python; primary language TypeScript)

> **Re-scoped 2026-09-18 (ADR-0013, superseding ADR-0012).** v1.0 keeps the full employee and autónomo scope: Modelo 100, 130 and 303, Valencia only, figures typed in by hand. Only the OCR service (Phase 4) and Madrid move to v1.x.
>
> Build order follows deadlines (ADR-0014). The author registers as an autónomo in January 2027, so Modelo 130 and 303 for Q1 2027 fall due 20 April 2027 alongside Renta 2026. Order: set-aside estimator, then the quarterly forms, then the employee annual path, then the autónomo annual path, which waits for April 2028. The milestone table in section 6 predates all of this and has not been re-estimated.

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
| Personal financial data | Privacy-by-design from Phase 1 (SPEC-013). v1.0 runs on the author's own machine for one user (ADR-0010), so blob encryption at rest and PII-free logs stay; the rest of the control set is scoped when hosting is on the table |
| Regions | v1.0 = Valencia (`VC`) only. A fake region in a test fixture proves the region model is data-driven (ADR-0013) |
| Background work | In-process `Channel<T>` queue with Postgres as job-state source of truth (ADR-0009); no broker in v1 |

## 3. Architecture summary

See `docs/architecture.md`. Two deployable services plus a SPA:

- **`GestorIA.Api`** (C#) — hosts the engine, classifier, rule engine, persistence, REST API.
- **`gestoria-ocr`** (Python) — stateless document extraction: file in → structured JSON + confidence out.
- **`web`** (React/TS) — upload, review/confirm, results and explanations.

## 4. Current state (2026-09-17)

The repo carried an early prototype: `IrpfTaxCalculator` (hard-coded combined 19–47 % scale, no regional split, no mínimo, per-bracket rounding), `IrpfCalculationResult`, a `Transaction` model, a BBVA CSV parser and two test classes.

As of 2026-09-18 the calculator, its result type and its tests are deleted, and the repo is under git with a green `dotnet build` and `dotnet test`. `Transaction`, `IStatementParser`, `BbvaCsvStatementParser` and `BbvaParserTests` survive, because SPEC-004 §5 carries them into Phase 4. The plan below builds the config-driven engine of SPEC-002 on that base.

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

### 5.1 What v1.0 actually builds (ADR-0013)

| Phase | v1.0 status |
|---|---|
| 0 Foundation | In. One `_todo` block gone with Madrid; the other three return with the quarterly forms |
| 1 Core engine | In, in full. Employee path first (G1, G2, G6, G7, G8b, G10), then autónomo (G3, G4, G5, G8, G9) |
| 2 Credits and explanations | In, and it is the point (SPEC-006, SPEC-010) |
| 3 Application, persistence, API | Open. See `docs/decisions.md` |
| 4 OCR and ingestion | Out. v1.x (SPEC-005) |
| 5 Web application | Open. See `docs/decisions.md` |
| 6 Hardening and release | In, and smaller, because ADR-0010 removed the server |

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

Entry criteria
- Every golden the simulator can express is entered into AEAT *Renta WEB Open Simulador* and its output recorded as the expected value, before the matching calculator is written (ADR-0011). Run log in `reports/investigations/2025-renta-reconciliation.md`.

Exit criteria
- All 10 golden cases pass **to the cent**.
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
- `config/tax-years/2026.json` and `2027.json`. These are not proofs of the year-switch path; they are the production configs (ADR-0014).
- Deployment guide: local Docker Compose (`api`, `postgres`, `ocr`), encrypted backups, restore drill (ADR-0010).
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
| Wrong tax figures | Users under/overpay | Goldens derived from the AEAT simulator before the calculator is written (ADR-0011), explicit "not advice" UI, trace visible |
| Casilla numbers shift yearly | Wrong sheet | Mapping lives in `YYYY.json` (SPEC-008); yearly runbook |
| OCR quality on photos | Bad inputs → bad outputs | Confidence thresholds + mandatory human confirmation; no expense without an invoice |
| Learning two languages slows delivery | Schedule | Vertical slices, engine first (pure C#), Python surface minimal and isolated |
| PII leakage | Legal/trust | Largely removed for v1.0: one user, own machine, no server (ADR-0010). Encryption at rest, no PII in logs and LLM fallback off by default still apply |
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
