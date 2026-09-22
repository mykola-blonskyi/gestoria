# Architecture

## Overview

**v1.0 scope (ADR-0013): Modelo 100, 130 and 303 for employees and autónomos, Valencia only, figures typed in by hand.** The OCR service below is deferred to v1.x; everything else is v1.0.

GestorIA turns raw personal financial documents (bank statements, nóminas, invoices, AEAT datos fiscales) into Spanish tax filings — Modelo 130/303 per quarter and Modelo 100 per year — with a step-by-step explanation of every figure and a list of tax credits the user may have missed. It is a two-service system plus a SPA. The **engine is pure**: given a profile, a ledger of confirmed documents and a year configuration, it deterministically produces a result and a trace. Everything else (parsing, OCR, storage, UI) exists to feed the engine correct inputs and show its outputs.

Decisions are recorded in `docs/adr/` (index: `docs/decisions.md`); module specifications in `docs/specs/`; roadmap in `plans/DEVELOPMENT_PLAN.md`.

---

## Goals

- Correct to the cent, reproducible, and explainable (trace from result → casilla → ledger row → source document).
- Nothing year-dependent hard-coded: all 📅 values live in `config/tax-years/YYYY.json`.
- Nothing enters a calculation unconfirmed; no deductible expense without a linked invoice.
- Zero-cost, self-hostable; a fully local deployment must be possible.

---

## System Context

```mermaid
flowchart LR
  U(["<b>Taxpayer — the author</b><br/>single user · employee<br/>+ autónomo from Jan 2027 · Valencia"])

  GI["<b>GestorIA</b><br/>computes Modelo 100 / 130 / 303 / 349,<br/>explains every figure,<br/>lists credits possibly missed"]

  AEAT["<b>AEAT</b> — Renta WEB + Open Simulador<br/><i>no API: manual entry;<br/>the simulator is the correctness oracle</i>"]
  GESTOR["<b>Gestor</b> (human)<br/><i>prepares Q1 2027 in parallel;<br/>the diff becomes goldens</i>"]
  BANKS["<b>Banks</b> — BBVA · Sabadell · Revolut · Wise<br/><i>CSV / XLSX exports, no bank APIs in v1</i>"]
  TGSS["<b>TGSS</b> — Seguridad Social<br/><i>cuota de autónomos;<br/>context only, not a filing</i>"]
  BOE["<b>BOE / AEAT Manual</b><br/><i>tax parameters, transcribed<br/>by hand once a year</i>"]
  MINIO["<b>MinIO</b> on the author's VPS<br/><i>document blobs</i>"]
  LLM["<b>LLM</b> — Ollama or free tier<br/><i>invoice structuring</i>"]

  U -->|"types figures, uploads documents,<br/>confirms the review queue"| GI
  GI -->|"casilla sheet PDF/CSV, explanations,<br/>deadlines, set-aside estimate"| U
  U -->|"files by hand"| AEAT
  AEAT -.->|"golden values (ADR-0011)"| GI
  GESTOR -.->|"reference 130 / 303 / 349, Q1 2027"| GI
  BANKS -->|"statement files"| GI
  TGSS -.->|"tramos, tarifa plana → config"| GI
  BOE -.->|"config/tax-years/YYYY.json (ADR-0003)"| GI
  GI <-->|"ciphertext only (ADR-0015)"| MINIO
  GI -.->|"off by default, feature-flagged"| LLM

  classDef planned stroke-dasharray: 5 5
  class LLM planned
```

**There is no integration with Hacienda.** GestorIA produces a casilla-by-casilla sheet
and the human files it. That is scope, not a limitation — e-filing is explicitly out
(`plans/DEVELOPMENT_PLAN.md` §1). The same AEAT that receives the filing also supplies
the golden values the engine is tested against, in the other direction and by hand.

Dashed edges and boxes, here and below, mean deferred or not yet built. What each one
means today is listed once, in `graph/architecture.md` → Gap vs target.

---

## System Components

