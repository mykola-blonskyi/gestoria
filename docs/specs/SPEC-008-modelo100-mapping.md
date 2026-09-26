# SPEC-008 — Modelo 100 Casilla Mapping

**Status:** Draft · **Phase:** 1 · **Theory refs:** §15.3

## 1. Purpose
Map engine aggregates to Renta casilla numbers for the given year so the user can transcribe into Renta WEB (or, later, AEAT XML import).

## 2. Source of truth
`config/tax-years/YYYY.json → casillas`: `{ "<casilla>": "<aggregate path>" }`, e.g. `"0003": "trabajo.ingresos"`, `"0013": "trabajo.ss"`, `"0019": "trabajo.otrosGastos"`, `"0023": "trabajo.reduccion"`, `"0027": "ahorro.intereses"`, `"0029": "ahorro.dividendos"`, `"0090": "inmuebles.imputacion"`, `"0171": "actividad.ingresos"`, `"0186": "actividad.ss"`, `"0223": "actividad.dj"`, `"0224": "actividad.rn"`, `"0435": "bases.big"`, `"0460": "bases.bia"`, `"0500": "bases.blg"`, `"0510": "bases.bla"`, `"0511": "minimo.total"`, `"0545": "cuota.cie"`, `"0546": "cuota.cia"`, `"0596": "pagosACuenta.retTrabajo"`, `"0599": "pagosACuenta.retActividad"`, `"0604": "pagosACuenta.pagos130"`, `"0670": "resultado"`. Ranges (`0187–0200` by expense type, `0564+` regional annex) are expressed as sub-maps keyed by category / rule id.

## 3. Output
```
CasillaSheet { Year, Entries[ { Casilla, Label(es), Value (rounded 2dp), AggregatePath, TraceStepId } ], UnmappedAggregates[] }
```
`UnmappedAggregates` non-empty ⇒ warning (config incomplete for this profile).

## 4. Export
CSV and PDF (Phase 5) grouped by Renta WEB sections; regional annex printed separately with rule titles.

## 5. Acceptance
- Golden case #1 sheet contains 0003=30000.00, 0019=2000.00, 0545+0546=4801.05 (2,463.00 + 2,338.05), and 0670=301.05 on Theory §5.3's retenciones of 4,500.00. Theory §5.3's 4,851.00 and 351.00 apply the state mínimo to the VC scale (#29).
- Every casilla in config resolves to an existing aggregate path (startup check).
