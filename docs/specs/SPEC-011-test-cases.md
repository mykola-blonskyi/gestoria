# SPEC-011 — Golden Test Cases and Testing Strategy

**Status:** Draft · **Phase:** 1+ · **Theory refs:** §15.6 (all ten cases), §4.4, §5.3–5.4, §7.2–7.4

## 1. Golden cases (must pass to the cent)

Expected values come from the AEAT Renta WEB Open Simulador, entered by hand before the calculator is written (ADR-0011). Where the simulator cannot express a case, fall to the next tier and record which one the fixture used.

| Tier | `oracle` | Use when |
|---|---|---|
| 1 | `aeat-simulator` | The simulator can express the case. Modelo 100 scenarios |
| 2 | `published-example` | A worked example from AEAT, a gestoría or a published manual. Cite it. Modelo 130 and 303 have no simulator |
| 3 | `theory` | Nothing better exists. The weakest gate, and marked as such |

The table below is an index. It is not the input. Each fixture states its inputs in full, because a golden whose inputs live in the vault is not a regression test: G1's expected 4,851.00 depends on an employee SS contribution of 1,950.00 that this table never mentions.

All cases gate v1.0 (ADR-0013).

| # | Scenario | Expected |
|---|---|---|
| G1 | Employee, 30,000 € gross, SS 1,950.00, VC, no children | cuota íntegra **4,851.00** |
| G2 | Employee, 18,000 € gross, SS 1,170.00, VC | full reducción 7,302 applied; cuota íntegra **365.93** |
| G3 | Autónomo, income 50,000, expenses 4,000, SS 3,600, VC | cuota íntegra **9,548.00**; Σ 130 = **8,480.00**; result **1,068.00 a ingresar** |
| G4 | Difícil justificación | previo 42,400 → **2,000**; 10,000 → **500**; −500 → **0** |
| G5 | 130 with loss-making Q3 | Q3 payable **0**, carry-over applied in Q4 |
| G6 | Ahorro 4,500 → **855**; 7,000 → **1,350** | tranche boundary at 6,000 |
| G7 | Minimum, two children (5, 2) | both parents individual → **9,500** each; single filer → **13,450** |
| G8 | Salary 20,000 + autónomo 8,000 | reducción por trabajo = **0** (other income > 6,500) |
| G8b | Salary 20,000 + savings income 8,000 | reducción por trabajo = **0**. Same cap, different route; an employee reaches it through dividends (SPEC-002 step 1) |
| G9 | Bank credit 1,060 from one invoice | base **1,000**, IVA **210**, retención **150** |
| G10 | Obligation | 21,000 single payer → not required; 16,000 + 2,000 → required |

Each golden lives in `tests/GestorIA.Engine.Tests/Golden/G0N_*.cs` with the input built by a fluent test builder and the expected trace stored as a JSON snapshot in `tests/golden/2025/G0N.json` (asserted with a semantic diff, not string equality).

Every fixture carries its provenance:

```json
{ "oracle": "aeat-simulator", "oracleRunDate": "2026-09-25",
  "inputs": { "grossSalary": "30000.00", "ssTrabajador": "1950.00", "region": "VC", ... },
  "expected": { "cuotaIntegra": "4851.00", ... } }
```

`oracle` is one of the three tiers above, and the fixture also records `oracleRef` for a `published-example`. G5 and G9 cannot use the simulator at all; G4 and G10 are components rather than whole returns. Those four are the cases to hunt published examples for.

## 2. Additional test layers
- **Property-based** (FsCheck): scale monotonicity/continuity; `Money` arithmetic; classifier never assigns `DEDUCTIBLE_EXPENSE` without invoice link.
- **Reconciliation**: `reports/investigations/2025-renta-reconciliation.md` — ≥ 3 profiles entered into AEAT Renta WEB Open Simulador; document inputs, official output, engine output, delta, cause.
- **Contract**: OCR schema fixtures (`tests/ocr-contract/`) deserialize in C#; Pydantic models validate the same fixtures.
- **Integration**: API end-to-end (Testcontainers Postgres, stubbed OCR).
- **OCR benchmark**: field accuracy per document type (SPEC-005 §5).
- **Mutation**: Stryker.NET ≥ 80 % on `GestorIA.Engine`.

## 3. Fixtures policy
All sample documents anonymised (fake NIFs with valid checksums, fake names, real layouts). No real personal data in the repo, ever. Generator script `tests/fixtures/generate.py` produces synthetic nóminas/invoices as PDFs for the OCR benchmark.

## 4. Prototype tests
`IrpfCalculatorTests.cs` was deleted on 2026-09-18 together with the calculator it tested. `BbvaParserTests.cs` survives and stays green; port it to the new `BankTransaction` model in Phase 4 (SPEC-004 §5).

## 5. Year change
When `config/tax-years/2026.json` lands, goldens are re-derived under `tests/golden/2026/`; 2025 goldens remain and keep passing with the 2025 config.
