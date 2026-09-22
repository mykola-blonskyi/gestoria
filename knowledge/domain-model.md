# Domain Model

Authoritative definition: `docs/specs/SPEC-001-domain-model.md`. Tax theory behind it: Obsidian vault (`Theory §…`), never copied here.
Entity-relationship diagram: `graph/architecture.md` → Components.

## Entities

### TaxpayerProfile (per tax year)

Responsibilities: snapshot of the person at 31 December — region, age, family, disability, employment/autónomo status, housing, properties.

Fields: `Nif`, `TaxYear`, `Region`, `BirthDate`, `DisabilityDegree`, `MaritalStatus`, `Spouse?`, `Descendants[]`, `Ascendants[]`, `Employment?`, `Activity?` (AutonomoRegistration), `Housing?`, `OtherProperties[]`.

Relationships: owns a `Ledger`; input to every calculation.

---

### Ledger rows (all linked to a `SourceDocument`)

| Entity | Purpose |
|---|---|
| `Nomina` | monthly payslip; feeds employment income |
| `CertificadoRetenciones` | annual employer certificate; overrides Σ nóminas |
| `FacturaEmitida` | issued invoice; activity income by devengo; retención; IVA repercutido |
| `FacturaRecibida` | supplier invoice; deductible expense (with share/depreciation); IVA soportado |
| `BankTransaction` | statement line with `Classification` (SPEC-004) and optional link to an invoice |
| `Pago130` / `Pago303` | quarterly payments already made |
| `SavingsRecord` | interest, dividends, gains/losses |
| `RentalRecord` | rental income and expenses per property |

---

### SourceDocument

Responsibilities: uploaded file + extraction result + review status. Nothing enters a ledger row until `Confirmed`.

Fields: `Type`, `FileRef` (encrypted), `Sha256`, `Extraction` (SPEC-005 result), `ReviewStatus`.

---

### Calculation outputs

`AnnualResult` (casillas, resultado, trace, joint comparison, credit outcomes, warnings, configHash) · `Modelo130Result` · `Modelo303Result` · `CalculationTrace` (step-by-step, each step referencing theory §, casillas and ledger rows).

---

### TaxYearConfig

Immutable, loaded from `config/tax-years/YYYY.json` (SPEC-007). Contains every 📅 value: scales, minimums, thresholds, rates, casilla map, credit rules.

## Value objects

`Money` (decimal, EUR) · `Rate` · `TaxYear` · `Region` (ISO 3166-2:ES) · `Nif` · `Quarter`.
