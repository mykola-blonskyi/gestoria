# SPEC-006 — Tax Credits (Deducciones) Rule Engine

**Status:** Draft · **Phase:** 2 · **Theory refs:** §11.0–11.2, §14.2, §15.5

## 1. Purpose
Evaluate national and regional credits from data, not code; tell the user which applied, which did not and why, and which are *possible* if documents are supplied. This is where the assistant saves the user the most money (Theory §11.0).

## 2. Rule schema (in `config/tax-years/YYYY.json → deducciones[]`)
```json
{
  "id": "VC-ALQUILER-HABITUAL",
  "scope": "autonomica", "region": "VC",
  "casilla": "B.VC.12",
  "title": { "es": "Deducción por alquiler de vivienda habitual", "en": "...", "ru": "..." },
  "conditions": [
    { "fact": "profile.housing.isRenting", "op": "eq", "value": true },
    { "fact": "profile.ageAt31Dec", "op": "lte", "value": 35, "orAny": ["profile.disabilityDegree >= 33", "profile.familiaNumerosa"] },
    { "fact": "result.sumBases", "op": "lte", "value": 30000, "note": "casillas 0435+0460, individual" },
    { "fact": "documents.rentalContract", "op": "present" },
    { "fact": "documents.fianzaDeposited", "op": "present", "severity": "possible" }
  ],
  "amount": { "type": "percentOfExpense", "expenseFact": "ledger.rentPaidYear", "pct": 0.20, "cap": 800, "capRule": "perTaxpayer" },
  "incompatibleWith": ["ES-ALQUILER-PRE2015"],
  "sources": ["Ley 13/1997 CV art. 4.Uno.n", "AEAT Manual Renta 2025"]
}
```
- `conditions` evaluated against a flat **fact bag** built from profile, ledger aggregates, engine result and document inventory.
- `severity: "possible"` conditions do not reject the credit; they mark it `Possible` with the missing document listed.
- `amount.type` ∈ `fixed | percentOfExpense | percentOfCuota | perDescendant | table`.
- Caps: `cap`, `capRule` (`perTaxpayer | perReturn | shareBetweenParents`).
- Application order: estatal then autonómica; each limited by its cuota part (SPEC-002 step 7).

## 3. Evaluator
```
CreditOutcome { RuleId, Status: Applied | NotApplied | Possible, Amount, FailedConditions[], MissingDocuments[], Explanation }
```
Pure function `(rules, factBag) → outcomes[]`. Facts are typed; unknown fact ⇒ rule evaluation error surfaced as a warning (never silently false).

## 4. v1 rule set
- Estatal (Theory §11.1): maternidad, familia numerosa/ascendiente/descendiente con discapacidad, donativos, alquiler pre-2015, vivienda pre-2013, energy-efficiency works.
- Valencia (§11.2): alquiler vivienda habitual, nacimiento/adopción, gastos guardería, material escolar, energía renovable, inversión en startups — each with sources.
- Madrid: alquiler, nacimiento, gastos educativos — proves the model generalises.

## 5. "Possibly missed" detector
For every rule with status `Possible` or `NotApplied` solely due to missing documents, the explanation layer generates an action ("Upload the fianza deposit certificate — Theory §14.3 template if the landlord refuses"). Sorted by potential amount desc.

## 6. Acceptance
- Unit test per rule: applied case, one test per failing condition, possible-with-missing-doc case.
- Adding a new rule via JSON only; C# unchanged (test loads an extra rule from a fixture).
- Incompatibility matrix enforced.
