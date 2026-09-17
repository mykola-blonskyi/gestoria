# tests

.NET test projects **and** cross-cutting fixtures live here.

```
tests/
├── GestorIA.Domain.Tests/               # BbvaParserTests; port to BankTransaction in Phase 4 (SPEC-004 §5)
├── GestorIA.Engine.Tests/               # Phase 1: golden + property tests (SPEC-011)
│   └── Golden/G01_*.cs … G10_*.cs
├── GestorIA.Api.Tests/                  # Phase 3: integration tests (Testcontainers Postgres)
├── golden/2025/G01.json … G10.json      # expected traces (semantic snapshots)
├── fixtures/
│   ├── bank/                            # anonymised statement lines, labelled (SPEC-004)
│   ├── documents/                       # synthetic nóminas/invoices as PDF/JPG for the OCR benchmark (SPEC-005)
│   └── generate.py                      # synthetic fixture generator
└── ocr-contract/                        # extraction-result JSON samples used by both C# and Python contract tests
```

No real personal data here, ever (SPEC-013). Reconciliation against the AEAT simulator is a report, not a test: `reports/investigations/`.
