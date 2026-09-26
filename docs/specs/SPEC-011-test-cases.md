# SPEC-011 — Golden Test Cases and Testing Strategy

**Status:** Draft · **Phase:** 1+ · **Theory refs:** §15.6 (all ten cases), §4.4, §5.3–5.4, §7.2–7.4

## 1. Golden cases (must pass to the cent)

Expected values come from the AEAT Renta WEB Open Simulador, entered by hand before the calculator is written (ADR-0011). Where the simulator cannot express a case, fall to the next tier and record which one the fixture used.

| Tier | `oracle` | Use when |
|---|---|---|
| 1 | `gestor-prepared` | A professional prepared the same period from the same facts. Strongest available for 130, 303 and 349, because it covers this taxpayer's own combination rather than a textbook's |
| 1 | `aeat-simulator` | The simulator can express the case. Modelo 100 scenarios |
| 2 | `published-example` | A worked example from AEAT or a published manual. Cite it |
| 3 | `theory` | Nothing better exists. The weakest gate, and marked as such |

The table below is an index. It is not the input. Each fixture states its inputs in full, because a golden whose inputs live in the vault is not a regression test: G1's expected 4,851.00 depends on an employee SS contribution of 1,950.00 that this table never mentions.

All cases gate v1.0 (ADR-0013).

| # | Scenario | Expected |
|---|---|---|
| G1 | Employee, 30,000 € gross, SS 1,950.00, VC, no children | cuota íntegra **4,851.00** |
| G2 | Employee, 18,000 € gross, SS 1,170.00, VC | reducción **3,840.50**, not the full 7,302: LIRPF art. 20 measures 16,830, before the 2,000 of otros gastos (#9). The cuota íntegra of 365.93 in Theory §5.4 rests on the full 7,302 and is to be re-taken from the simulator |
| G3 | Autónomo, income 50,000, expenses 4,000, SS 3,600, VC | cuota íntegra **9,548.00**; Σ 130 = **8,480.00**; result **1,068.00 a ingresar** |
| G4 | Difícil justificación | previo 42,400 → **2,000**; 10,000 → **500**; −500 → **0** |
| G5 | 130 with loss-making Q3 | Q3 payable **0**, carry-over applied in Q4 |
| G6 | Ahorro 4,500 → **855**; 7,000 → **1,350** | tranche boundary at 6,000 |
| G7 | Minimum, two children (5, 2) | both parents individual → **9,500** each; single filer → **13,450** |
| G8 | Salary 20,000 + autónomo 8,000 | reducción por trabajo = **0** (other income > 6,500); relief lost **1,194.15** (#9) |
| G8b | Salary 20,000 + savings income 8,000 | reducción por trabajo = **0**. Same cap, different route; an employee reaches it through dividends (SPEC-002 step 1) |
| G9 | Bank credit 1,060 from one invoice | base **1,000**, IVA **210**, retención **150** |
| G10 | Obligation | 21,000 single payer → not required; 16,000 + 2,000 → required |
| G11 | Pluriactividad: salary 40,000 (SS 2,600) + established activity 30,000 (RETA 4,802.40), VC | Σ 130 = **5,039.52**; the activity adds **9,217.13**; true-up gap **4,177.61**, payable by 30 June 2026 (#9) |
| G12 | Set-aside estimate in the first month of activity, projection only: alta 15 January, no employment, no activity the year before, projected 30,000 invoiced and 1,200 of expenses besides the RETA cuota, EU and US clients, VC, as of Q1 | hold back **0.2034** of each payment; Q1 Modelo 130 **1,298.93**, due 1–20 April; TGSS **80.00** a month, 925.33 for the year; true-up gap **0.00**, since the advances of 5,174.93 exceed the 4,980.28 the activity is taxed; IVA **0.00** with its reason (#10) |

G1, G3, G8, G11 and G12 apply the state mínimo of 5,550 to the regional scale as well, because the configuration holds no regional mínimos (#29). The AEAT Manual práctico Renta 2025, cap. 14, gives VC residents a regional mínimo of 6,105 (Ley 13/1997 art. 2 bis), which lowers the regional cuota of any base above it by 49.95. G1's cuota íntegra and G3's cuota, liability and gap overstate the tax by that amount. In G08 and G11 both the salary-alone and the stacked base are above 6,105, so the same 49.95 sits in both cuotas and cancels out of the liability on the activity and the gap. G12 uses the state mínimo too; its liability overstates by the same 49.95, but the advances exceed it either way, so its gap and hold-back share do not move.

Each golden lives in `tests/golden/2025/G0N.json`, run by `tests/GestorIA.Engine.Tests/Golden/G0N_*.cs`. The fixture holds the inputs as well as the expected values, and the runner builds the engine input from the fixture, so the file alone states the case. Expected values are compared as numbers, not as strings.

A fixture has one object per calculation it checks, and each object carries its own provenance. A golden such as G3 checks the annual cuota íntegra and the four Modelo 130 advances, and the two can rest on different oracles. `tests/golden/2025/G03.json` and `G05.json` are the reference (#8):

```json
{ "golden": "G03",
  "scenario": "Autónomo, income 50,000, expenses 4,000, SS 3,600, VC, foreign clients only. ...",
  "config": "2025.example.json",
  "modelo130": {
    "oracle": "theory",
    "oracleRef": "Theory §7.3, ed. 2: Example B quarter by quarter ...",
    "oracleRunDate": "2026-09-26",
    "inputs":   { "previousYear": { "rendimientoNeto": "42400.00" }, "retenciones": "foreignPayersOnly", "quarters": [ ... ] },
    "expected": { "quarters": [ ... ], "totalAIngresar": "8480.00" } } }
```

Amounts are strings with two decimals. `oracle` is one of the tiers above. `oracleRef` is always recorded and names the exact source: the spec or theory section for `theory`, the document and page or URL for `published-example`. The runner fails a fixture that lacks `oracle`, `oracleRef` or a valid `oracleRunDate`. G5 and G9 cannot use the simulator at all; G4 and G10 are components rather than whole returns. Those four are the cases to hunt published examples for.

## 2. Additional test layers
- **Property-based** (FsCheck): scale monotonicity/continuity; `Money` arithmetic; classifier never assigns `DEDUCTIBLE_EXPENSE` without invoice link.
- **Reconciliation**: `reports/investigations/2025-renta-reconciliation.md` — ≥ 3 profiles entered into AEAT Renta WEB Open Simulador; document inputs, official output, engine output, delta, cause.
- **Q1 2027 gestor reconciliation**: a gestor prepares Modelo 130, 303 and 349 for Q1 2027 from the same facts. The engine runs the same period in parallel and every line is diffed. Q1 is **filed from the gestor's numbers**; the engine's first self-filed quarter is Q2 2027, due 20 July. The diff becomes `gestor-prepared` goldens and the report goes to `reports/investigations/2027-q1-gestor-reconciliation.md`.
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
