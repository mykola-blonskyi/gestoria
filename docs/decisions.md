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
| [ADR-0007](adr/ADR-0007-frontend-react-typescript.md) | React + TypeScript SPA (not Blazor) | Accepted (framework, routing and i18n superseded by ADR-0017) | 2026-09-17 |
| [ADR-0008](adr/ADR-0008-deployment-hosted-vps-v1.md) | Hosted single-VPS deployment for v1 | Superseded by ADR-0010 | 2026-09-17 |
| [ADR-0009](adr/ADR-0009-ocr-job-queue-inprocess-channel.md) | In-process `Channel<T>` job queue for OCR extraction in v1 | Proposed | 2026-09-17 |
| [ADR-0010](adr/ADR-0010-deployment-local-only-v1.md) | Local-only deployment for v1.0; hosted VPS deferred | Accepted | 2026-09-18 |
| [ADR-0011](adr/ADR-0011-aeat-simulator-as-correctness-oracle.md) | The AEAT simulator decides golden values | Accepted | 2026-09-18 |
| [ADR-0012](adr/ADR-0012-v1-scope-one-employee-modelo-100.md) | v1.0 computes Modelo 100 for one employee in Valencia | Superseded by ADR-0013 | 2026-09-18 |
| [ADR-0013](adr/ADR-0013-v1-scope-employee-and-autonomo.md) | v1.0 covers employee and autónomo scope; OCR and Madrid stay out | Accepted (sequencing amended by ADR-0014; Madrid part superseded by ADR-0016) | 2026-09-18 |
| [ADR-0014](adr/ADR-0014-build-order-april-2027-cluster.md) | Build order follows the April 2027 deadline cluster | Accepted | 2026-09-18 |
| [ADR-0015](adr/ADR-0015-documents-in-minio-encrypted-client-side.md) | Documents live in MinIO on the author's VPS, encrypted client-side | Accepted | 2026-09-18 |
| [ADR-0016](adr/ADR-0016-madrid-in-v1.md) | Madrid is a v1.0 region, in every tax year | Accepted | 2026-09-27 |
| [ADR-0017](adr/ADR-0017-web-app-nextjs-next-intl.md) | The web app is Next.js (App Router) with next-intl, in `web/` | Accepted | 2026-09-27 |

## Small decisions (no ADR)

- **One taxpayer profile per local installation, its tax year a field** — 2026-09-27, #69. SPEC-001 calls the profile "per year"; with one user and one dashboard, one profile whose year is chosen in settings is enough, and a second `POST` is a 409. The database holds the rule (a unique index on an always-true column), not only the handler, so racing creates store one profile. Per-year profiles return with a second year to compare or a second user.
- **A new profile starts on the newest tax year whose configuration declares no gap** — 2026-09-27, #69. The newest year, 2026, refuses every set-aside estimate until its renta window is published (around March 2027), so defaulting to it would greet a first profile with a refusal. A gapped year stays selectable, with its gaps listed in settings, and its estimate shows the 422 as "not published yet". The default moves on by itself when `2026.json` closes its gaps or `2027.json` lands complete.
- **The API migrates its database as it starts** — 2026-09-27, #69. One user, one local database: `Database.MigrateAsync()` at start-up is idempotent and saves a setup step. It is skipped while `dotnet build` generates the OpenAPI document. Revisit with a second instance or a hosted database, where migrations become a deployment step.

- **There is a web app, and the CLI stays** — 2026-09-27, settling the CLI-or-SPA question that was deferred to M3. The author chose a Next.js web app in `web/` (ADR-0017, #65).
- **Regions in v1.0: Valencia (`VC`) and Madrid (`MD`)** — 2026-09-27, superseding the 2026-09-18 note that kept Valencia only. Both are complete in every tax-year file, and the schema requires both (ADR-0016).
- **Build order follows deadlines, not scope** — 2026-09-18. Quarterly forms first, because the author registers as an autónomo in January 2027 and Modelo 130 and 303 for Q1 2027 fall due 20 April 2027, alongside Renta 2026 (ADR-0014). The autónomo annual path is not needed until April 2028.
- **PostgreSQL, EF Core and ASP.NET stay in v1.0** — 2026-09-18, closing a challenge raised and withdrawn the same day. ADR-0006 stands unchanged. The deciding argument is not volume, it is `decimal`. SQLite has no decimal type, EF Core maps `decimal` to TEXT there, and ordering and comparison on money break. ADR-0004 makes `double`/`float` for money a build error, so a store that cannot hold an exact decimal is disqualified before convenience is discussed. PostgreSQL `numeric` holds it exactly. The ledger is also genuinely relational (transaction → linked document → invoice) with immutable versioned rows per SPEC-001 §5, and the Phase 3 learning track lists EF Core and ASP.NET as goals.
- **Retención is a property of the payer, not the issuer** — 2026-09-18. See business rule 3b. This was latent in SPEC-001, where `AutonomoRegistration.RetencionRate` looks like it belongs on the invoice.
- **v1.0 produces the Modelo 349 itself** — 2026-09-18, not just per-client totals. The data is a by-product of the 303 calculation, and a quarter with no intra-EU operations producing no filing is part of the output.
- **A gestor prepares Q1 2027 in parallel** — 2026-09-18. The 130, 303 and 349 have no borrador and no simulator, and the author's first quarter combines tarifa plana, EU reverse charge, US export and zero retención, which no worked example covers. Q1 is filed from the gestor's numbers; the diff against the engine becomes `gestor-prepared` goldens; Q2 2027 is the first self-filed quarter.
- **Commits are authored solely by the repository owner** — 2026-09-18. Enforced by `.githooks/commit-msg`, not only documented. See `docs/CONVENTIONS.md`.
- **VIES verification is recorded by hand in v1.0** — 2026-09-18. `Client.Vies { Number, VerifiedOn }` plus a warning at invoice time when it is missing or stale. The VIES consultation reference is the evidence worth storing, and an automated call that returns a boolean discards it.
- **Golden oracles are ranked** — 2026-09-18. `aeat-simulator`, then `published-example` with a citable reference, then `theory`. Tax-advisor articles find rules; they do not fix numbers (SPEC-011 §1).
- **Credits are the destination, not a mid-plan phase** — 2026-09-18. Renta WEB already produces a borrador with most figures in it. What it does not do is tell you which regional credit you missed. SPEC-006 and SPEC-010 are what v1.0 is for; SPEC-002 exists to make them evaluable.
- **The first filing is Renta 2026, due April to June 2027** — 2026-09-18. The first production config is therefore `2026.json`, not `2025.json`.
- **The prototype is deleted, not kept green** — 2026-09-18. `IrpfTaxCalculator`, `IrpfCalculationResult` and their tests are gone. SPEC-001 §7 and SPEC-011 §4 previously said to keep them compiling until the goldens passed; they had already stopped compiling. `IStatementParser`, `BbvaCsvStatementParser`, `Transaction` and `BbvaParserTests` survive, because SPEC-004 §5 carries them forward.

## Decisions still open


- Whether the 31-week plan survives its own cuts, and what the revised milestone dates are against April 2027.
