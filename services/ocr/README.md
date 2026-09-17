# gestoria-ocr — document extraction service (Python)

See `docs/specs/SPEC-005-document-ingestion-ocr.md`, ADR-0002 and ADR-0005.

Target layout (Phase 0 skeleton, Phase 4 implementation):

```
services/ocr/
├── pyproject.toml                # python 3.12, fastapi, paddleocr, opencv-python-headless, pydantic, pypdfium2
├── uv.lock
├── Dockerfile                    # models baked in; non-root; healthcheck
├── schema/extraction-result.schema.json   # the contract shared with C#
├── gestoria_ocr/
│   ├── main.py                   # FastAPI app: /health /version /extract
│   ├── pipeline.py               # ingress → text-layer fast path → preprocess → ocr → detect → extract → (llm) → score
│   ├── preprocess.py
│   ├── ocr.py                    # PaddleOCR wrapper (+ tesseract fallback)
│   ├── extractors/{nomina,factura,bank_statement,datos_fiscales,modelo036,rental}.py
│   ├── structuring/{heuristic,llm}.py   # llm prompts live in ../../prompts/ocr/
│   └── models.py                 # Pydantic models generated from schema
├── bench/                        # accuracy benchmark against tests/fixtures/documents; report → reports/audits/
└── tests/
```

Commands: `uv sync`, `uv run uvicorn gestoria_ocr.main:app`, `uv run pytest`, `uv run python -m bench`.
