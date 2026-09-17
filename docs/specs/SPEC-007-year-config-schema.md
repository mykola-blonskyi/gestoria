# SPEC-007 — Tax Year Configuration Schema (`config/tax-years/YYYY.json`)

**Status:** Draft · **Phase:** 0–1 · **ADR:** 0003 · **Theory refs:** every 📅 mark, §4, §5.2, §6.2–6.3, §7.3–7.4, §8.2, §11, §12, §15.3

## 1. Top-level structure
```
{
  "taxYear": 2025,
  "schemaVersion": 1,
  "sources": [ { "ref": "AEAT Manual práctico Renta 2025", "url": "..." } ],
  "irpf": {
    "escalaEstatal":   [ { "upTo": 12450, "rate": 0.095 }, ... , { "upTo": null, "rate": 0.245 } ],
    "escalaAhorro":    { "combined": [ {"upTo": 6000, "rate": 0.19}, ... ], "estatalShare": 0.5 },
    "minimos": { "contribuyente": 5550, "mayor65": 1150, "mayor75": 1400,
                 "descendientes": [2400, 2700, 4000, 4500], "menor3": 2800,
                 "ascendiente": 1150, "ascendiente75": 1400,
                 "discapacidad33": 3000, "discapacidad65": 9000, "asistencia": 3000,
                 "descendienteIncomeCap": 8000, "descendienteAgeCap": 25 },
    "trabajo": { "otrosGastos": 2000, "movilidadExtra": 2000, "discapacidadExtra": 3500, "discapacidadExtra65": 7750,
                 "unionFeeCap": 500,
                 "reduccion": { "fixed": 7302, "t1": 14852, "t2": 17673.52, "t3": 19747.50, "k1": 1.75, "k2": 1.14, "otherIncomeCap": 6500 } },
    "actividad": { "dificilJustificacion": { "pct": 0.05, "max": 2000 }, "homeUtilitiesFactor": 0.30,
                   "retencionProfesional": 0.15, "retencionNuevo": 0.07, "retencionNuevoYears": 3,
                   "depreciation": { "equipos": 4, "software": 3, "mobiliario": 10 } },
    "ahorro": { "lossOffsetPct": 0.25, "carryForwardYears": 4 },
    "inmuebles": { "imputacionPct": 0.011, "imputacionPctRevisado": 0.02, "rentalReductionPct": 0.50, "depreciationPct": 0.03 },
    "reducciones": { "planPensionesCap": 1500, "conjuntaBiparental": 3400, "conjuntaMonoparental": 2150 },
    "obligacion": { "unPagador": 22000, "variosPagadores": 15876, "segundoPagadorMin": 1500, "ahorroCap": 1600, "imputadasCap": 1000 }
  },
  "regions": {
    "VC": { "name": "Comunitat Valenciana", "escalaAutonomica": [ ... ], "minimosOverride": null },
    "MD": { ... }
  },
  "modelo130": { "rate": 0.20, "retencionExemptionShare": 0.70, "mortgageCap": 660.14, "applyDj": false, "minoracion": [ ... ], "lines": { ... } },
  "modelo303": { "lines": { ... } },
  "iva": { "rates": { "general": 0.21, "reducido": 0.10, "superreducido": 0.04 } },
  "seguridadSocial": { "tramos": [ { "name": "Reducida 1", "netUpTo": 670, "baseMin": 653.59, "baseMax": 718.94, "cuotaMin": 205 }, ... ],
                       "tarifaPlana": { "amount": 80, "months": 12, "extensionMonths": 12 } },
  "calendar": { "modelo130": [ ["04-01","04-20"], ["07-01","07-20"], ["10-01","10-20"], ["+1-01-01","+1-01-30"] ],
                "renta": ["+1-04-02","+1-06-30"], "holidays": [ ... ] },
  "casillas": { "0003": "trabajo.ingresos", ... },          // SPEC-008
  "deducciones": [ ... ]                                      // SPEC-006
}
```
Scales use `upTo` (upper bound of tranche, `null` = open) so a tranche's width is derived, avoiding off-by-one edits.

## 2. Validation
JSON Schema at `config/tax-years/schema.json`; validated in CI and at startup. Rules: tranches strictly increasing; rates in [0,1]; regions ⊇ {`VC`,`MD`} for v1; every `deducciones[].casilla` exists in `casillas`; `sources` non-empty.

## 3. Loading
`TaxYearConfigLoader.Load(year)` → immutable `TaxYearConfig` record; SHA-256 of the file stored as `ConfigHash` on every result. Unknown region or year → `ConfigNotFoundException` (never default).

## 4. New year runbook (`docs/runbooks/new-tax-year.md`, Phase 6)
copy → update from BOE/AEAT → bump `sources` → run golden tests (expected to fail) → update goldens with reconciliation against AEAT simulator → PR with diff table of changed values.
