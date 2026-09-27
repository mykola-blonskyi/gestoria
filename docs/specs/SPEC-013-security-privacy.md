# SPEC-013 — Security and Privacy

**Status:** Draft · **Phase:** 3, 6

## 1. Data classification
Everything in the ledger and documents is personal financial data (GDPR). NIFs, IBANs, names, salaries are PII.

## 2. Controls
- **At rest**: document blobs encrypted **client-side** (AES-256-GCM, per-document data key wrapped by a master key from env) before upload to MinIO on the author's VPS (ADR-0015). MinIO never holds plaintext or the master key. The local PostgreSQL volume relies on full-disk encryption.
- **Key escrow is a release blocker.** The master key is stored outside both the laptop and the VPS, in the author's password manager, with a written recovery procedure. A restore using only the escrowed key is rehearsed before the first real document is uploaded. A key that exists in one place makes the backup a second way to lose the data.
- **In transit**: TLS to MinIO. The OCR service is deferred (ADR-0013), so the API↔OCR path is not a v1.0 concern.
- **Logging**: structured logs with a PII scrubber; never log rawText, amounts with identifiers, or user file names; correlation ids only. Automated test greps log output for NIF/IBAN patterns: `tests/GestorIA.Api.Tests/ProfileNotLogged.cs` (#69) captures every category at Trace, EF Core's SQL included, through a profile's life, and also finds none of its amounts. EF Core's sensitive-data logging stays off; turning it on fails that test. `tests/GestorIA.Api.Tests/TransactionsNotLogged.cs` (#72) does the same through a bank statement's import, re-import, refusal and listing, and finds none of its descriptions or amounts. A refused statement's errors name line numbers and never quote the file.
- **LLM fallback**: off by default; when on, only the OCR raw text of a single document is sent; NIFs/IBANs masked before sending; provider and data-retention terms shown to the user; never images in v1.
- **Retention**: Spanish law requires these records to be kept for years, and the tax prescription period differs from the Código de Comercio period. The tax side is settled: the Administración's right to determine a tax debt prescribes in four years (Ley 58/2003 General Tributaria art. 66.a), counted from the day after the filing period ends (art. 67.1), and the formal obligations tied to it, keeping the documents that justify a return among them, can be demanded until then (art. 70.1); data from a prescribed year that a later return still uses, such as a loss carried forward, must be justified for as long as that later return can be checked (art. 70.3). Read on boe.es, BOE-A-2003-23186, 2026-09-28. Whether the Código de Comercio's six years (art. 30) reach a profesional under IRPF is not verified yet. GestorIA is not where the law requires them to be kept: deleting all data is the user's decision, and the confirmation says to download a copy first and why (#74). Until the commercial side is settled, treat deletion as the dangerous operation, not the missing one. MinIO object versioning is on.
- **Availability**: the engine never reads documents, so MinIO being unreachable cannot block a calculation (ADR-0015).
- **Auth**: local mode API key stored hashed; hosted mode OIDC; rate limiting on uploads; file type sniffing (magic bytes), size limits (a bank statement: 2 MiB, and a ZIP or a file with NUL bytes is refused before parsing, #72), PDF sanitisation (no JS), image re-encoding.
- **Dependencies**: Dependabot/Renovate; `dotnet list package --vulnerable` and `pip-audit` in CI; pinned Docker base images.
- **Secrets**: never in repo; `.env.example` documented; pre-commit secret scan.

## 3. Threats considered
VPS compromise → MinIO holds ciphertext only, and the master key is not on it (ADR-0015). Laptop loss → documents survive in MinIO, and the escrowed key is what makes them readable again. Master key loss → the documents are unrecoverable, which is why escrow is a release blocker. Supply chain → lockfiles and audit. Malicious PDF and SSRF via the LLM provider return with the OCR service in v1.x.

## 4. Acceptance
Checklist reviewed at Phase 6 (result in `reports/audits/`); a documented deletion test (`tests/GestorIA.Api.Tests/ProfileDeletion.cs`, #74: every table of the EF Core model holds a row before `DELETE /profiles/{id}` and none after); PII log test passes.
