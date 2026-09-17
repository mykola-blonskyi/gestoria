# SPEC-005 — Document Ingestion and OCR Service (`gestoria-ocr`)

**Status:** Draft · **Phase:** 4 · **Theory refs:** §5.1, §7.5, §15.1 · **ADRs:** 0002, 0005

## 1. Purpose
Turn PDFs and images into structured, confidence-scored fields; keep humans in the loop for anything uncertain.

## 2. Supported document types (v1)
| `document_type` | Fields extracted | Notes |
|---|---|---|
| `nomina` | period_start, period_end, employer_cif, employee_nif, total_devengado, exentas, en_especie, ss_trabajador, irpf_rate, irpf_amount, liquido, is_extra_pay, atrasos | layout per Orden ESS/2098/2014; many vendor templates |
| `factura` (emitida or recibida) | number, series, issue_date, issuer{nif,name,country}, recipient{nif,name}, lines[], base, iva_rate, iva_amount, retencion_rate, retencion_amount, total, kind (completa/simplificada), exemption_article? | direction decided by comparing NIFs with the profile |
| `certificado_retenciones` | year, employer_cif, totals | |
| `bank_statement_pdf` | rows[{date, value_date, description, amount, balance}] | prefer CSV when the bank offers it |
| `datos_fiscales` | key/value blocks (190, 193, 180, catastro) | text-layer PDF from Renta WEB |
| `modelo_036` | alta_date, iae_code, roi, home_share, criterion | |
| `rental_contract` | address, cadastral_ref, landlord_nif, monthly_rent, start_date | |

## 3. API
`POST /extract` — multipart: `file`, `document_type` (or `auto`), `hints` (JSON: profile NIF, employer CIFs, known clients)
`GET /health`, `GET /version` (model versions, schema version)

Response (`schema/extraction-result.schema.json`, `schemaVersion: 1`):
```json
{
  "schemaVersion": 1,
  "documentType": "factura",
  "detectedType": "factura", "typeConfidence": 0.97,
  "engine": { "ocr": "paddleocr-3.x", "structuring": "heuristic|llm:<model>" },
  "fields": [
    { "name": "base", "value": "1000.00", "confidence": 0.93, "bbox": [0,0,0,0], "page": 1, "source": "ocr" },
    { "name": "retencion_amount", "value": "150.00", "confidence": 0.61, "bbox": [0,0,0,0], "page": 1, "source": "llm" }
  ],
  "tables": [],
  "rawText": "...",
  "warnings": ["simplified_invoice_detected"]
}
```
Amounts are **strings** with 2 decimals; dates ISO-8601. Missing field → omitted, not null.

## 4. Pipeline
1. **Ingress**: type sniffing, page split, size limits (20 MB, 30 pages).
2. **Text-layer fast path**: if PDF has text (pypdfium2/pdfplumber), skip OCR; use layout from the text layer.
3. **Pre-processing** (images/scans): EXIF rotate, deskew, denoise, adaptive threshold.
4. **OCR**: PaddleOCR (det + rec, `lang=es`, angle classifier). Tesseract as fallback engine behind a flag.
5. **Type detection** (if `auto`): keyword/regex scoring (`DEVENGOS`, `LIQUIDO A PERCIBIR`, `FACTURA`, `BASE IMPONIBLE`, `IBAN`…).
6. **Field extraction**: per-type extractor: anchors + regex + spatial rules, NIF checksum validation, arithmetic consistency (`base × (1+iva) − ret ≈ total` boosts confidence; mismatch lowers it and warns).
7. **Optional LLM structuring** (`LLM_STRUCTURING=off|ollama|cloud`): input = rawText of one document; prompt from `prompts/ocr/`; output validated against the schema; disagreements with heuristic values lower confidence of both; never sends images to cloud in v1.
8. **Confidence policy**: per-field score = OCR rec score × consistency factor; `needsReview = any(field.confidence < threshold[type][field])` (defaults 0.85; money fields 0.9).

## 5. Non-functional
- Stateless; no disk persistence beyond the request; temp files shredded.
- p95 latency: text-layer PDF ≤ 2 s, single-page photo ≤ 8 s on CPU.
- Pinned dependencies (`uv.lock`), Docker image with models baked in, healthcheck.
- Benchmark harness `bench/` with labelled fixtures; report to `reports/audits/`.

## 6. C# client (`GestorIA.Infrastructure.Ocr`)
`IDocumentExtractor.ExtractAsync(Stream, DocumentType?, Hints, ct)`; DTOs generated from schema; Polly retry/circuit-breaker; jobs run in a `BackgroundService` consuming a `Channel<ExtractJob>`; results persisted to `SourceDocument.Extraction`; `needsReview` documents go to the queue (SPEC-012).

## 7. Acceptance
Phase-4 exit criteria in `plans/DEVELOPMENT_PLAN.md`; contract test: C# DTOs deserialize every fixture response in `tests/ocr-contract/`; schema drift breaks CI.
