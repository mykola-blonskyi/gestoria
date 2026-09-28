# SPEC-009 — REST API (`GestorIA.Api`)

**Status:** Draft · **Phase:** 3 · **ADRs:** 0005, 0006, 0007

## 1. Principles
Versioned (`/api/v1`), JSON, OpenAPI 3.1 generated from code, problem+json errors (RFC 9457). Money as strings with 2 decimals in DTOs (`"1234.56"`), dates ISO-8601.

> **v1.0 trim (2026-09-18).** Idempotency keys and cursor pagination wait for a second user and a large collection. One user with four filings a year has neither problem. The versioned path, OpenAPI generation and problem+json stay, because they cost nothing now and are expensive to retrofit.

## 1.1 Built so far (#66, #68, #69, #72, #74)

- `GET /api/v1/health/live` answers 204 while the process runs. `GET /api/v1/health/ready` answers 204 when PostgreSQL accepts a connection within 5 seconds, and otherwise the 503 `database-unavailable` problem (`src/GestorIA.Api/Health.cs`). Neither needs the key. OCR reachability joins readiness when the OCR service does (§2).
- `GET /api/v1/config/tax-years` and `GET /api/v1/config/tax-years/{year}`: each year's `configHash`, usable regions and declared gaps (SPEC-007 §3), read through `TaxYearConfigLoader` from the files copied next to the binary (`TaxYears:Directory` overrides). An unknown year is a 404.
- `POST /api/v1/set-aside/estimate?taxYear=YYYY`: the body is the console's input file byte for byte (`src/GestorIA.Cli/README.md`), parsed by the console's own parser, so both refuse the same inputs. The answer is `SetAsideResult` with money as two-decimal strings, the hold-back share as its exact decimal fraction, the trace in the engine's order and the notices.
- The taxpayer profile (#69, `src/GestorIA.Api/Profiles/`), stored in PostgreSQL (ADR-0006) through EF Core, migrated as the API starts:
  - `GET /api/v1/profiles` lists the stored profiles; `POST /api/v1/profiles` stores one (201 with `Location`); `GET` and `PUT /api/v1/profiles/{id}` read and replace it; an unknown id is a 404 `profile-not-found`. Local mode keeps one profile per installation, held by the database (a unique index on an always-true `Singleton` column), so concurrent creates store exactly one: a second `POST` is a 409 `profile-exists` naming the first. The tax year is a field of the profile, so changing year is a `PUT`.
  - The body is `ProfileInputDocument`: `taxYear`, `region`, `employment { ingresos, seguridadSocial }`, `activity { alta, previousYear, newActivity }` and `projection { ingresos, gastos, baseCotizacion }`, the console input file's profile and projection. The two either-or facts are objects tagged by `kind`: `previousYear` is `{ "kind": "noActivity" }` or `{ "kind": "rendimientoNeto", "rendimientoNeto": "-1234.56" }`, `newActivity` is `{ "kind": "established" }` or `{ "kind": "started", "period": "first" | "following", "ingresosFromFormerEmployer": "0.00" }`. Amounts are strings in euros with at most two decimals and twelve digits, so the `numeric(18,6)` columns hold them exactly. A body of the wrong shape is refused at its first wrong field; a body of the right shape has every value checked and every refused one named at once in `errors`: the year must have a configuration, the region must be in it, the alta must fall in the year or before, the base de cotización must be a base of the year's tables (LGSS art. 308.1.a 3.ª).
  - `GET /api/v1/profiles/{id}/set-aside/estimate?asOf=Qn` runs the set-aside estimator on the stored profile and answers `SetAsideEstimate`. No closed quarter is stated: the actuals are ledger data, which arrives with transactions, so the projection covers every month of alta. For a golden with no actuals (G12, G16, G22) the answer equals the input file's, step by step. A year whose configuration lacks what the estimate needs is a 422 `config-gap`: every 2026 estimate today, since `2026.json` declares its renta window and Q4 Modelo 130 deadline unpublished.
  - `GET /api/v1/profiles/{id}/export` answers everything stored for the profile in one versioned document (§2.1), with `Cache-Control: no-store` and `Content-Disposition: attachment; filename="gestoria-export-YYYY-MM-DD.json"`, the day of the export in Madrid and nothing about whose it is (#74). `DELETE /api/v1/profiles/{id}` deletes the profile and every row that belongs to it and answers 204; the rows go with it through their foreign keys' `ON DELETE CASCADE`, in one statement. Nothing is kept. Both answer an unknown id with 404 `profile-not-found`. `tests/GestorIA.Api.Tests/ProfileDeletion.cs` is the documented deletion test (SPEC-013 §4): it reads every table from the EF Core model, requires each to hold a row before the delete and none after, so a table added later is covered without editing it.
  - Nothing about the profile is logged (SPEC-013 §2): `ProfileNotLogged` captures every log category at Trace, EF Core's SQL included, while a profile is created, refused, replaced, read, estimated, exported and deleted, and finds none of its amounts and nothing shaped like a NIF or an IBAN.
- Bank statements and their movements (#72, `src/GestorIA.Api/Transactions/`), stored under the profile in PostgreSQL:
  - `POST /api/v1/profiles/{id}/bank-statements?bank=bbva` imports a statement. The body is the file itself, sent as `text/csv`, `text/plain` or `application/vnd.ms-excel` (the label Windows gives a `.csv` when Excel is installed); any other `Content-Type`, a form upload or JSON among them, or none, is a 415 `statement-media-type`, after the key check like every refusal. At most 2 MiB (`StatementFile.MaxBytes`; a year of a personal account is well under 200 KB), read before anything else and refused whole above that with a 413 `statement-too-large`. What is not text is refused before parsing: a ZIP archive such as an XLSX workbook, anything holding a NUL byte (UTF-16 among them). The text is UTF-8, with or without a byte-order mark, else Windows-1252. The `bank` adapter parses it (SPEC-004 §5); a file it cannot read is refused whole, with nothing stored, as `invalid-input` whose `errors` are keyed `file` or `line N` and never quote the file. The answer is `BankStatementImport { bank, lines, imported, alreadyImported }`.
  - **Idempotent by a key per line.** A line's key is the SHA-256 of its booking date, value date, amount and description, and of its occurrence: the count of identical lines before it in the same file. Two identical coffees on one day are two lines, and a later export covering the same days gives them the same two keys. The balance is not part of the key. A unique index on `(ProfileId, LineKey)` holds it in the database, and imports into one profile run one at a time under the profile's row lock (`SELECT … FOR UPDATE`), so importing a statement again, or an overlapping one, or the same one twice at once, stores each line once. Without an account in the key, two accounts that print the same line on the same day would count it once; accounts arrive with the classifier (SPEC-001 §7).
  - `GET /api/v1/profiles/{id}/transactions?year=YYYY&quarter=Qn` answers the stored movements as `TransactionView { id, bookingDate, valueDate, description, amount, balance }`, money as signed two-decimal strings (positive is money in), in booking-date order; a day's lines follow the order of the imports that stored them, then their line numbers in their files. Both are columns (`ImportSequence`, the profile's imports counted from 1 under its row lock, and `LineNumber`), so neither the clock nor the ids decide the order. Without `year`, every movement; `quarter` needs `year` and filters by booking date. No pagination: a year is a few thousand rows (§1).
  - The movements belong to the profile: a foreign key with cascading delete, so deleting a profile deletes them (SPEC-013, #74).
  - Nothing about a statement is logged: `TransactionsNotLogged` captures every category at Trace while a statement is imported, imported again, refused and listed, and finds none of its descriptions or amounts and nothing shaped like a NIF or an IBAN.
- A database lost after start-up (`src/GestorIA.Api/DatabaseUnavailable.cs`): an exception from any endpoint whose chain holds an `NpgsqlException` that is not a server's answer (a `PostgresException`), or a `PostgresException` about the connection (class `08`, `57P01`–`57P03`), is answered 503 `database-unavailable` instead of an unhandled 500. A unique violation and every other server answer keep their own handling. The body is the same fixed words every time, and the one log line, a Warning, says only that the database is not reachable: Npgsql's message names the host, the port and the database, so the exception is never logged (SPEC-013 §2). EF Core's failure events (`ConnectionError`, `QueryIterationFailed`, `SaveChangesFailed`) are ignored for the same reason; the exception still reaches the handler, and any other failure is logged there as a 500. So are the Debug-level connection and data-reader events (opening, opened, closing, closed, disposing, disposed, `MigrateUsingConnection`), which name the database and its server on every connection. The one place the database's name still reaches a log is the SQL of the start-up migration's `CREATE DATABASE`, the first time it runs. The profile endpoints, export and delete among them, and the transaction endpoints declare the 503 in the OpenAPI document. `DatabaseOutage` stops the test's own PostgreSQL container partway through and holds every answer, and every log line written after the stop at any level down to Trace, to this.
- Problem types (`src/GestorIA.Api/Problems.cs`): `invalid-input` (400, `errors` keyed by the JSON path of each refused value, by the name of a missing or malformed query parameter such as `taxYear`, `asOf`, `bank`, `year` or `quarter`, or by `file` or `line N` for a statement), `config-gap` (422: no file for the year, or a value the file lacks or declares unpublished), `estimate-refused` (422: an input only the engine can judge, such as actuals out of order), `tax-year-not-found` (404), `profile-not-found` (404), `profile-exists` (409), `statement-too-large` (413), `statement-media-type` (415), `api-key-required` (401: no key, more than one, or the wrong one, without saying which), `database-unavailable` (503: the API runs but PostgreSQL does not answer it).
- The web app unlocks only once the key is accepted and `/health/ready` answers: with the database down, the unlock screen says so and shows `docker compose up -d postgres`, as it shows how to start the API when that does not answer. A 503 `database-unavailable` later on the overview, in settings or on the transactions page says the same in the app's language.
- The OpenAPI 3.1 document is generated at build time into `src/GestorIA.Api/openapi/v1.json` and committed. CI fails when it is stale, and oasdiff fails a pull request that breaks the base branch's document (§5). `tests/GestorIA.Api.Tests` validates every set-aside golden's input and every answer against it.
- CORS allows the web app's origin (`Cors:Origins`), the methods `GET`, `POST`, `PUT` and `DELETE`, and its `X-Api-Key` header.
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
| `POST /profiles/{id}/bank-statements?bank=bbva` | Statement import, the file as the body; idempotent per line (#72). XLSX later |
| `GET /profiles/{id}/transactions?year=&quarter=` | Stored movements, by quarter (#72); `?status=unclear` becomes the review queue with the classifier |
| `POST /transactions/{id}/classify` | Body: class, linkedDocumentId?, note |
| `GET /profiles/{id}/review-queue` | Unified queue: low-confidence fields + unclear transactions + missing invoices |
| `POST /profiles/{id}/calculations/quarter` `{ quarter }` | Runs 130 + 303 → `QuarterResult` |
| `POST /profiles/{id}/calculations/annual` `{ mode }` | Runs Modelo 100 → `AnnualResultDto` |
| `GET /calculations/{id}` · `GET /calculations/{id}/trace` · `GET /calculations/{id}/export?format=csv|pdf` | |
| `GET /profiles/{id}/calendar` | Upcoming deadlines with amounts when known |
| `GET /config/tax-years` · `GET /config/tax-years/{year}` | Read-only config (for SPA labels) |
| `GET /profiles/{id}/export` · `DELETE /profiles/{id}` | GDPR export/delete (SPEC-013); the export format is §2.1 (#74) |
| `GET /health/live` · `GET /health/ready` | readiness includes DB and OCR reachability |

## 2.1 Export format (#74)

`GET /profiles/{id}/export` answers `ProfileExport`, the file the user keeps outside the machine and what the restore (#75) reads back:

```json
{
  "format": "gestoria.export",
  "formatVersion": 1,
  "classification": "personal-financial-data",
  "exportedAt": "2026-09-28T09:00:00+00:00",
  "entities": {
    "profiles": [ { "id": "…", "taxYear": 2025, "region": "VC", "employment": { … }, "activity": { … }, "projection": { … } } ],
    "bankTransactions": [
      { "id": "…", "bookingDate": "2025-01-02", "valueDate": "2025-01-02", "description": "…", "amount": "-12.50", "balance": "1987.50",
        "importSequence": 1, "lineNumber": 7, "lineKey": "<64 hex digits>" }
    ]
  }
}
```

- `format` is always `gestoria.export`, so a reader can refuse a file that is not an export before it looks further.
- `formatVersion` is an integer. It is raised when a change would make an existing export read differently: an entity kind or a field removed or renamed, or a field whose meaning changes. Adding an entity kind, or an optional field to one, keeps the version. A reader never drops what it does not know: a restore that meets a version, an entity kind or a field it does not know refuses the file rather than restore part of it.
- `classification` labels the file as personal financial data (SPEC-013 §1) wherever it ends up.
- `exportedAt` is the moment of the export, ISO-8601 with its offset, in UTC. The file name the API gives it (`Content-Disposition`, `ProfileExport.FileName`, tested at fixed instants either side of midnight in Madrid) and the web app's download both carry the day of that moment in Madrid (`Europe/Madrid`; ADR-0016's regions are on peninsular time).
- `entities` has one member per table of the database, named after it (`Profiles` is `profiles`, `BankTransactions` is `bankTransactions`), each the list of that table's rows that belong to the profile. `ProfileExportEndpoint.EveryTableOfTheModelIsExportedWithAllItsRows` reads the tables from the EF Core model and fails when a table has no member or a member has fewer rows than the table, so a table added later cannot be left out of the export unnoticed.
  - `profiles` holds the one `ProfileView` (§1.1), with its `id`.
  - `bankTransactions` (#72) holds the profile's statement lines in the list's order (booking date, then `importSequence`, then `lineNumber`), each the `TransactionView` of `GET /transactions` plus what a restore needs to store it again exactly: `importSequence` and `lineNumber`, which order a day's lines, and `lineKey`, the SHA-256 that keeps a later import of the same statement from storing a line twice (§1.1). With the key restored, importing the statement again after a restore adds nothing.
  - Money stays a two-decimal string and dates ISO-8601, as everywhere in the API.
  - **A known kind missing from a version 1 file reads as zero rows.** A file exported before #72 has no `bankTransactions` and is still a valid version 1 export: its profile had no stored movements. What a restore refuses is a kind, a field or a version it does not know, never a known kind that is absent.
- The export holds only what the user entered or uploaded, plus the ids, order and line keys the API gave it, and nothing computed: estimates and traces are recomputed from it.

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
