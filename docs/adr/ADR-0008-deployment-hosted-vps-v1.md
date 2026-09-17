# ADR-0008: Hosted single-VPS deployment for v1; local-only mode deferred

**Status:** Accepted · **Date:** 2026-09-17

## Context
Two deployment topologies were on the table for v1.0: (A) hosted — API, PostgreSQL and the OCR service run on a VPS operated by the project owner, users access a website; (B) local-only — the same containers run on the user's own machine via Docker Compose, data never leaves it. Both are technically possible with the current architecture; the question is which one the v1.0 release must ship and support.

## Decision
v1.0 ships **topology A**: one VPS, Docker Compose (`api`, `postgres`, `ocr`, `caddy`), TLS via Caddy, OCR without a public port. Local-only mode (B) is deferred to v1.x.

## Alternatives
- **B in v1.0**: maximal privacy and no server liability, but adds an end-user installation path (Docker Desktop on Mac/Windows/Linux), installation docs and cross-platform smoke tests to the release scope.
- **A + B in v1.0**: both paths tested and documented from day one — too much surface for a solo developer.

## Consequences
- The architecture keeps the abstractions that make B cheap later: `IFileStorage` (disk / S3-compatible), single-user auth mode, no hard dependency on managed cloud services. These are not removed.
- v1.0 must take hosting seriously: encryption at rest for blobs, encrypted backups, GDPR export/delete, PII-free logs (SPEC-013) are release blockers, not nice-to-haves.
- Phase 6 deliverable "local-only deployment guide" moves to `plans/backlog.md`.
- Revisit when external users ask for it or when the OCR service needs to run on their hardware.
