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
| [ADR-0012](adr/ADR-0012-v1-scope-one-employee-modelo-100.md) | v1.0 computes Modelo 100 for one employee in Valencia | Superseded by ADR-0013 | 2026-09-18 |
| [ADR-0013](adr/ADR-0013-v1-scope-employee-and-autonomo.md) | v1.0 covers employee and autónomo scope; OCR and Madrid stay out | Accepted | 2026-09-18 |

## Small decisions (no ADR)

- **Regions in v1.0: Valencia (`VC`) only** — 2026-09-18, superseding the 2026-09-17 note that added Madrid. A fake region in a test fixture proves the model is data-driven (ADR-0013).
- **Build order follows deadlines, not scope** — 2026-09-18. The employee Modelo 100 path is built first because Renta 2026 is the only date Hacienda has set for this project. The autónomo path follows and gets a deadline when the author registers.
- **Credits are the destination, not a mid-plan phase** — 2026-09-18. Renta WEB already produces a borrador with most figures in it. What it does not do is tell you which regional credit you missed. SPEC-006 and SPEC-010 are what v1.0 is for; SPEC-002 exists to make them evaluable.
- **The first filing is Renta 2026, due April to June 2027** — 2026-09-18. The first production config is therefore `2026.json`, not `2025.json`.
- **The prototype is deleted, not kept green** — 2026-09-18. `IrpfTaxCalculator`, `IrpfCalculationResult` and their tests are gone. SPEC-001 §7 and SPEC-011 §4 previously said to keep them compiling until the goldens passed; they had already stopped compiling. `IStatementParser`, `BbvaCsvStatementParser`, `Transaction` and `BbvaParserTests` survive, because SPEC-004 §5 carries them forward.

## Decisions still open

- Whether the author intends to register as an autónomo and when. That is what would give the autónomo path a deadline, and it is also what decides whether ADR-0010's single-user premise still holds.

- Whether the SPA (SPEC-012) earns its place against a CLI, now that ADR-0012 has removed the review queue it was largely built around.
- Whether PostgreSQL and EF Core (ADR-0006) and an ASP.NET API (SPEC-009) are still justified for one user typing one profile a year, or whether v1.0 is an engine library plus a config plus a thin front end.
- Which SPEC-013 controls v1.0 ships, now that ADR-0010 removed the hosting and ADR-0012 removed the document uploads that justified most of them.
- Whether the 31-week plan survives its own cuts, and what the revised milestone dates are against April 2027.
