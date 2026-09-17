# SPEC-001 — Domain Model

**Status:** Draft · **Phase:** 1, 3 · **Theory refs:** §1.3, §3.1–3.2, §5.1, §7.1, §7.5, §15.1

## 1. Purpose
Define the entities, value objects and invariants shared by the engine, application and persistence layers. Identifiers keep Spanish tax vocabulary (see `docs/CONVENTIONS.md`). Supersedes the prototype `Transaction`/`IrpfCalculationResult` models in `src/GestorIA.Domain/Models`.

## 2. Value objects

| Type | Definition | Invariants |
|---|---|---|
| `Money` | `decimal Amount`, currency fixed EUR | exact arithmetic; `Round2()` explicit |
| `Rate` | `decimal` in [0,1] | e.g. `0.21m` for IVA 21 % |
| `TaxYear` | `int Year` | 2024 ≤ year ≤ current+1 |
| `Region` | ISO 3166-2:ES code (`VC`, `MD`, `CT`, `AN`…) | must exist in config for the year; `PV`/`NC` (foral) rejected in v1 |
| `Nif` | Spanish NIF/NIE/CIF | checksum validated; foreign VAT ids allowed with `Country != ES` |
| `Quarter` | `TaxYear`, `1..4` | period bounds derived |
| Shares | `OfficeShare` (0–1), `DeductibleShare` (0–1) | |

## 3. Taxpayer

```
TaxpayerProfile
  Id, Nif, Name
  TaxYear                        // profile is per year (snapshot at 31-Dec, Theory §4.4)
  Region                         // residence at 31-Dec
  BirthDate → Age at 31-Dec
  DisabilityDegree (0 | 33..64 | 65..100), NeedsThirdPartyAssistance
  MaritalStatus, Spouse?: FamilyMember (for conjunta)
  Descendants[]: FamilyMember { BirthDate, LivesWithTaxpayer, OwnIncome, DisabilityDegree, SharedWithOtherParent }
  Ascendants[]:  FamilyMember { BirthDate, LivesWithTaxpayer, OwnIncome, DisabilityDegree }
  Employment?: { Employers[] { Cif, Name } }
  Activity?:   AutonomoRegistration
  Housing?:    { IsRenting, RentalContract?, IsOwnerOfVivienda, Mortgage? }
  OtherProperties[]: { CadastralRef, CadastralValue, RevisedInLast10Years, Use: Rented|Empty|Habitual, DaysAvailable }
```

```
AutonomoRegistration (Modelo 036/037)
  AltaDate, BajaDate?
  IaeCode, ActivityKind: Profesional | Empresarial
  RegisteredInROI (bool)
  HomeOfficeSharePercent (0–100)
  AccountingCriterion: Devengo | Caja      // Caja locks for 3 years (warning only in v1)
  RetencionRate: 0.07 | 0.15 | 0            // derived: 7 % first 3 years if Profesional
  TarifaPlana: { StartDate, ExtendedSecondYear }
```

## 4. Ledger entities (all reference a `SourceDocument`)

```
Nomina            Period(Month), EmployerCif, TotalDevengado, Exentas, EnEspecie, SsTrabajador,
                  IrpfRetenido, IrpfRate, Liquido, Atrasos?, IsExtraPay
CertificadoRetenciones  Year, EmployerCif, totals — takes priority over Σ nóminas (Theory §15.1)
FacturaEmitida    Number, Series, IssueDate, AccrualDate, Client{Nif,Name,Country,InVies},
                  Base, IvaRate, IvaAmount, RetencionRate, RetencionAmount, Total,
                  IvaRegime: Standard | ReverseChargeEU | ExportNonEU | Exempt(article)
FacturaRecibida   Number, IssueDate, Supplier{Nif,Name,Country}, Base, IvaRate, IvaAmount, Total,
                  Kind: Completa | Simplificada, ExpenseCategory, DeductibleShare, IvaDeductible(bool),
                  Asset?: { Cost, UsefulLifeYears, StartDate }   // depreciation, Theory §9
BankTransaction   AccountId, BookingDate, ValueDate, Description, Counterparty?, Amount(signed),
                  Balance?, BankCategory?, Classification (SPEC-004), LinkedDocumentId?
Pago130 / Pago303 Quarter, AmountPaid, PaidDate, Receipt(SourceDocument)
SavingsRecord     Kind: Interest | Dividend | Coupon | Gain | Loss, Date, Gross, Retencion, Source
RentalRecord      Property, Month, RentReceived, Expenses[] (per Theory §10.2)
```

## 5. Documents

```
SourceDocument   Id, Type (BankStatement|Nomina|CertificadoRetenciones|FacturaEmitida|FacturaRecibida|
                 DatosFiscales|Modelo036|RentalContract|Receipt|Other), FileRef (encrypted blob),
                 Sha256, UploadedAt, ExtractionStatus, Extraction: ExtractedDocument (SPEC-005),
                 ReviewStatus: Pending | Confirmed | Rejected
```

Invariants
- A `FacturaRecibida` can only be `Confirmed` if `Kind == Completa` **or** the user explicitly accepts the audit risk of a simplificada (flag stored, warning in explanations).
- A `BankTransaction` classified `DEDUCTIBLE_EXPENSE` must have `LinkedDocumentId` before it counts (Theory §15.2 main rule).
- Ledger rows are immutable after confirmation; corrections create a new version and keep history.

## 6. Year attribution
- Employment: nómina `Period` month.
- Activity income/expense: `AccrualDate` (devengo) — the invoice date, not the payment date; if `Caja`, the payment date (v1: flag + warning, calculation still by devengo).
- Savings: payment date per broker/bank report.

## 7. Migration from the prototype
`IrpfTaxCalculator` and `IrpfCalculationResult` were deleted on 2026-09-18. `Transaction` survives only because `BbvaCsvStatementParser` still produces it; it is replaced by `BankTransaction` in Phase 4.

- `Transaction.Type` derived from the sign of `Amount` → replaced by explicit `Classification` (SPEC-004).
- `Transaction.IsDeductible` → replaced by a link to a confirmed `FacturaRecibida` with `DeductibleShare`.
- `VatRate` enum with integer values → `Rate` value object from config (`iva.rates`).

## 8. Open questions
- Multi-account ownership shares (joint accounts) — v1 assumes 100 % unless profile says otherwise.
- Payments in kind valuation rules — v1 accepts the nómina's stated value.
