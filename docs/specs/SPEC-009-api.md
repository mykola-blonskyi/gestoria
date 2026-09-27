# SPEC-009 — REST API (`GestorIA.Api`)

**Status:** Draft · **Phase:** 3 · **ADRs:** 0005, 0006, 0007

## 1. Principles
Versioned (`/api/v1`), JSON, OpenAPI 3.1 generated from code, problem+json errors (RFC 9457). Money as strings with 2 decimals in DTOs (`"1234.56"`), dates ISO-8601.

> **v1.0 trim (2026-09-18).** Idempotency keys and cursor pagination wait for a second user and a large collection. One user with four filings a year has neither problem. The versioned path, OpenAPI generation and problem+json stay, because they cost nothing now and are expensive to retrofit.

## 1.1 Built so far (#66, #68, #69)

- `GET /api/v1/health/live` answers 204 while the process runs. `GET /api/v1/health/ready` answers 204 when PostgreSQL accepts a connection within 5 seconds, and otherwise the 503 `database-unavailable` problem (`src/GestorIA.Api/Health.cs`). Neither needs the key. OCR reachability joins readiness when the OCR service does (§2).
- `GET /api/v1/config/tax-years` and `GET /api/v1/config/tax-years/{year}`: each year's `configHash`, usable regions and declared gaps (SPEC-007 §3), read through `TaxYearConfigLoader` from the files copied next to the binary (`TaxYears:Directory` overrides). An unknown year is a 404.
- `POST /api/v1/set-aside/estimate?taxYear=YYYY`: the body is the console's input file byte for byte (`src/GestorIA.Cli/README.md`), parsed by the console's own parser, so both refuse the same inputs. The answer is `SetAsideResult` with money as two-decimal strings, the hold-back share as its exact decimal fraction, the trace in the engine's order and the notices.
- The taxpayer profile (#69, `src/GestorIA.Api/Profiles/`), stored in PostgreSQL (ADR-0006) through EF Core, migrated as the API starts:
  - `GET /api/v1/profiles` lists the stored profiles; `POST /api/v1/profiles` stores one (201 with `Location`); `GET` and `PUT /api/v1/profiles/{id}` read and replace it; an unknown id is a 404 `profile-not-found`. Local mode keeps one profile per installation, held by the database (a unique index on an always-true `Singleton` column), so concurrent creates store exactly one: a second `POST` is a 409 `profile-exists` naming the first. The tax year is a field of the profile, so changing year is a `PUT`.
  - The body is `ProfileInputDocument`: `taxYear`, `region`, `employment { ingresos, seguridadSocial }`, `activity { alta, previousYear, newActivity }` and `projection { ingresos, gastos, baseCotizacion }`, the console input file's profile and projection. The two either-or facts are objects tagged by `kind`: `previousYear` is `{ "kind": "noActivity" }` or `{ "kind": "rendimientoNeto", "rendimientoNeto": "-1234.56" }`, `newActivity` is `{ "kind": "established" }` or `{ "kind": "started", "period": "first" | "following", "ingresosFromFormerEmployer": "0.00" }`. Amounts are strings in euros with at most two decimals and twelve digits, so the `numeric(18,6)` columns hold them exactly. A body of the wrong shape is refused at its first wrong field; a body of the right shape has every value checked and every refused one named at once in `errors`: the year must have a configuration, the region must be in it, the alta must fall in the year or before, the base de cotización must be a base of the year's tables (LGSS art. 308.1.a 3.ª).
  - `GET /api/v1/profiles/{id}/set-aside/estimate?asOf=Qn` runs the set-aside estimator on the stored profile and answers `SetAsideEstimate`. No closed quarter is stated: the actuals are ledger data, which arrives with transactions, so the projection covers every month of alta. For a golden with no actuals (G12, G16, G22) the answer equals the input file's, step by step. A year whose configuration lacks what the estimate needs is a 422 `config-gap`: every 2026 estimate today, since `2026.json` declares its renta window and Q4 Modelo 130 deadline unpublished.
  - Nothing about the profile is logged (SPEC-013 §2): `ProfileNotLogged` captures every log category at Trace, EF Core's SQL included, while a profile is created, refused, replaced, read and estimated, and finds none of its amounts and nothing shaped like a NIF or an IBAN.
- A database lost after start-up (`src/GestorIA.Api/DatabaseUnavailable.cs`): an exception from any endpoint whose chain holds an `NpgsqlException` that is not a server's answer (a `PostgresException`), or a `PostgresException` about the connection (class `08`, `57P01`–`57P03`), is answered 503 `database-unavailable` instead of an unhandled 500. A unique violation and every other server answer keep their own handling. The body is the same fixed words every time, and the one log line, a Warning, says only that the database is not reachable: Npgsql's message names the host, the port and the database, so the exception is never logged (SPEC-013 §2). EF Core's failure events (`ConnectionError`, `QueryIterationFailed`, `SaveChangesFailed`) are ignored for the same reason; the exception still reaches the handler, and any other failure is logged there as a 500. EF Core's Debug-level connection events name the database and its server on every connection; the API does not log at Debug by default. The profile endpoints declare the 503 in the OpenAPI document. `DatabaseOutage` stops the test's own PostgreSQL container partway through and holds every answer, and every log line at Information or above written after the stop, to this.
- Problem types (`src/GestorIA.Api/Problems.cs`): `invalid-input` (400, `errors` keyed by the JSON path of each refused value, or `taxYear` or `asOf` for a missing or malformed query parameter), `config-gap` (422: no file for the year, or a value the file lacks or declares unpublished), `estimate-refused` (422: an input only the engine can judge, such as actuals out of order), `tax-year-not-found` (404), `profile-not-found` (404), `profile-exists` (409), `api-key-required` (401: no key, more than one, or the wrong one, without saying which), `database-unavailable` (503: the API runs but PostgreSQL does not answer it).
- The web app unlocks only once the key is accepted and `/health/ready` answers: with the database down, the unlock screen says so and shows `docker compose up -d postgres`, as it shows how to start the API when that does not answer. A 503 `database-unavailable` later on the overview or in settings says the same in the app's language.
- The OpenAPI 3.1 document is generated at build time into `src/GestorIA.Api/openapi/v1.json` and committed. CI fails when it is stale, and oasdiff fails a pull request that breaks the base branch's document (§5). `tests/GestorIA.Api.Tests` validates every set-aside golden's input and every answer against it.
- CORS allows the web app's origin (`Cors:Origins`) and its `X-Api-Key` header.
- The local API key of §3 (#68): every endpoint but the health checks answers `401` problem+json `api-key-required` without it.

## 2. Resources

| Method & path | Purpose |
|---|---|
| `POST /profiles` · `GET /profiles` · `GET /profiles/{id}` · `PUT /profiles/{id}` | Taxpayer profile (SPEC-001); one per installation in local mode, its tax year a field (#69) |
| `GET /profiles/{id}/set-aside/estimate?asOf=Qn` | The set-aside estimate of the stored profile (#69) |
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

Local mode as built (#68, `src/GestorIA.Api/ApiKey.cs`):
- The client sends the key in `X-Api-Key`. The OpenAPI document declares it as the `apiKey` security scheme on every operation but the health checks, which stay open so a client can tell "not running" from "locked".
- Configuration holds only the key's SHA-256 (SPEC-013 §2), as `Auth:ApiKeySha256`: user secrets on a development machine, the `Auth__ApiKeySha256` environment variable elsewhere, never a file in the repository. The API refuses to start without a usable hash, including the hash of an empty key.
- The presented key is hashed and compared with `CryptographicOperations.FixedTimeEquals`, so the comparison takes the same time however much of it matches.
- The check runs as middleware before parameter binding, on every path under `/api/v1` but `/api/v1/health`, whatever endpoint routing chose: a locked route asked with the wrong method or `Content-Type`, or a path that does not exist, is a 401 like any other request without the key, not a 405, 415 or 404 that would tell a caller which routes exist. A request without the key does no other work. CORS preflights are answered before the check. The locked route group only declares the key in the OpenAPI document. In Development the API also serves its OpenAPI document at `/openapi/v1.json` without the key: it is the committed `v1.json` and holds no data. Nothing logs the key: `ApiKeyNotLogged` captures every log category at Trace while the key is used, mistyped and left out.

## 4. Errors
`400` validation (field errors), `404`, `409` (document already confirmed), `422` (calculation cannot run: missing config/region, unconfirmed required docs — body lists blockers), `503` (OCR unavailable — upload accepted and queued anyway; `database-unavailable` when PostgreSQL cannot be reached, §1.1).

## 5. Acceptance
Integration test suite with Testcontainers (in place since #69: one PostgreSQL container per test run, from `compose.yaml`'s image, one database per API instance): profile → JSON nóminas → annual → golden #1; OpenAPI diff check in CI; generated TS client compiles.
