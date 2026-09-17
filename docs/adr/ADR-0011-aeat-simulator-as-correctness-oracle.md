# ADR-0011: The AEAT simulator decides golden values; the theory document explains them

**Status:** Accepted · **Date:** 2026-09-18

## Context
The ten golden cases in SPEC-011 gate every engine merge. Their expected values come from the theory document in the Obsidian vault, from §15.6 and the worked examples in §5.3, §5.4 and §7.2.

That document was written for this project by its author. So the gate currently asks whether the engine agrees with this project's own reading of Spanish tax law. It does not ask whether the engine agrees with Hacienda. If a rule is misread in the vault, the goldens encode the error and then pass forever, which is the worst failure available to a test suite whose whole job is correctness.

AEAT publishes Renta WEB Open Simulador. It is free, it takes a full profile and it returns official figures. `plans/DEVELOPMENT_PLAN.md` used it once, as a Phase 1 exit audit over three profiles, after the calculators were written.

## Decision
The simulator is the oracle for every golden case it can express, and it runs before the calculator, not after.

Each such case is entered into the simulator by hand. The simulator's output becomes the expected value in `tests/golden/2025/G0N.json`. The fixture records `oracle: aeat-simulator`, the run date and the exact inputs entered.

The theory document keeps its job, which is explaining why a number is what it is. It no longer decides what the number is.

Some cases the simulator cannot express on its own: G4 (difícil justificación in isolation), G5 (Modelo 130 carry-over), G9 (invoice reconstruction) and G10 (filing obligation). Those keep the theory document as their source and are marked `oracle: theory` in the fixture, which flags them as the weaker gate they are.

## Alternatives
- **Theory document as oracle, simulator as a Phase 1 exit audit.** This is what the plan says today. Entering a case by hand costs perhaps an hour, once per tax year. Discovering a wrong constant after four calculators have been built against it costs the rest of the phase.
- **AEAT Manual práctico as oracle.** It is authoritative, but reading a rule out of legal prose is exactly the derivation step that produced the theory document. Repeating that step does not make it independent.

## Consequences
- Phase 1 reorders. Reconciliation moves from the exit criteria to the entry criteria.
- A disagreement between the simulator and the vault is a finding about the vault, and it gets fixed there.
- The new-tax-year runbook (SPEC-007 §4) gains a step: re-run every `aeat-simulator` golden against that year's simulator before touching the expected values.
- The simulator is a website with no API and no stability guarantee. The recorded inputs and run date are what make a golden re-derivable when it changes.
