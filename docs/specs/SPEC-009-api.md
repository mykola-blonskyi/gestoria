# SPEC-009 — REST API (`GestorIA.Api`)

**Status:** Draft · **Phase:** 3 · **ADRs:** 0005, 0006, 0007

## 1. Principles
Versioned (`/api/v1`), JSON, OpenAPI 3.1 generated from code, problem+json errors (RFC 9457). Money as strings with 2 decimals in DTOs (`"1234.56"`), dates ISO-8601.

> **v1.0 trim (2026-09-18).** Idempotency keys and cursor pagination wait for a second user and a large collection. One user with four filings a year has neither problem. The versioned path, OpenAPI generation and problem+json stay, because they cost nothing now and are expensive to retrofit.

## 1.1 Built so far (#66)

- `GET /api/v1/health/live` answers 204.
- `GET /api/v1/config/tax-years` and `GET /api/v1/config/tax-years/{year}`: each year's `configHash`, usable regions and declared gaps (SPEC-007 §3), read through `TaxYearConfigLoader` from the files copied next to the binary (`TaxYears:Directory` overrides). An unknown year is a 404.
- `POST /api/v1/set-aside/estimate?taxYear=YYYY`: the body is the console's input file byte for byte (`src/GestorIA.Cli/README.md`), parsed by the console's own parser, so both refuse the same inputs. The answer is `SetAsideResult` with money as two-decimal strings, the hold-back share as its exact decimal fraction, the trace in the engine's order and the notices.
- Problem types (`src/GestorIA.Api/Problems.cs`): `invalid-input` (400, `errors` keyed by the JSON path of the refused value), `config-gap` (422: no file for the year, or a value the file lacks or declares unpublished), `estimate-refused` (422: an input only the engine can judge, such as actuals out of order), `tax-year-not-found` (404).
- The OpenAPI 3.1 document is generated at build time into `src/GestorIA.Api/openapi/v1.json` and committed. CI fails when it is stale, and oasdiff fails a pull request that breaks the base branch's document (§5). `tests/GestorIA.Api.Tests` validates every set-aside golden's input and every answer against it.
- CORS allows the web app's origin (`Cors:Origins`). No auth yet: the API key of §3 is its own ticket.

## 2. Resources

| Method & path | Purpose |
|---|---|
| `POST /profiles` · `GET /profiles/{id}` · `PUT /profiles/{id}` | Taxpayer profile per year (SPEC-001) |
| `POST /profiles/{id}/documents` (multipart) | Upload; returns `202 { documentId, jobId }` |
| `GET /documents/{id}` | Status, extraction, review state |
| `POST /documents/{id}/confirm` | Body: corrected fields → creates/updates ledger rows |
| `POST /documents/{id}/reject` | |
| `POST /profiles/{id}/bank-statements` | CSV/XLSX import with `bank` adapter id |
| `GET /profiles/{id}/transactions?status=unclear` | Review queue |
| `POST /transactions/{id}/classify` | Body: class, linkedDocumentId?, note |
| `GET /profiles/{id}/review-queue` | Unified queue: low-confidence fields + unclear transactions + missing invoices |
| `POST /profiles/{id}/calculations/quarter` `{ quarter }` | Runs 130 + 303 → `QuarterResult` |
| `POST /profiles/{id}/calculations/annual` `{ mode }` | Runs Modelo 100 → `AnnualResultDto` |
| `GET /calculations/{id}` · `GET /calculations/{id}/trace` · `GET /calculations/{id}/export?format=csv|pdf` | |
| `GET /profiles/{id}/calendar` | Upcoming deadlines with amounts when known |
| `GET /config/tax-years` · `GET /config/tax-years/{year}` | Read-only config (for SPA labels) |
| `GET /profiles/{id}/export` · `DELETE /profiles/{id}` | GDPR export/delete (SPEC-013) |
| `GET /health/live` · `GET /health/ready` | readiness includes DB and OCR reachability |

## 3. Auth
v1 local mode: single user, API key in header. OIDC (Authorization Code + PKCE) behind a feature flag for hosted mode; all resources scoped to the authenticated user.

## 4. Errors
`400` validation (field errors), `404`, `409` (document already confirmed), `422` (calculation cannot run: missing config/region, unconfirmed required docs — body lists blockers), `503` (OCR unavailable — upload accepted and queued anyway).

## 5. Acceptance
Integration test suite with Testcontainers: profile → JSON nóminas → annual → golden #1; OpenAPI diff check in CI; generated TS client compiles.
