# tests

.NET test projects **and** cross-cutting fixtures live here. The root `README.md` explains how to run the tests and what the golden oracles mean.

## Current

```
tests/
├── GestorIA.Api.Tests/          # the API over HTTP (WebApplicationFactory) on a real PostgreSQL (Testcontainers, Docker required), its OpenAPI document against real requests and answers, the web fixtures (#66, #69)
├── GestorIA.Cli.Tests/          # the console's input parsing and output formatting (#11)
├── GestorIA.Domain.Tests/       # Money and Rate, the double/float ban, the BBVA statement parser and statement line keys
├── GestorIA.Engine.Tests/       # calculator examples, FsCheck properties, config validation and mutations
│   └── Golden/                  # one runner per golden fixture
├── fixtures/bank/               # a synthetic BBVA statement for 2025: made-up names and amounts, and the IBAN ES12 9999 0000 1111 2222 3333, which fails the mod-97 check and names no bank (#72)
└── golden/2025/                 # golden fixtures G03, G05, G08, G11–G22 (SPEC-011): inputs, expected values, oracle
```

## Planned

```
tests/
├── fixtures/
│   ├── bank/                    # more statements, their lines labelled for the classifier (SPEC-004)
│   ├── documents/               # synthetic nóminas/invoices as PDF/JPG for the OCR benchmark (SPEC-005)
│   └── generate.py              # synthetic fixture generator
└── ocr-contract/                # extraction-result JSON samples used by both C# and Python contract tests
```

No real personal data here, ever (SPEC-013). Reconciliation against the AEAT simulator is a report, not a test: `reports/investigations/`.
