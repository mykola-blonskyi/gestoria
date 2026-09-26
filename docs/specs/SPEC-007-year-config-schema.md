# SPEC-007 — Tax Year Configuration Schema (`config/tax-years/YYYY.json`)

**Status:** Draft · **Phase:** 0–1 · **ADR:** 0003 · **Theory refs:** every 📅 mark, §4, §5.2, §6.2–6.3, §7.3–7.4, §8.2, §11, §12, §15.3

## 1. Top-level structure
```
{
  "taxYear": 2025,
  "schemaVersion": 1,
  "sources": [ { "kind": "aeat-manual", "ref": "AEAT Manual práctico Renta 2025", "url": "..." } ],
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
    "actividad": { "dificilJustificacion": { "pct": 0.05, "max": 2000 },
                   "inicioActividad": { "pct": 0.20, "maxRendimiento": 100000, "formerEmployerShare": 0.50 },
                   "homeUtilitiesFactor": 0.30,
                   "retencionProfesional": 0.15, "retencionNuevo": 0.07, "retencionNuevoYears": 3,
                   "depreciation": { "equipos": 4, "software": 3, "mobiliario": 10 } },
    "ahorro": { "lossOffsetPct": 0.25, "carryForwardYears": 4 },
    "inmuebles": { "imputacionPct": 0.011, "imputacionPctRevisado": 0.02, "rentalReductionPct": 0.50, "depreciationPct": 0.03 },
    "reducciones": { "planPensionesCap": 1500, "conjuntaBiparental": 3400, "conjuntaMonoparental": 2150 },
    "obligacion": { "unPagador": 22000, "variosPagadores": 15876, "segundoPagadorMin": 1500, "ahorroCap": 1600, "imputadasCap": 1000 }
  },
  "regions": {
    "VC": { "name": "Comunitat Valenciana", "escalaAutonomica": [ ... ],
            "minimosOverride": { "contribuyente": 6105, "mayor65": 1265, "mayor75": 1540,
                                 "descendientes": [2640, 2970, 4400, 4950], "menor3": 3080,
                                 "ascendiente": 1265, "ascendiente75": 1540,
                                 "discapacidad33": 3300, "discapacidad65": 9900, "asistencia": 3300 } },
    "MD": { ... }
  },
  "modelo130": { "rate": 0.20, "retencionExemptionShare": 0.70,
                 "mortgageRate": 0.02, "mortgageCap": 660.14,          // cap is per quarter; rate applies to ingresosYTD
                 "minoracion": [ { "netUpTo": ..., "amountPerQuarter": ... }, ... ], "lines": { ... } },
  "modelo303": { "lines": { ... } },
  "modelo349": { "periodThresholdEurPerYear": null,                    // above it, filing turns monthly; UNVERIFIED, see §2.1
                 "lines": { ... } },
  "iva": { "rates": { "general": 0.21, "reducido": 0.10, "superreducido": 0.04 } },
  "seguridadSocial": { "tramos": [ { "name": "Reducida 1", "netFrom": 0, "netUpTo": 670,
                                     "baseMin": 653.59, "baseMax": 718.94,
                                     "cuotaMin": 205, "cuotaMinKind": "approximate" }, ... ],
                       "tarifaPlana": { "amount": 80, "months": 12, "extensionMonths": 12,
                                        "extensionNetIncomeCap": null } },                      // SMI; UNVERIFIED, see §2.1
  "calendar": { "modelo130": [ ["04-01","04-20"], ["07-01","07-20"], ["10-01","10-20"], ["+1-01-01","+1-01-30"] ],
                "renta": ["+1-04-02","+1-06-30"], "holidays": [ ... ] },
  "casillas": { "0003": "trabajo.ingresos", ... },          // SPEC-008
  "deducciones": [ ... ],                                     // SPEC-006
  "provenance": { "/irpf/escalaEstatal": { ... }, ... }        // §1.1
}
```
Scales use `upTo` (upper bound of tranche, `null` = open) so a tranche's width is derived, avoiding off-by-one edits.

