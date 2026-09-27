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

The table below is an index. It is not the input. Each fixture states its inputs in full, because a golden whose inputs live in the vault is not a regression test: G1's expected 4,801.05 depends on an employee SS contribution of 1,950.00 that this table never mentions.

All cases gate v1.0 (ADR-0013).

| # | Scenario | Expected |
|---|---|---|
| G1 | Employee, 30,000 € gross, SS 1,950.00, VC, no children | cuota íntegra **4,801.05**: 2,463.00 estatal + 2,338.05 VC (#29) |
| G2 | Employee, 18,000 € gross, SS 1,170.00, VC | reducción **3,840.50**, not the full 7,302: LIRPF art. 20 measures 16,830, before the 2,000 of otros gastos (#9). The cuota íntegra of 365.93 in Theory §5.4 rests on the full 7,302 and is to be re-taken from the simulator |
| G3 | Autónomo, income 50,000, expenses 4,000, SS 3,600, VC | cuota íntegra **9,498.05**; Σ 130 = **8,080.00**; result **1,418.05 a ingresar** (#29, #33) |
| G4 | Difícil justificación | previo 42,400 → **2,000**; 10,000 → **500**; −500 → **0** |
| G5 | 130 with loss-making Q3 | Q3 payable **0**, carry-over applied in Q4: Q4 pays **4,052**, Σ 130 = **8,080.00** (#33) |
| G6 | Ahorro 4,500 → **855**; 7,000 → **1,350** | tranche boundary at 6,000 |
| G7 | Minimum, two children (5, 2) | state mínimo: both parents individual → **9,500** each; single filer → **13,450**. VC mínimo for the regional scale: **10,450** each; **14,795** (#29) |
| G8 | Salary 20,000 + autónomo 8,000 | reducción por trabajo = **0** (other income > 6,500); relief lost **1,194.15** (#9) |
| G8b | Salary 20,000 + savings income 8,000 | reducción por trabajo = **0**. Same cap, different route; an employee reaches it through dividends (SPEC-002 step 1) |
| G9 | Bank credit 1,060 from one invoice | base **1,000**, IVA **210**, retención **150** |
| G10 | Obligation | 21,000 single payer → not required; 16,000 + 2,000 → required |
| G11 | Pluriactividad: salary 40,000 (SS 2,600) + established activity 30,000 (RETA 4,802.40), VC | Σ 130 = **4,787.54**; the activity adds **9,234.47**; true-up gap **4,446.93**, payable by 30 June 2026 (#9, #33, #48) |
| G12 | Set-aside estimate in the first month of activity, projection only: alta 15 January, no employment, no activity the year before, projected 30,000 invoiced and 1,200 of expenses besides the RETA cuota, EU and US clients, VC, as of Q1 | hold back **0.1941** of each payment; Q1 Modelo 130 **1,228.99**, due 1–22 April 2025 (the 20th a Sunday, the 21st Easter Monday in VC, #40); TGSS **80.00** a month, 925.33 for the year; true-up gap **0.00**, since the advances of 4,896.19 exceed the 3,365.93 the activity is taxed once LIRPF art. 32.3 takes 20 % off a new activity's first positive period; IVA **0.00** with its reason (#10, #29, #30, #33) |
| G13 | G11's salary plus a new activity in its first positive period: 30,000 to foreign clients, RETA tarifa plana 960, VC | Σ 130 = **5,117.60** on the net before art. 32.3; LIRPF art. 32.3 takes **5,517.60** off the activity net; the activity adds **8,468.86**; gap **3,351.26** (#30, #33, #48) |
| G14 | Set-aside estimate mid-year: G12's taxpayer with Q1 closed at 6,000 invoiced and 345.33 spent, 27,000 and 900 projected for April to December, as of Q2 | Q2 Modelo 130 **1,507.40**, due 1–21 July 2025 (the 20th a Sunday, #40); Σ 130 5,496.59; TGSS 80.00 a month, tarifa plana ending with 2026-01 and **425.85** after it; gap **0.00**; hold back **0.1947** (#15) |
| G15 | Set-aside estimate for pluriactividad: G11's salary plus an established activity, Q1–Q2 actuals of 15,000 and 3,155.10, six General 7 cuotas of 425.85 paid in them, 16,000 and 600 projected, as of Q3 | the year settles on General 6, so the 2,555.10 paid are added back and the six months left priced at **400.20**; Q3 Modelo 130 **1,234.89**; the activity adds 9,096.62; gap **4,376.32**, payable by 30 June 2026; TGSS 4,956.30 for the year; hold back **0.4534** (#15, #39, #48, #52) |
| G16 | Set-aside estimate crossing the other-income cap: G8's salary plus an established activity projected at 12,000, as of Q1 | TGSS **225.75** a month (Reducida 2); activity net 8,826.45 > 6,500, reducción por trabajo **1,194.15** lost and reported; gap **1,305.24**; hold back **0.4483** (#15, #39) |
| G17 | Set-aside estimate with tarifa plana in force: alta 10 June 2024, Q1 actuals, as of Q2 | TGSS **80.00** a month, tarifa plana ending with 2025-06 and **425.85** from 2025-07; Q2 Modelo 130 **1,551.40**; hold back **0.2474** (#15, #39) |
| G18 | G17's taxpayer as of Q3, the quarter after tarifa plana lapses, with Q1–Q2 actuals | TGSS **425.85** a month; Q3 Modelo 130 **1,354.27**; hold back **0.2474** (#15, #39) |
| G19 | Set-aside estimate after a loss-making Q1: 1,000 invoiced against 3,240 spent, as of Q2 | Q1 pays 0 and carries a **−100** result from its minoración; Q2 nets the loss in its cumulative base and deducts the 100: **981.80**; hold back **0.1771** (#15) |
| G21 | G11 in Madrid: salary 40,000 (SS 2,600) + established activity 30,000 (RETA 4,802.40), MD | the activity adds **8,655.00**, 579.47 less than in VC; true-up gap **3,867.46**, payable by 30 June 2026 (#60) |
| G22 | G16 in Madrid: G8's salary plus an established activity projected at 12,000, as of Q1, MD | Q1 Modelo 130 **341.32**, due 1–**21** April 2025 (Easter Monday is not a holiday in Madrid, so a day earlier than G16); reducción por trabajo 1,194.15 lost; gap **1,206.14**; hold back **0.4401** (#60) |

The regional scale is measured against the region's own mínimo personal y familiar (LIRPF art. 56.3 and 74.1). For VC that is Ley 13/1997 art. 2 bis, repeated in the AEAT Manual práctico Renta 2025, cap. 14: a mínimo del contribuyente of 6,105 against the state 5,550, which lowers the VC cuota of any base above 6,105 by 555 × 0.09 = 49.95, and descendientes amounts about 10 % above the state's (#29). Theory §4.4, §5.3, §7.2 and §15.6 apply the state mínimo to both scales, so G1, G3 and G7 above depart from the theory: G1 and G3 by 49.95, G7 by adding the VC figures. In G08, G11 and G13 both the salary-alone and the stacked base are above 6,105, so the 49.95 sits in both cuotas and cancels out of the liability on the activity and the gap. Madrid sets its own amounts too, DL 1/2010 (Madrid) arts. 2 to 2 quater as worded by Ley 13/2023: a mínimo del contribuyente of 5,956.65 (#60). Every base in G21 and G22 is above it, so it cancels the same way.

The VC scale is Ley 13/1997 art. 2.1 as worded by Ley 9/2022 for periods ending from 1 January 2023 (boe.es BOE-A-1998-8202; AEAT Manual práctico Renta 2025, cap. 15). From 42,000 up its rates are 20, 22.5, 25, 26.5, 27.5 and 28.5 %. Theory §4.2 has 19.9, 22.4, 24.9, 26.6, 27.9 and 28.9 %, which no version of the law has, so every golden whose base passes 42,000 departs from the theory: G11 (stacked 59,337.72), G13 (57,470.40) and G15 (59,001.515) (#48, #52). Every other golden base is below 42,000. The Madrid scale is DL 1/2010 (Madrid) art. 1 as worded by Ley 13/2023, for periods starting from 1 January 2023 (boe.es BOCM-m-2010-90068; AEAT Manual práctico Renta 2025, cap. 15), and applies unchanged in 2026 (#60).

The set-aside goldens choose the RETA tramo the way TGSS regularises it, LGSS art. 308.1.c 1.ª–3.ª (#39): Modelo 100 casilla 0224, the activity net after difícil justificación and before any LIRPF art. 32 reduction, plus the titular's own cuotas (casilla 0186), less 7 % gastos genéricos, spread over the months of alta. Casilla 0186 is the cuotas the taxpayer paid for the closed months, stated with the actuals, plus the projected months' cuotas (#52). The projected cuotas and the tramo depend on each other; the goldens take the highest consistent tramo. The year's cuotas SS in the hold-back take the higher of paid and priced for the closed months, over those months together. TGSS keeps what was paid clamped between the cuotas at the tramo's base mínima and base máxima (LGSS art. 308.1.c 3.ª–4.ª), so the higher is exact below the base mínima and between the two bases, and over-reserves above the base máxima, whose refund is not modelled. G11's RETA of 4,802.40 is General 6 under the same rule: (23,937.72 + 4,802.40) × 0.93 / 12 = 2,227.36 a month.

Theory §7.3 Example B leaves difícil justificación out of the Modelo 130 and takes Σ 130 as 20 % × 42,400 = 8,480. Under estimación directa simplificada casilla 02 includes it (RD 439/2007 art. 30.2ª and 110.1.a; AEAT instrucciones del modelo 130, casilla 02), so the legal figure is 20 % × 40,400 = 8,080, and the theory's 8,480 and its 1,068 result are not the legal figures. G3 and G5 keep Example B's inputs and depart from its Modelo 130 figures (#33).

Each golden lives in `tests/golden/2025/G0N.json`, run by `tests/GestorIA.Engine.Tests/Golden/G0N_*.cs`. The fixture holds the inputs as well as the expected values, and the runner builds the engine input from the fixture, so the file alone states the case. Expected values are compared as numbers, not as strings.

A fixture has one object per calculation it checks, and each object carries its own provenance. A golden such as G3 checks the annual cuota íntegra and the four Modelo 130 advances, and the two can rest on different oracles. `tests/golden/2025/G03.json` and `G05.json` are the reference (#8):

```json
{ "golden": "G03",
  "scenario": "Autónomo, income 50,000, expenses 4,000, SS 3,600, VC, foreign clients only. ...",
  "config": "2025.example.json",
  "modelo130": {
    "oracle": "theory",
    "oracleRef": "Arithmetic under SPEC-003 §1, RD 439/2007 art. 30.2ª and 110.1.a and the AEAT instructions ...",
    "oracleRunDate": "2026-09-26",
    "inputs":   { "previousYear": { "rendimientoNeto": "42400.00" }, "retenciones": "foreignPayersOnly", "quarters": [ ... ] },
    "expected": { "quarters": [ ... ], "totalAIngresar": "8080.00" } } }
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
