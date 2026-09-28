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

### 3.1 Built so far (#73)

- The classes a line can be given are `ActivityIncome`, `DeductibleExpense`, `SocialSecurity`, `AeatPayment`, `EmploymentIncome`, `SavingsIncome`, `OwnTransfer` and `Personal` (`TransactionClass`, `src/GestorIA.Domain/Models`). An unclear line has no class. `HOME_EXPENSE` waits for the home-office share.
- The rules are data: `config/transaction-rules.json`, in this section's order, first match wins. Each rule has an id, a class, a direction (`credit`, `debit` or `any`), whether it is `certain`, and its patterns. A pattern matches at the start of a word of the description, case- and accent-insensitively, with every run of punctuation and spaces read as one space, so `CUOTA AUTONOM` matches `CUOTA AUTONOMOS`, `DIVIDEND` matches `DIVIDENDO` and `ABONO DIVIDEND`, and `AGUA` does not match `PARAGUAS`. A pattern ending in a space matches a whole word only: `DIA ` matches `COMPRA DIA` and `SUPER DIA, VALENCIA` but not `DIARIO`. Built: steps 2 (TGSS), 3 (AEAT, without `FormHint`), 4 by wording only (`NOMINA|SALARIO`, no employer CIF or nómina cross-check), 5, 7 and 9. Step 1 waits for `FacturaEmitida`, step 6 for accounts, step 8 for `HOME_EXPENSE`. The API loads and checks the file as it starts, and refuses to start when the file is malformed.
- A line's classification is `Confirmed(class)`, `Suggested(class, rule)` or `Unclear`. The user's decision always wins. Without one, a `certain` rule confirms, any other rule suggests, and no match leaves the line unclear. Only the user's decision is stored (`BankTransactions.Class`); the rules run on every read (`docs/decisions.md`). Only a debit rule may be `certain`, and the file is refused at start-up otherwise: a credit could be activity income, so salary (`NOMINA `, `NOMINAS `, `SALARIO `, `SALARIOS `, whole words), savings, an AEAT refund (`aeat-credit`) and every other recognised credit only suggest, and a TGSS credit is left unclear. The certain rules are the TGSS charges, the AEAT payments and personal spending. A debit confirmed into a class outside the activity is not the activity's rendimiento (LIRPF art. 28): its quarter becomes actuals with it left out, where an unclear or suggested line holds its quarter on the projection until it is reviewed. The vendor rule (step 7) only suggests. Known false match: `DIA ` also catches a debit such as `CARGO DIA 15 ALQUILER OFICINA` as personal; it moves no money while no expense can count (business rule 1), and the user can reclassify it.
- The review queue holds the tax year's suggested and unclear lines. The web app asks each one's question, "money in / money out: what is it?", with the rule's suggestion first when there is one.
- A confirmed `DEDUCTIBLE_EXPENSE` counts only once an invoice is linked. No document can be stored yet, so none counts (business rule 1). The estimate names each such line, and the overview says how many there are.
- `Confidence`, `Evidence[]`, `FormHint`, `LinkedDocumentId` and learning from confirmations (§4) are not built.

## 4. Learning from confirmations
User corrections are stored as `ClassificationFeedback { pattern, class }` and become per-user rules evaluated before step 7. No ML in v1.

## 5. Input adapters
`IStatementParser` (existing interface in `Domain/Interfaces`) implementations per bank: BBVA CSV/XLSX, Sabadell, Revolut, Wise, generic CSV with column mapping. PDF statements go through SPEC-005.

Built so far (#72): `IStatementParser.Parse(text)` answers every `BankTransaction` of a statement in the file's order, or refuses the whole file with `InvalidStatementException`, whose errors name each unreadable line by number and never quote it. `BbvaCsvStatementParser` reads BBVA's CSV (`Fecha;Fecha Valor;Concepto;Importe;Saldo`, dates `dd/MM/yyyy`, Spanish amounts, `;` inside double quotes allowed). BBVA XLSX is not read yet; the API refuses a ZIP file with a message saying to export CSV.

## 6. Acceptance
- Precision ≥ 98 % on SOCIAL_SECURITY, AEAT_PAYMENT, OWN_TRANSFER.
- Golden #9 reconstruction.
- Fixture set `tests/fixtures/bank/` with ≥ 200 anonymised, labelled lines.
- Every `UNCLEAR` has a human-readable question.
