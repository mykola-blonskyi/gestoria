# ADR-0010: Local-only deployment for v1.0; hosted VPS deferred

**Status:** Accepted · **Date:** 2026-09-18 · **Supersedes:** ADR-0008

## Context
ADR-0008 chose a hosted single VPS for v1.0. It was written on 2026-09-17, before any engine code existed and before anyone had asked who v1.0 is for.

That audience is now settled. v1.0 has one user, the author, computing his own Renta. Nobody else's NIF, salary or IBAN is involved.

Hosting other people's tax data makes the author a data controller under GDPR, personally liable, while he is still learning the language the engine is written in. ADR-0008 priced that correctly and accepted the bill: it made encryption at rest, encrypted backups and GDPR export and delete into release blockers. With one user, that bill buys nothing.

## Decision
v1.0 runs on the author's own machine. Docker Compose with `api`, `postgres` and `ocr`, no public port, no TLS terminator, single-user API key auth as already specified in SPEC-009 §3.

Hosting moves to the backlog behind a real request from a real second person.

## Alternatives
- **Hosted VPS (ADR-0008).** Justified when the user set includes people other than the author. Until then it adds a server to operate, a legal role to occupy and four release blockers, in exchange for a convenience the only user does not need.
- **Both topologies in v1.0.** Doubles the release surface for a solo developer. ADR-0008 rejected this for the same reason and that reasoning still holds.

## Consequences
- Caddy leaves the container set. OIDC, upload rate limiting and public-port hardening move to the hosted milestone.
- Document blob encryption at rest and PII-free logs stay in v1.0. They are cheap, and they are the controls that matter the moment a laptop is backed up to a cloud drive.
- Which of the remaining SPEC-013 controls v1.0 ships is not decided here.
- The abstractions that keep hosting cheap later stay untouched: `IFileStorage`, the single-user auth mode, no dependency on a managed cloud service. ADR-0008 already protected these and its instinct there was right.
- ADR-0009 justified an in-process queue partly on "load is one user uploading a batch a few times a year". That was an assumption. It is now a decision.
- `plans/backlog.md` swaps its deployment entries: the local-only guide comes out, the hosted VPS goes in.
- Revisit when a second person asks to use it, or when the OCR service needs hardware the author does not own.