```mermaid
flowchart TB
  subgraph client["Client — <i>form undecided until M3</i>"]
    SPA["<b>web/</b> — React 19 + TS + Vite<br/>onboarding · upload · review queue ·<br/>results · trace · casilla export<br/><i>contains no tax math</i><br/>SPEC-012 · ADR-0007"]
    CLI["<b>CLI</b><br/><i>the live alternative</i>"]
  end

  subgraph api["<b>GestorIA.Api</b> — .NET 10 / ASP.NET Core · container 'api'"]
    EP["Minimal API /api/v1<br/>OpenAPI 3.1 · problem+json · API key<br/>SPEC-009"]
    APP["<b>Application</b> — use cases, DTOs,<br/>validators, ports"]
    ENG["<b>Engine — pure and deterministic</b><br/>no I/O · no HTTP · no clock<br/>SPEC-002/003/006/008"]
    INF["<b>Infrastructure</b><br/>EF Core · IFileStorage · parsers ·<br/>classifier · TaxYearConfigLoader · OCR client"]
    JOB["BackgroundService<br/>in-process Channel&lt;ExtractJob&gt;<br/>ADR-0009 <i>(Proposed)</i>"]
  end

  OCR["<b>gestoria-ocr</b> — Python 3.12 / FastAPI<br/>POST /extract · stateless, keeps nothing<br/>PaddleOCR + OpenCV<br/>SPEC-005 · ADR-0002 <i>(Proposed)</i>"]

  PG[("<b>PostgreSQL 16</b><br/>ledger · documents · job state ·<br/>traces (JSONB) · numeric for money<br/>ADR-0006")]
  BLOB[("<b>MinIO</b> — S3 compatible<br/>AES-256-GCM client-side<br/>ADR-0015")]
  CFG[/"<b>config/tax-years/YYYY.json</b><br/>+ schema.json<br/>ADR-0003 · SPEC-007"/]

  SPA -->|"generated TS client"| EP
  CLI --> EP
  EP --> APP
  APP --> ENG
  APP --> INF
  INF --> PG
  INF --> BLOB
  INF -.->|"REST + shared JSON Schema<br/>Polly retry / circuit breaker<br/>ADR-0005 <i>(Proposed)</i>"| OCR
  JOB --> INF
  CFG -->|"read by the loader"| INF
  INF ==>|"TaxYearConfig injected;<br/>SHA-256 hashed into every result"| ENG

  classDef planned stroke-dasharray: 5 5
  class OCR,CLI,SPA,JOB planned
```

Note where the config enters: the file is read by **Infrastructure** and handed to the
Engine already parsed and hashed. SPEC-007 §3 is explicit about why — put the loader in
the Engine and "the engine's purity becomes a comment rather than a property".

Containers actually deployed in v1.0: `api` and `postgres`, plus the MinIO already
running on the author's VPS. `ocr` and `caddy` are v1.x.


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

```mermaid
sequenceDiagram
  autonumber
  actor U as Taxpayer
  participant UI as SPA / CLI
  participant API as Api + Application
  participant P as Parsers / OCR client
  participant Q as Review queue
  participant L as Ledger (Postgres)
  participant E as Engine (pure)
  participant X as Explanations
  participant A as AEAT Renta WEB

  U->>UI: upload statement / nómina / invoice
  UI->>API: POST /profiles/{id}/documents
  API->>P: machine-readable → C# parser; PDF or image → gestoria-ocr (v1.x)
  P-->>API: ExtractedDocument { type, fields[value, confidence], rawText }
  API->>Q: low-confidence fields + UNCLEAR transactions
  Note over Q,L: nothing enters the ledger unconfirmed —<br/>no deductible expense without a linked invoice
  U->>UI: confirm or correct
  UI->>API: POST /documents/{id}/confirm
  API->>L: immutable ledger row, linked to its SourceDocument
  API->>API: classify transactions · match payments to invoices ·<br/>split base / IVA / retención (±0.01, ±45 days)
  U->>UI: run quarter or annual
  UI->>API: POST /profiles/{id}/calculations/annual
  API->>E: Run(profile, ledger, yearConfig)
  E-->>API: AnnualResult + Trace + CreditOutcomes + ConfigHash
  API->>X: trace + outcomes → localized messages (ES/EN/RU)
  API-->>UI: casillas · explanations · missed credits · warnings
  U->>A: types the casilla sheet in by hand
```

Quarterly flow is identical up to step 4, then `Modelo130Calculator` / `Modelo303Calculator` with cumulative year-to-date inputs.

---

## Deployment

| Mode | Where | Status |
|---|---|---|
| Local-only | the author's machine, Docker Compose, no public port. Documents in MinIO on his VPS, encrypted client-side (ADR-0015) | **v1.0** (ADR-0010) |
| Single VPS | one host, Docker Compose, Caddy TLS | backlog — needs a second user first (ADR-0010 supersedes ADR-0008) |
| Split | API on VPS, OCR on a GPU box | later — requires the RabbitMQ `IExtractJobQueue` (ADR-0009) |

```mermaid
flowchart TB
  subgraph laptop["Author's machine — Docker Compose, no public port, no TLS terminator"]
    A["<b>api</b><br/>.NET 10 · engine + jobs · hashed API key"]
    P[("postgres:16")]
    O["<b>ocr</b><br/><i>v1.x · no public port</i>"]
    A --> P
    A -.-> O
  end

  subgraph vps["Author's VPS"]
    M[("<b>MinIO</b><br/>AES-256-GCM, per-document data key<br/>wrapped by a master key")]
  end

  A -->|"ciphertext only —<br/>the VPS never holds a key"| M
  K["<b>Master key</b><br/><i>escrowed offline; a rehearsed restore<br/>is a release blocker (M1)</i>"] -.-> A

  subgraph later["Backlog — needs a second user"]
    C["caddy TLS + OIDC"] --> A2["api on a VPS"]
    A2 -.->|"RabbitMQ IExtractJobQueue"| O2["ocr on a GPU box"]
  end

  classDef planned stroke-dasharray: 5 5
  class O,C,A2,O2,later planned
```

The engine never reads documents, so MinIO being unreachable cannot block a
calculation — the separation is what makes an off-machine blob store acceptable at all.

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
