# ADR-0002: Separate Python service for OCR and ML

**Status:** Proposed · **Date:** 2026-09-17

> Demoted from Accepted on 2026-09-18. Nothing has been extracted yet.
> Accept once the Phase 4 benchmark shows PaddleOCR meeting the field-accuracy
> targets in `plans/DEVELOPMENT_PLAN.md`, or reopen if it does not.

## Context
Nóminas and supplier invoices arrive as PDFs and phone photos. Extraction quality directly determines calculation quality ("no expense without an invoice", Theory §15.2). Options: Tesseract in-process via C# wrapper; cloud document AI (Azure/Google/AWS, free tiers); Python service with PaddleOCR/docTR; LLM-vision only.

## Decision
Run a dedicated Python 3.12 / FastAPI service (`services/ocr`) using PaddleOCR (multilingual, Spanish) with OpenCV pre-processing and layout heuristics, exposing `POST /extract`. LLM structuring is an optional, feature-flagged stage. C# never embeds Tesseract as the primary path.

## Alternatives considered
| Option | Quality on photos | Ops complexity | Cost | Vendor lock-in |
|---|---|---|---|---|
| Tesseract in C# | low–medium | lowest | free | none |
| Cloud document AI | high | low | free tier then paid | high |
| Python + PaddleOCR/docTR | high | medium (2nd runtime) | free | none |
| LLM-vision only | medium–high, variable | low | free tier / GPU | medium |

## Trade-offs
Quality is the product requirement and the author is willing to learn Python, so the second runtime is accepted. Cost: separate Dockerfile, CI job, dependency pinning, health checks, and a JSON-Schema contract (ADR-0005). Mitigation: the service is stateless, tiny in surface (one endpoint + health), and pinned by lockfile; C# wraps calls in Polly retry/circuit-breaker and queues work so the API never blocks on it.

## Consequences
- Extraction accuracy is benchmarked (`services/ocr/bench`, report in `reports/audits/`) and gates Phase 4.
- Any LLM stage validates output against a strict JSON Schema; failures fall back to heuristic extraction + user review.
- Tesseract may still be used as an offline fallback engine inside the Python service, never as the primary.
