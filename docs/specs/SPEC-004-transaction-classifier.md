# SPEC-004 — Bank Transaction Classifier and Invoice Matcher


**Status:** Draft · **Phase:** 4 · **Theory refs:** §7.4, §9, §15.1, §15.2

## 1. Purpose
First automatic pass over bank statement lines: assign a class, link supporting documents, never guess when unsure. Replaces the prototype's `IsDeductible` flag and sign-derived `TransactionType`.

## 2. Classes
`ACTIVITY_INCOME · EMPLOYMENT_INCOME · SAVINGS_INCOME · SOCIAL_SECURITY · AEAT_PAYMENT · DEDUCTIBLE_EXPENSE · HOME_EXPENSE · PERSONAL · OWN_TRANSFER · UNCLEAR`

Each classification carries `Confidence (0–1)`, `RuleId`, `Evidence[]` and, where applicable, `LinkedDocumentId`.

## 3. Rule order (first match wins; the matcher runs first for credits)
1. **Invoice matcher** (credits only): for each unmatched `FacturaEmitida`, compute expected `Total` (SPEC-003 §3) and match by amount ±0.01 within ±45 days of issue; tie-break by counterparty similarity (Jaro-Winkler ≥ 0.85) and client NIF in description → `ACTIVITY_INCOME`, linked.
2. Counterparty/description contains `TGSS|SEGURIDAD SOCIAL|CUOTA AUTONOM` → `SOCIAL_SECURITY`.
3. `AEAT|AGENCIA TRIBUTARIA|HACIENDA|MODELO 130|MODELO 303|MOD.130` → `AEAT_PAYMENT` with `FormHint` (130/303/100).
4. Credit with employer CIF from profile or `NOMINA|SALARIO` → `EMPLOYMENT_INCOME`; amount cross-checked against nómina `Liquido` (±1 €) → link.
5. `INTERESES|DIVIDENDO|CUPON|ABONO DIVIDEND` → `SAVINGS_INCOME`.
6. Transfers where counterparty IBAN ∈ profile's own accounts, or Bizum from contacts marked personal → `OWN_TRANSFER`.
7. Debit to a known business vendor (`config/vendors.json`: Google, Adobe, GitHub, OpenAI, Amazon Business, hosting…) → `DEDUCTIBLE_EXPENSE` **candidate**; `NeedsInvoice` until a `FacturaRecibida` is linked.
8. Utilities (`IBERDROLA|ENDESA|NATURGY|DIGI|MOVISTAR|VODAFONE|AGUA`…) → `HOME_EXPENSE` candidate with computed share `HomeOfficeShare × homeUtilitiesFactor`; also `NeedsInvoice`.
9. Supermarkets, restaurants (unless tagged "client meeting"), clothing, gym, streaming → `PERSONAL`.
10. Otherwise → `UNCLEAR` → review queue with a direct question.

The "no expense without invoice" rule is enforced structurally: `DEDUCTIBLE_EXPENSE`/`HOME_EXPENSE` contribute to the ledger only when `LinkedDocumentId != null` and the document is `Confirmed`.

## 4. Learning from confirmations
User corrections are stored as `ClassificationFeedback { pattern, class }` and become per-user rules evaluated before step 7. No ML in v1.

## 5. Input adapters
`IStatementParser` (existing interface in `Domain/Interfaces`) implementations per bank: BBVA CSV/XLSX (existing `BbvaCsvStatementParser` to be adapted to the new `BankTransaction`), Sabadell, Revolut, Wise, generic CSV with column mapping. PDF statements go through SPEC-005.

## 6. Acceptance
- Precision ≥ 98 % on SOCIAL_SECURITY, AEAT_PAYMENT, OWN_TRANSFER.
- Golden #9 reconstruction.
- Fixture set `tests/fixtures/bank/` with ≥ 200 anonymised, labelled lines.
- Every `UNCLEAR` has a human-readable question.
