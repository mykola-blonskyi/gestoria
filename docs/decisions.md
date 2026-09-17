# Architecture Decisions

Full records live in `docs/adr/` — one file per decision, immutable once **Accepted** (write a new ADR to supersede). Template: `snippets/adr-template.md`.

An ADR is **Accepted** only when the decision has been felt: code written, a service called, a benchmark run. Before that it is **Proposed**, however confident the reasoning looks. See `docs/CONVENTIONS.md`.

| ADR | Title | Status | Date |
|---|---|---|---|
| [ADR-0001](adr/ADR-0001-core-language-csharp.md) | C#/.NET for the tax engine and API | Accepted | 2026-09-17 |
| [ADR-0002](adr/ADR-0002-polyglot-ocr-python-service.md) | Separate Python service for OCR and ML | Proposed | 2026-09-17 |
| [ADR-0003](adr/ADR-0003-year-config-not-hardcoded.md) | Tax parameters are versioned JSON per year, never hard-coded | Accepted | 2026-09-17 |
| [ADR-0004](adr/ADR-0004-decimal-money-type.md) | `decimal` for money; rounding only at the casilla boundary | Accepted | 2026-09-17 |
| [ADR-0005](adr/ADR-0005-service-communication.md) | REST + JSON Schema between API and OCR; gRPC deferred | Proposed | 2026-09-17 |
| [ADR-0006](adr/ADR-0006-persistence-postgresql-efcore.md) | PostgreSQL with EF Core | Accepted | 2026-09-17 |
| [ADR-0007](adr/ADR-0007-frontend-react-typescript.md) | React + TypeScript SPA (not Blazor) | Accepted | 2026-09-17 |
| [ADR-0008](adr/ADR-0008-deployment-hosted-vps-v1.md) | Hosted single-VPS deployment for v1 | Superseded by ADR-0010 | 2026-09-17 |
| [ADR-0009](adr/ADR-0009-ocr-job-queue-inprocess-channel.md) | In-process `Channel<T>` job queue for OCR extraction in v1 | Proposed | 2026-09-17 |
| [ADR-0010](adr/ADR-0010-deployment-local-only-v1.md) | Local-only deployment for v1.0; hosted VPS deferred | Accepted | 2026-09-18 |
| [ADR-0011](adr/ADR-0011-aeat-simulator-as-correctness-oracle.md) | The AEAT simulator decides golden values | Accepted | 2026-09-18 |

## Small decisions (no ADR)

- **Regions in v1: Valencia (`VC`) + Madrid (`MD`)** — confirmed 2026-09-17. Madrid exists to prove the region model is data-driven, not to be a second fully-supported market. Note the conflict to resolve: `plans/current.md` schedules the Madrid scale in Phase 0, `config/tax-years/2025.example.json` marks it `_todo: fill in Phase 2`.
- **The prototype is deleted, not kept green** — 2026-09-18. `IrpfTaxCalculator`, `IrpfCalculationResult` and their tests are gone. SPEC-001 §7 and SPEC-011 §4 previously said to keep them compiling until the goldens passed; they had already stopped compiling. `IStatementParser`, `BbvaCsvStatementParser`, `Transaction` and `BbvaParserTests` survive, because SPEC-004 §5 carries them forward.

## Decisions still open

- Which SPEC-013 controls v1.0 ships, now that ADR-0010 has removed the hosting that justified most of them.
- Whether the SPA (SPEC-012, Phase 5) belongs in v1.0 at all, given one user.
- Whether Phase 0 should still gate on a complete `2025.json`, when four `_todo` blocks are research against BOE and only the `VC` scale is needed by the first goldens.
- Which document types Phase 4 must extract, against how many the author actually receives in a year.
- Whether `2025.example.json` becomes `2025.json` before or after the first golden passes.
