# SPEC-011 — Golden Test Cases and Testing Strategy

**Status:** Draft · **Phase:** 1+ · **Theory refs:** §15.6 (all ten cases), §4.4, §5.3–5.4, §7.2–7.4

## 1. Golden cases (must pass to the cent; figures from Theory §15.6, cross-checked there by a separate script)

| # | Scenario | Expected |
|---|---|---|
| G1 | Employee, 30,000 € gross, VC, no children | cuota íntegra **4,851.00** |
| G2 | Employee, 18,000 € gross | full reducción 7,302 applied; cuota íntegra **365.93** |
| G3 | Autónomo, income 50,000, expenses 4,000, SS 3,600, VC | cuota íntegra **9,548.00**; Σ 130 = **8,480.00**; result **1,068.00 a ingresar** |
| G4 | Difícil justificación | previo 42,400 → **2,000**; 10,000 → **500**; −500 → **0** |
| G5 | 130 with loss-making Q3 | Q3 payable **0**, carry-over applied in Q4 |
| G6 | Ahorro 4,500 → **855**; 7,000 → **1,350** | tranche boundary at 6,000 |
| G7 | Minimum, two children (5, 2) | both parents individual → **9,500** each; single filer → **13,450** |
| G8 | Salary 20,000 + autónomo 8,000 | reducción por trabajo = **0** (other income > 6,500) |
| G9 | Bank credit 1,060 from one invoice | base **1,000**, IVA **210**, retención **150** |
| G10 | Obligation | 21,000 single payer → not required; 16,000 + 2,000 → required |

Each golden lives in `tests/GestorIA.Engine.Tests/Golden/G0N_*.cs` with the input built by a fluent test builder and the expected trace stored as a JSON snapshot in `tests/golden/2025/G0N.json` (asserted with a semantic diff, not string equality).

## 2. Additional test layers
- **Property-based** (FsCheck): scale monotonicity/continuity; `Money` arithmetic; classifier never assigns `DEDUCTIBLE_EXPENSE` without invoice link.
- **Reconciliation**: `reports/investigations/2025-renta-reconciliation.md` — ≥ 3 profiles entered into AEAT Renta WEB Open Simulador; document inputs, official output, engine output, delta, cause.
- **Contract**: OCR schema fixtures (`tests/ocr-contract/`) deserialize in C#; Pydantic models validate the same fixtures.
- **Integration**: API end-to-end (Testcontainers Postgres, stubbed OCR).
- **OCR benchmark**: field accuracy per document type (SPEC-005 §5).
- **Mutation**: Stryker.NET ≥ 80 % on `GestorIA.Engine`.

## 3. Fixtures policy
All sample documents anonymised (fake NIFs with valid checksums, fake names, real layouts). No real personal data in the repo, ever. Generator script `tests/fixtures/generate.py` produces synthetic nóminas/invoices as PDFs for the OCR benchmark.

## 4. Existing prototype tests
`tests/GestorIA.Domain.Tests/IrpfCalculatorTests.cs` and `BbvaParserTests.cs` test the prototype; keep them green until the goldens pass on the new engine, then retire `IrpfCalculatorTests` and port `BbvaParserTests` to the new `BankTransaction` model.

## 5. Year change
When `config/tax-years/2026.json` lands, goldens are re-derived under `tests/golden/2026/`; 2025 goldens remain and keep passing with the 2025 config.
