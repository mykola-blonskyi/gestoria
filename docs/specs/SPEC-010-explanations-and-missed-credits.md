# SPEC-010 — Explanations, Warnings and "Possibly Missed" Credits

**Status:** Draft · **Phase:** 2 · **Theory refs:** §3.4, §3.6, §6.1, §15.5

## 1. Purpose
Every number must be explainable in plain language, in the user's language (ES/EN/RU), without exposing tax theory text from the vault (only short generated messages).

## 2. Inputs
`CalculationTrace`, `CreditOutcome[]`, `Warning[]`, profile facts.

## 3. Message catalogue (resource files `Explanations.{es,en,ru}.resx`)
Keyed by trace step / warning code; templated with values. Required messages (Theory §15.5):
- `RESULT_A_INGRESAR_WITH_RETENCIONES` — why you owe despite monthly withholding (advance mechanism; two-payer effect; 130 at 20 % vs real rate).
- `REFUND_SMALLER_THAN_EXPECTED` — credit not applied: age / income limit / fianza not lodged.
- `SS_REGULARIZACION_AHEAD` — separate from tax, TGSS not AEAT, estimated amount from tramos vs actual paid.
- `SET_ASIDE_ESTIMATE` — autónomos: ~20 % IRPF + all IVA collected + cuota; foreign-client remote workers 20–30 %.
- `REDUCCION_TRABAJO_LOST` — other income > 6,500 (golden #8), amount of relief lost.
- `MARGINAL_VS_EFFECTIVE` — effective rate shown next to top marginal rate (Theory §3.4).
- `SIMPLIFICADA_RISK` — expenses backed only by simplified invoices.
- `CERTIFICADO_MISMATCH`.

## 4. Structure of the explanation object
```
Explanation { Summary (3–5 sentences), Sections[ { Title, Steps[ { Text, Amount?, TraceStepId } ] } ],
              MissedCredits[ { RuleId, Title, PotentialAmount, MissingDocuments[], NextAction } ],
              Warnings[ { Code, Severity, Text } ], Disclaimer }
```
`NextAction` may reference a template id from Theory §14 (e.g. burofax to landlord) — the SPA links to the vault template outside the repo, or a future in-app template.

## 5. Tone rules
Plain words first, term in parentheses (`"withholding (retención)"`), one idea per sentence, no legal citations in the summary (available in details), amounts always with two decimals and €.

## 6. Acceptance
Snapshot tests of rendered explanations for golden #1, #3, #8 in three languages; reviewer checklist mapped to Theory §15.5.
