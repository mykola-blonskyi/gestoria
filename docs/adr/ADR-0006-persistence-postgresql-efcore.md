# ADR-0006: PostgreSQL with EF Core

**Status:** Accepted · **Date:** 2026-09-17

## Context
Need relational integrity (document → ledger row → trace), JSONB for traces and extracted fields, free and self-hostable.

## Decision
PostgreSQL 16 via EF Core (code-first migrations). `numeric(18,6)` for money columns; JSONB for `CalculationTrace`, `ExtractedDocument.fields`, credit outcomes. Testcontainers for integration tests. SQLite is **not** used, even for tests, to avoid decimal/JSON behaviour drift.

## Alternatives
- SQLite: zero-ops for local mode, but weak `decimal` and JSON support and test/prod drift.
- SQL Server: fine technically, licensing and hosting cost.
- Document DB: no relational integrity for the audit chain.

## Consequences
- One migration per PR touching the model; migrations reviewed.
- Blobs (uploaded files) are not stored in Postgres; a file-storage port abstracts local disk and S3-compatible storage.
- Local-only mode still runs Postgres in a container.
