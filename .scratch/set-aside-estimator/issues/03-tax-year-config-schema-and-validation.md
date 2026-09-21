# 03: Tax-year configuration schema and validation

**Issue:** #4 — https://github.com/mykola-blonskyi/gestoria/issues/4

**What to build:** A tax-year configuration file can be trusted. A JSON Schema says what the file must look like, cross-field rules say what the schema cannot express, and both run in CI so a bad rate is caught by a machine rather than by a wrong tax return months later.

A wrong rate here is indistinguishable from a wrong answer, so this ticket's job is to refuse files rather than to be forgiving. Reading a surviving file into a typed model is #14.

**Blocked by:** 01 (#3) — closed

**Status:** ready-for-agent

**Design plan:** `~/.claude/plans/4-buzzing-tulip.md`

### The schema

- [ ] `config/tax-years/schema.json` exists, JSON Schema 2020-12, and describes the structure in SPEC-007
- [ ] `additionalProperties: false` on every object. A typo'd key such as `escalaEstattal` is an error, not a silently ignored extra
- [ ] `_todo` is declared as an explicit property on each block permitted to be incomplete, **not** matched by a `^_` pattern. `_tood` must still fail
- [ ] A block that does **not** carry `_todo` must be complete: its arrays are `minItems: 1` and its maps `minProperties: 1`, via `if/then`. Removing a `_todo` re-arms the rule automatically
- [ ] `deducciones: []` is exempt from that rule. A year with no configured credits is a legitimate state; an empty regional scale never is
- [ ] Rates constrained to [0,1], money non-negative, `upTo` positive or null, region codes and 4-digit casilla keys by pattern, `sources` non-empty, `schemaVersion` const 1
- [ ] An optional top-level `$schema` property is allowed, so editors can validate the file while it is being typed

### Cross-field rules, in C#

JSON Schema can only see one node and its subtree. Anything comparing two nodes lives in C#, where the failure can say *why* rather than naming a keyword.

- [ ] Scale tranches strictly increasing by `upTo`; exactly one `upTo: null` and it is last
- [ ] Rates within a scale non-decreasing. Not in SPEC-007 today; add it. A Spanish IRPF scale that goes down is always a transcription error
- [ ] Social security bands contiguous: `tramo[i+1].netFrom == tramo[i].netUpTo`, and `baseMin <= baseMax`
- [ ] `deducciones[].casilla` resolves against `casillas` **only for `scope: "estatal"`**. Regional credits use the annex namespace (`B.VC.12`, SPEC-006 §2) and are checked by pattern instead
- [ ] Every `provenance` pointer dereferences to a real node. A dangling pointer is silent rot
- [ ] `taxYear` equals the filename stem; calendar windows are chronological

### Provenance

- [ ] SPEC-007 §1 and §1.1 currently contradict each other: §1 shows bare values, §1.1 a `{value, source, verified}` envelope, and the file follows §1. Resolved as a sibling `provenance` block keyed by JSON Pointer
- [ ] Keys are at the granularity of the **verifiable unit**, not the leaf. One BOE article verifies a whole scale, so this is roughly 20 entries rather than 120
- [ ] `kind` is an enum: `boe | aeat-manual | tgss | published-example | theory`, matching the oracle tiers in SPEC-011 so the two vocabularies stay aligned
- [ ] `verified` is required only when `kind != "theory"`. A theory value was never verified against anything, and demanding a date invents rigour
- [ ] The block is populated and required. Almost every entry is `kind: "theory"` today, which is the honest current state. After this, no number can be added without a source

### Mutation tests

- [ ] One test per criterion, mutating the parsed document **in memory**, never writing a file
- [ ] Cases: rate → 1.5; `sources` → `[]`; `regions` without `VC`; `escalaEstattal`; shuffled tranches; `upTo: null` not last; a gap between social security bands; a dangling provenance pointer; `_todo` removed from a still-empty block
- [ ] Each asserts both that validation fails **and** that the message names the expected JSON Pointer

These are the real defence, not a formality. **JSON Schema ignores unknown keywords rather than rejecting them**, so a typo'd `"mimimum"` silently deletes a rule and everything stays green. The 2020-12 meta-schema permits unknown keywords, so a meta-schema check does not catch it. Only the mutation suite does.

### Known-gaps register

- [ ] A static register asserted **symmetrically**: the set of `_todo` paths in the file must *equal* the registered set
- [ ] Symmetry both ways. A new gap fails until justified; a closed gap fails until its entry is deleted, so gaps cannot quietly outlive the work that closed them
- [ ] Seeded with `/regions/MD/escalaAutonomica`, `/modelo130/minoracion`, `/modelo130/lines`, `/modelo303/lines`, `/seguridadSocial/tramos`, and `/modelo349` + `/deducciones` as deliberately absent

### SPEC-007 gaps closed here

Found by reading the theory doc against the spec. The spec is fixed **before** the schema, so the schema is a transcription rather than a design exercise conducted in JSON.

- [ ] **`modelo130.mortgageRate`, value `0.02`.** Theory §7.3 gives "2% of income, capped at 660.14 € per quarter". The config held only the cap; the 2% is hardcoded in SPEC-003 §1's formula. That is a tax number in code, a direct ADR-0003 violation. Both specs get fixed
- [ ] **`modelo349.periodThreshold`** gains an explicit unit (€ per year, above which filing is monthly). **Value left unset**
- [ ] **`seguridadSocial.tramos[].netFrom`** added. The theory gives a range; the spec kept only the upper bound, which makes band contiguity uncheckable
- [ ] **`seguridadSocial.tarifaPlana.extensionNetIncomeCap`** added for the SMI threshold that gates the 12-month extension. **Value left unset**
- [ ] **`cuotaMinKind`** on each tramo, to separate a planning figure from an exact one. Theory §6.2 says "~205 €" and "trust the amount actually debited"

`periodThreshold` and the SMI cap ship as shape without value on purpose. Both are theory-sourced and unverified, and SPEC-003 §2.1 already says to verify the threshold against AEAT before Q1 2027. Writing an unverified number into a machine-readable config the engine will treat as authoritative is exactly what ADR-0011 exists to prevent.

### Scope corrections

- [ ] SPEC-007 §2's `regions ⊇ {VC, MD}` is stale. ADR-0013 removed Madrid from v1.0, so the schema requires `VC` only
- [ ] SPEC-007 §2 is rewritten into two columns, schema versus C#, with the dividing rule stated: **one node and its subtree → schema; comparing two nodes → C#**
- [ ] "`casillas` values resolve to real engine aggregates" is recorded in SPEC-007 §2 as explicitly **out of this ticket**. It needs an engine model that does not exist. Belongs to #14
- [ ] `plans/current.md` currently reads "Write `config/tax-years/schema.json` (SPEC-007) and a startup validator". Only the first half ships here; split the line and point the rest at #14

### Green

- [ ] `2025.example.json` validates as-is. Every remaining gap is in the register rather than silently tolerated
- [ ] `dotnet build` and `dotnet test` green, 0 warnings
- [ ] Each rule demonstrated failing on purpose once, then restored. A guardrail that is configured but inert is worse than none, because it buys confidence it has not earned (see #18)

## Not in this ticket

Startup validation, the other half of SPEC-007 §2, is #14. Note for whoever picks that up: `GestorIA.Engine` is defined as pure, no I/O, so the loader and its schema dependency belong in Infrastructure or Application, not next to the engine that consumes the config.
