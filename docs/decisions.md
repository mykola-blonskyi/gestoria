# Architecture Decisions

Full records live in `docs/adr/` — one file per decision, immutable once **Accepted** (write a new ADR to supersede). Template: `snippets/adr-template.md`.

| ADR | Title | Status | Date |
|---|---|---|---|
| [ADR-0001](adr/ADR-0001-core-language-csharp.md) | C#/.NET for the tax engine and API | Accepted | 2026-09-17 |
| [ADR-0002](adr/ADR-0002-polyglot-ocr-python-service.md) | Separate Python service for OCR and ML | Accepted | 2026-09-17 |
| [ADR-0003](adr/ADR-0003-year-config-not-hardcoded.md) | Tax parameters are versioned JSON per year, never hard-coded | Accepted | 2026-09-17 |
| [ADR-0004](adr/ADR-0004-decimal-money-type.md) | `decimal` for money; rounding only at the casilla boundary | Accepted | 2026-09-17 |
| [ADR-0005](adr/ADR-0005-service-communication.md) | REST + JSON Schema between API and OCR; gRPC deferred | Accepted | 2026-09-17 |
| [ADR-0006](adr/ADR-0006-persistence-postgresql-efcore.md) | PostgreSQL with EF Core | Accepted | 2026-09-17 |
| [ADR-0007](adr/ADR-0007-frontend-react-typescript.md) | React + TypeScript SPA (not Blazor) | Accepted | 2026-09-17 |
| [ADR-0008](adr/ADR-0008-deployment-hosted-vps-v1.md) | Hosted single-VPS deployment for v1; local-only mode deferred | Accepted | 2026-09-17 |
| [ADR-0009](adr/ADR-0009-ocr-job-queue-inprocess-channel.md) | In-process `Channel<T>` job queue for OCR extraction in v1 | Accepted | 2026-09-17 |

## Small decisions (no ADR)

- **Regions in v1: Valencia (`VC`) + Madrid (`MD`)** — confirmed 2026-09-17. Madrid exists to prove the region model is data-driven, not to be a second fully-supported market.

## Decisions still open

- None.
