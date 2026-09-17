# Architecture

## Overview

**v1.0 scope (ADR-0012): Modelo 100 for one employee in Valencia, figures typed in by hand.** The OCR service, the quarterly forms and the classifier below are the v1.x target architecture, kept here because the engine boundary is designed for them.

GestorIA turns raw personal financial documents (bank statements, nóminas, invoices, AEAT datos fiscales) into Spanish tax filings — Modelo 130/303 per quarter and Modelo 100 per year — with a step-by-step explanation of every figure and a list of tax credits the user may have missed. It is a two-service system plus a SPA. The **engine is pure**: given a profile, a ledger of confirmed documents and a year configuration, it deterministically produces a result and a trace. Everything else (parsing, OCR, storage, UI) exists to feed the engine correct inputs and show its outputs.

Decisions are recorded in `docs/adr/` (index: `docs/decisions.md`); module specifications in `docs/specs/`; roadmap in `plans/DEVELOPMENT_PLAN.md`.

---

## Goals

- Correct to the cent, reproducible, and explainable (trace from result → casilla → ledger row → source document).
- Nothing year-dependent hard-coded: all 📅 values live in `config/tax-years/YYYY.json`.
- Nothing enters a calculation unconfirmed; no deductible expense without a linked invoice.
- Zero-cost, self-hostable; a fully local deployment must be possible.

---

## System Components

```mermaid
flowchart LR
  subgraph Client
    SPA[React/TS SPA]
  end
  subgraph API["GestorIA.Api (C# / ASP.NET Core)"]
    APP[Application use cases]
    ENG[GestorIA.Engine<br/>pure calculation]
    RUL[Credit rule engine]
    CLS[Transaction classifier<br/>+ invoice matcher]
    PRS[Parsers: bank CSV/XLSX,<br/>invoice JSON, AEAT XML]
    INF[Infrastructure<br/>EF Core · blob storage · jobs]
  end
  subgraph OCR["gestoria-ocr (Python / FastAPI)"]
    PRE[Pre-processing<br/>OpenCV]
    PAD[PaddleOCR]
    EXT[Field extraction<br/>nómina · factura · statement]
    LLM[Optional LLM structuring<br/>feature-flag]
  end
  DB[(PostgreSQL)]
  FS[(Encrypted blob store)]
  CFG[/config/tax-years/YYYY.json/]

  SPA -->|REST /api/v1| APP
  APP --> ENG --> RUL
  APP --> CLS --> ENG
  APP --> PRS
  APP --> INF --> DB
  INF --> FS
  APP -->|POST /extract| PRE --> PAD --> EXT --> LLM
  CFG --> ENG
  CFG --> RUL
```

### Frontend — `web/` (React 19 + TypeScript, Vite)

Responsibilities: onboarding wizard, document upload, review queue (confirm/correct extracted fields and unclear transactions), quarterly and annual results, explanations, casilla sheet export. Contains **no tax math** — displays engine output only. SPEC-012.

Dependencies: generated OpenAPI client, TanStack Query, i18next (ES/EN/RU).

### Backend — `GestorIA.Api` (.NET 10, ASP.NET Core)

Layering (dependency arrows point inward only; the Engine never references EF Core, HTTP or the file system):

```
src/GestorIA.Domain          entities, value objects, enums                     (no dependencies)   SPEC-001
src/GestorIA.Engine          calculators, mappers, rule evaluator               (→ Domain)          SPEC-002/003/006/008
src/GestorIA.Application     use cases, DTOs, validators, ports (interfaces)    (→ Domain, Engine)
src/GestorIA.Infrastructure  EF Core, blob storage, OCR client, parsers, jobs   (→ Application)     SPEC-004/005
src/GestorIA.Api             endpoints, auth, OpenAPI, composition root         (→ Application, Infrastructure)  SPEC-009
tests/GestorIA.Engine.Tests  golden + property tests                            (→ Engine)          SPEC-011
tests/GestorIA.Api.Tests     integration tests (Testcontainers Postgres)        (→ Api)
```

Responsibilities: everything stateful and everything that computes. Dependencies: PostgreSQL 16 (EF Core), `gestoria-ocr` over REST, `config/tax-years`.

### Document extraction — `services/ocr` (Python 3.12, FastAPI)

Responsibilities: `POST /extract` — file in, structured fields with per-field confidence out. Stateless, keeps nothing. PaddleOCR + OpenCV pre-processing + per-document-type extractors; optional LLM structuring stage behind a flag. SPEC-005, ADR-0002.

Dependencies: none on the API; contract is `services/ocr/schema/extraction-result.schema.json` (ADR-0005).

---

## Integrations

External systems:

- AEAT Renta WEB Open Simulador — manual reconciliation benchmark for the engine (not an API).
- Bank statement exports (BBVA, Sabadell, Revolut, Wise) — file adapters, no bank APIs in v1.
- Optional LLM provider (Ollama local, or a free-tier cloud provider) for invoice structuring — off by default.

---

## Data Flow

Annual return:

1. **Ingest** — machine-readable files → C# parsers; PDFs/images → `gestoria-ocr`. Both yield `ExtractedDocument { type, fields[{name, value, confidence}], rawText }`.
2. **Review** — low-confidence fields and `UNCLEAR` transactions go to the review queue. Nothing enters the ledger unconfirmed.
3. **Ledger** — confirmed `Nomina`, `FacturaEmitida`, `FacturaRecibida`, `BankTransaction`, `Pago130/303`, each linked to its source document.
4. **Classify & match** — transactions get a class (Theory §15.2); client payments are matched to issued invoices and split into base / IVA / retención.
5. **Calculate** — `IrpfAnnualCalculator.Run(profile, ledger, yearConfig)` → `AnnualResult { casillas, trace, jointComparison, creditOutcomes, warnings, configHash }`.
6. **Explain** — trace + credit outcomes → localized messages; "possible credits" list missing documents.
7. **Export** — casilla sheet (PDF/CSV) for manual entry into Renta WEB.

Quarterly flow is identical up to step 4, then `Modelo130Calculator` / `Modelo303Calculator` with cumulative year-to-date inputs.

---

## Deployment

| Mode | Where | Status |
|---|---|---|
| Local-only | the author's machine, Docker Compose, no public port | **v1.0** (ADR-0010) |
| Single VPS | one host, Docker Compose, Caddy TLS | backlog — needs a second user first (ADR-0010 supersedes ADR-0008) |
| Split | API on VPS, OCR on a GPU box | later — requires the RabbitMQ `IExtractJobQueue` (ADR-0009) |

Containers: `api`, `ocr`, `postgres`, `caddy`. OCR has no public port. Background extraction runs inside `api` (in-process `Channel<T>`, job state in Postgres).

---

## Security

Authentication: v1.0 is single user with a hashed API key. OIDC (Authorization Code + PKCE) waits for the hosted milestone.

Authorization: every query scoped by user id; no cross-user access paths.

Secrets Management: env / KMS only; never in repo; pre-commit secret scan. Document blobs encrypted at rest (AES-256-GCM, per-user data key). Details: SPEC-013.

---

## Observability

Logging: Serilog structured logs with a PII scrubber (no NIF/IBAN/rawText/amounts-with-identifiers); correlation ids across API → OCR.

Metrics: OpenTelemetry — request latency, OCR latency and confidence distribution, calculation runs per year/region.

Tracing: OpenTelemetry traces across API and OCR calls; every calculation result stores `ConfigHash` for reproducibility.
