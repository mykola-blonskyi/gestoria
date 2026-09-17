# ADR-0012: v1.0 computes Modelo 100 for one employee in Valencia

**Status:** Superseded by [ADR-0013](ADR-0013-v1-scope-employee-and-autonomo.md) · **Date:** 2026-09-18

> Superseded the same day. The decision rested on an inference the author never made: that because he is not an autónomo today, the autónomo domain has no user. Autónomo scope is restored in ADR-0013.
> The record is kept because the OCR and Madrid cuts below survived, and because the failure mode is worth remembering.

## Context
The project was specced for "Spanish residents (employees and *autónomos* under *estimación directa simplificada*)". That produced thirteen specs, six phases and a 31-week plan.

Two facts have since been settled. ADR-0010 established that v1.0 has one user, the author, on his own machine. He is an employee, not an autónomo, and the first filing this tool is used for is Renta 2026, due between April and June 2027.

An employee does not file Modelo 130 or Modelo 303. Both obligations arise from economic activity. So about half the specced domain describes filings this user will never make, and the plan spends Phase 1 and much of Phase 4 building them.

Today is 2026-09-18. The deadline is roughly 30 weeks out and the plan is 31 weeks long, so it has no slack to spend on work nobody will run.

## Decision
v1.0 computes **Modelo 100 for one employee resident in Valencia**.

In scope: employment income, savings income, the personal and family minimum, the state and Valencia scales, the casilla sheet, the calculation trace, the explanations, and the credits engine. Data entry is manual, typed from nóminas, the certificado de retenciones and the AEAT datos fiscales.

Out of v1.0 and moved to backlog: Modelo 130, Modelo 303, activity income and every autónomo concept, the OCR service, Madrid, and the expense side of the bank transaction classifier.

## Alternatives
- **Keep autónomo support in case the author registers as one.** Speculative. It carries SPEC-003 in full, activity income, issued and received invoices, difícil justificación, the SS tramos table and the classifier's expense rules. None of it is load-bearing for Modelo 100, and all of it is additive later.
- **Keep OCR so data entry is automatic.** About a dozen documents a year, against the longest phase in the plan, written in a language the author is still learning. One year of typing teaches you what extraction has to produce.
- **Keep Madrid to prove the region model generalises.** A fake region in a test fixture proves it just as well and costs an afternoon rather than a research task against BOE.

## Consequences
- All four `_todo` blocks in `config/tax-years/2025.example.json` disappear. Madrid goes by scope; the Modelo 130 and 303 line numbers and the autónomo SS tramos go with the forms. The config is complete for what v1.0 computes, which clears the largest item in Phase 0.
- Six goldens gate v1.0: G1, G2, G6, G7, G8 and G10. G3, G4, G5 and G9 stay in SPEC-011 as the v1.x corpus and stop being gates.
- **G8 must be restated.** It currently reaches the 6,500 € other-income cap through autónomo income. The cap counts savings gross and rental net too (SPEC-002 step 1), so an employee with dividends can lose the entire `ReduccionTrabajo`. That is exactly the trap this tool exists to catch, so the rule stays and the scenario changes.
- Phase 1 loses `ActivityIncomeCalculator`, `Modelo130Calculator` and `Modelo303Calculator`. Phase 4 leaves v1.0 entirely.
- ADR-0002, ADR-0005 and ADR-0009 now describe backlog work. They stay Proposed.
- SPEC-003, SPEC-004 and SPEC-005 keep their numbers and move to Deferred. `docs/CONVENTIONS.md` forbids renumbering.
- The first production config is `2026.json`. `2025.json` stays as the test corpus, because the goldens are 2025 cases.
- Revisit the moment the author registers as an autónomo. The engine boundary does not change, so re-entry is additive.
