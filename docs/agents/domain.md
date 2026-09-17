# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`knowledge/glossary.md`**: the term list. Code identifiers keep the Spanish tax term (`CuotaIntegra`, `RendimientoNeto`, `Retencion`, `BaseLiquidableGeneral`); comments and docs are English, per `docs/CONVENTIONS.md`.
- **`knowledge/domain-model.md`**: entities and value objects. Where it disagrees with `docs/specs/SPEC-001-domain-model.md`, the spec wins and the summary is stale.
- **`knowledge/business-rules.md`**: the invariants the code must enforce. Some are correct but dormant under the current v1.0 scope and say so inline; dormant is not the same as wrong, and they return with the scope that needs them.
- **`docs/adr/`**: read the ADRs touching the area you are about to work in. `docs/decisions.md` is the index and carries status.

This repo has **no `CONTEXT.md`** and does not want one. The three `knowledge/` files above are its equivalent, and `docs/CONVENTIONS.md` and the project `CLAUDE.md` both name them as the source of truth. Creating a root `CONTEXT.md` would duplicate `knowledge/glossary.md`.

Tax theory, the *why* behind the rules, lives **outside the repo** in the Obsidian vault and is cited as `Theory §x.y`. Never copy theory prose in; a `// Theory §7.3` pointer is the whole convention. Golden test inputs and expected values are the exception, because they are fixture data rather than theory and belong in the repo in full.

If a file you expect is missing, **proceed silently**. Don't flag its absence and don't propose creating it upfront. `/domain-modeling`, reached via `/grill-with-docs` and `/improve-codebase-architecture`, creates these lazily when terms or decisions actually resolve.

## File structure

Single-context repo:

```
/
├── CLAUDE.md
├── knowledge/
│   ├── glossary.md          ← terms
│   ├── domain-model.md      ← entities and value objects
│   └── business-rules.md    ← invariants
├── docs/
│   ├── decisions.md         ← ADR index, with status
│   ├── adr/
│   ├── specs/               ← SPEC-001…013, authoritative over knowledge/
│   └── CONVENTIONS.md
├── config/tax-years/        ← every year-dependent tax value
└── src/
```

## Use the glossary's vocabulary

When your output names a domain concept, in an issue title, a refactor proposal, a hypothesis or a test name, use the term as `knowledge/glossary.md` defines it. Don't drift to synonyms.

In this repo that mostly means **not translating**. Write `CuotaIntegra`, not "gross tax liability"; `RendimientoNeto`, not "net yield". The Spanish terms map one to one onto AEAT form fields and the theory document, and a translation breaks that mapping. The prototype's `NetYield` is the mistake to avoid.

If the concept you need isn't in the glossary, that's a signal: either you're inventing language the project doesn't use, which means reconsider, or there's a real gap, which means note it for `/domain-modeling`.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0011 (the AEAT simulator decides golden values), but worth reopening because…_

**Check the status before citing an ADR.** `docs/decisions.md` carries it, and this repo actually uses the full vocabulary. ADR-0008 is Superseded. ADR-0002, 0005 and 0009 are Proposed, meaning the evidence behind them is not yet in hand. ADR-0013's scope stands while its sequencing clause was amended by ADR-0014. Citing a superseded or proposed ADR as settled is the failure this index exists to prevent.

An ADR here is `Accepted` only once the evidence it rests on is in hand: for a technology choice, the technology has been exercised; for a scope choice, the facts have been stated by the person who owns them. New ADRs use the `Premises` table in `snippets/adr-template.md`, which separates what was stated from what was concluded.