`minimosOverride` holds the mínimos the region's own scale is measured against (LIRPF art. 56.3 and 74.1). `null` means the region approved no amounts of its own, and the state `irpf.minimos` apply to its scale as well. Otherwise it carries every amount of `irpf.minimos` except `descendienteIncomeCap` and `descendienteAgeCap`, which are conditions a region cannot change. A partial set is a schema error: taking the missing amounts from the state would mix two laws without saying so. VC's amounts are Ley 13/1997 art. 2 bis (#29). The engine reads only `contribuyente` today; the others wait for the mínimo calculator (golden #7).

Social security bands carry **both** bounds. The theory gives them as a range (§6.2), and keeping only the upper bound makes a gap between two bands undetectable.

`cuotaMinKind` is `approximate | exact`. Theory §6.2 prints these as "~205 €", says sources differ by a few euros, and says to trust the amount actually debited. A planning figure must not be mistaken for a computed one.

## 1.1 Provenance

Every 📅 value records what it was verified against, so that next January it can be re-checked without being re-derived. Provenance lives in **one sibling block**, keyed by a JSON Pointer to the value:

```json
"provenance": {
  "/irpf/escalaEstatal":          { "kind": "boe",    "ref": "Ley 35/2006 art. 63.1, redacción Ley 31/2022", "verified": "2026-09-25" },
  "/irpf/trabajo/reduccion":      { "kind": "aeat-manual", "ref": "Manual Renta 2025, cap. 3", "verified": "2026-09-25" },
  "/regions/VC/escalaAutonomica": { "kind": "theory", "ref": "Theory §4.2, ed. 2" },
  "/seguridadSocial/tramos":      { "kind": "theory", "ref": "Theory §6.2, ed. 2" }
}
```

**Key at the granularity of the verifiable unit, not the leaf.** One BOE article verifies a whole scale, not each tranche. That is roughly twenty entries rather than a hundred and twenty. Values stay scannable in a PR diff, which is half of what ADR-0003 bought by choosing a file over a database, and re-verifying a scale touches one line.

**`kind` is an enum**, not free text: `boe | aeat-manual | tgss | published-example | theory`. The values match the oracle tiers SPEC-011 §1 uses for golden cases, so the project has one vocabulary for "how do we know this number". Comparing free text against the literal `"theory"` is fragile; an enum makes "which values are still unverified?" a mechanical query.

**`verified` is required only when `kind != "theory"`.** A theory-sourced value was never verified against anything, so demanding a date for it invents rigour that does not exist. The theory document is a study aid written for this project, not a legal source.

**The block is required and complete.** A pointer that resolves to no node is an error, and so is a 📅 value with no entry. After that, no number can enter this file without saying where it came from. Most entries read `kind: "theory"` today, which is the honest current state and a work queue: sort by `verified` ascending and the top of the list is what January needs.

An earlier draft of this section wrapped each value as `{value, source, verified}`. That form was rejected. JSON Schema has no generics, so every wrapped type needs its own `$defs` entry, and the file roughly triples in size, which stops it being reviewable. Worse, a re-verification pass would touch `verified` on every value it covers, burying the changed *numbers* among changed *dates* and defeating §4's diff table.

## 2. Validation

JSON Schema at `config/tax-years/schema.json`, applied in CI (issue #4) and at startup (issue #14).

**The dividing rule: if a rule can be decided by looking at one node and its own subtree, it belongs in the schema. If it requires comparing two nodes to each other, it belongs in C#.**

That line is not stylistic. JSON Schema can express some ordering constraints through `allOf`/`contains` gymnastics, but the result hardcodes array length and produces a failure that names a keyword and an instance location without saying *why*. "`/irpf/escalaEstatal/3` failed `contains`" tells the reader nothing. C# can say "tranche 3 (`upTo` 35200) is not greater than tranche 2 (`upTo` 60000)". In a file where a wrong value is indistinguishable from a wrong tax return, the message matters as much as the refusal.

### In the schema

| Rule | Mechanism |
|---|---|
| Every block's key set, nothing extra | `required` + `additionalProperties: false` on every object |
| Rates in [0,1] | `$defs/rate` |
| Money non-negative | `$defs/money` |
| `upTo` positive or `null` | `{"type": ["number","null"], "exclusiveMinimum": 0}` |
| `regions` contains `VC` | `"regions": { "required": ["VC"] }` |
| Region codes are ISO 3166-2:ES-shaped | `propertyNames` pattern |
| Casilla keys are four digits | `propertyNames` pattern |
| `sources` non-empty, each `ref` non-empty | `minItems` + `minLength` |
| `schemaVersion` is 1 | `{"const": 1}` |
| Calendar tokens and windows | `$defs/calendarDay` + `prefixItems` |
| No duplicate tranches | `uniqueItems` |
| `verified` required unless `kind: "theory"` | `if/then` inside `$defs/provenanceEntry` |
| A block without `_todo` must be complete | `if/then` per block, see below |

### In C#

| Rule | Why not the schema |
|---|---|
| Tranches strictly increasing by `upTo` | pairwise comparison across elements |
| Exactly one `upTo: null`, and it is last | position and cardinality across elements |
| Rates within a scale non-decreasing | pairwise; a Spanish IRPF scale that descends is always a transcription error |
| Social security bands contiguous (`tramo[i+1].netFrom == tramo[i].netUpTo`), `baseMin <= baseMax` | pairwise |
| `deducciones[].casilla` resolves, **for `scope: "estatal"` only** | cross-node reference; see below |
| Every `provenance` pointer dereferences to a real node | pointer resolution; a dangling pointer is silent rot |
| `taxYear` equals the filename stem | the filename is outside the document |
| Calendar windows chronological, `start <= end` | pairwise |

**Casilla namespaces are not one namespace.** SPEC-008 §2's `casillas` keys are four-digit Modelo 100 fields. SPEC-006 §2's regional credits carry annex identifiers such as `B.VC.12`. So "every `deducciones[].casilla` exists in `casillas`", as earlier drafts of this section said, is false: it would reject the first regional credit anyone adds. The check splits by `scope` — `estatal` resolves against `casillas`, `autonomica` matches the annex pattern.

### Declared incompleteness

A block that does **not** carry a `_todo` note must be complete: its arrays carry at least one item and its maps at least one key, expressed as `if/then` on the presence of `_todo`. Removing the note re-arms the rule automatically, which is the behaviour wanted when someone fills a block in.

`_todo` is declared as an explicit property wherever it is permitted, not matched by a `^_` pattern. A pattern would let `_tood` through silently, and catching typo'd keys is the main thing this schema is for.

`deducciones: []` is exempt. A year with no configured credits is a legitimate state (SPEC-006 is Phase 2); an empty regional scale never is.

### Out of scope here

`casillas` values are aggregate paths such as `trabajo.ingresos`. Checking that each one resolves to a real engine aggregate needs a model of the engine, which does not exist yet. The typed loader (issue #14) does not do it either: it does not map `casillas`, because nothing reads them until the Modelo 100 mapper. The check lands with that mapper, as SPEC-008 §5 specifies. It is named here so it is not mistaken for something this validation already covers.

### Unverified values

`modelo349.periodThresholdEurPerYear` and `seguridadSocial.tarifaPlana.extensionNetIncomeCap` are declared with `null`. Their shape is fixed; their values are not known to anyone here. Both come from the theory document and neither has been checked against AEAT or TGSS — SPEC-003 §2.1 already carries that as an open task before Q1 2027.

Writing an unverified number into a file the engine treats as authoritative converts a guess into an apparent fact, which is precisely the distinction ADR-0011 draws for golden values. A `null` is honest; a plausible number is not.

## 3. Loading
`TaxYearConfigLoader.Load(year)` → immutable `TaxYearConfig` record; SHA-256 of the file stored as `ConfigHash` on every result. Unknown region or year → `ConfigNotFoundException` (never default).

A region block carrying `_todo` has declared itself unusable, so asking for it raises `ConfigNotFoundException` quoting the note, exactly as an absent region does. Computing with its empty scale would be the silent default this section forbids.

**The loader does not live in `GestorIA.Engine`.** The engine is pure by definition, no I/O and no file system (`docs/architecture.md`), so reading a file and evaluating a schema at startup belongs in Infrastructure or Application. The instinct is to put the loader next to the engine that consumes its output; resist it, or the engine's purity becomes a comment rather than a property.

## 4. New year runbook (`docs/runbooks/new-tax-year.md`, Phase 6)
copy → update from BOE/AEAT → update the `provenance` entry for every value touched, including `verified` → run golden tests (expected to fail) → update goldens with reconciliation against AEAT simulator → PR with a diff table of changed values.

Sorting `provenance` by `verified` ascending gives the work queue. Entries still reading `kind: "theory"` have never been checked against a legal source and are the ones to start with.
