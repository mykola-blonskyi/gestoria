# SPEC-013 — Security and Privacy

**Status:** Draft · **Phase:** 3, 6

## 1. Data classification
Everything in the ledger and documents is personal financial data (GDPR). NIFs, IBANs, names, salaries are PII.

## 2. Controls
- **At rest**: document blobs encrypted (AES-256-GCM, per-user data key wrapped by a master key from env/KMS); DB volume encrypted at host level; backups encrypted.
- **In transit**: TLS everywhere (Caddy); API↔OCR over private network; OCR service has no public port.
- **Logging**: structured logs with a PII scrubber; never log rawText, amounts with identifiers, or user file names; correlation ids only. Automated test greps log output for NIF/IBAN patterns.
- **LLM fallback**: off by default; when on, only the OCR raw text of a single document is sent; NIFs/IBANs masked before sending; provider and data-retention terms shown to the user; never images in v1.
- **Retention**: user-triggered delete removes ledger, documents, traces and blobs within 24 h; export produces a zip (JSON + original files).
- **Auth**: local mode API key stored hashed; hosted mode OIDC; rate limiting on uploads; file type sniffing (magic bytes), size limits, PDF sanitisation (no JS), image re-encoding.
- **Dependencies**: Dependabot/Renovate; `dotnet list package --vulnerable` and `pip-audit` in CI; pinned Docker base images.
- **Secrets**: never in repo; `.env.example` documented; pre-commit secret scan.

## 3. Threats considered
Malicious PDF upload → sandboxed extraction (separate container, no network egress except API); SSRF via LLM provider URL → allow-list; IDOR → all queries scoped by user id; supply chain → lockfiles + audit.

## 4. Acceptance
Checklist reviewed at Phase 6 (result in `reports/audits/`); a documented deletion test; PII log test passes.
