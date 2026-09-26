# Glossary

Code identifiers keep the Spanish term (see `docs/CONVENTIONS.md`). Full glossary with explanations: Theory §16 (vault).

## Terms

| Term (identifier) | English | Where used |
|---|---|---|
| **AEAT / Hacienda** | Spanish tax agency | classifier (`AEAT_PAYMENT`), forms |
| **TGSS / Seguridad Social** | social security | classifier (`SOCIAL_SECURITY`), cuota autónomos |
| **IRPF** | personal income tax | Modelo 100, Modelo 130 |
| **IVA** | VAT | Modelo 303 |
| **Nómina** | payslip | `Nomina` entity |
| **Devengado / Exentas / Líquido** | gross earnings / exempt amounts / net pay | `Nomina` fields |
| **Retención** | withholding at source (advance IRPF) | `Nomina.IrpfRetenido`, `FacturaEmitida.RetencionAmount` |
| **Factura emitida / recibida** | issued / received invoice | ledger entities |
| **Factura completa / simplificada** | full / simplified invoice (simplified ≠ valid proof) | `FacturaRecibida.Kind` |
| **Base imponible** | tax base (of an invoice: net amount; of IRPF: BIG/BIA) | `Base`, `bases.big/bia` |
| **Rendimiento neto (RN)** | net income of a category | `trabajo.rn`, `actividad.rn` |
| **Reducción por rendimientos del trabajo** | low-salary relief | `trabajo.reduccion`, golden #8 |
| **Gastos de difícil justificación** | 5 % hard-to-justify expense allowance (cap 2,000) | `actividad.dj` |
| **Reducción por inicio de actividad** | 20 % off a new activity's positive net in its first positive period and the next (LIRPF art. 32.3), annual return only | `actividad.inicioActividad`, `NewActivity`, golden G13 |
| **Base liquidable general / del ahorro (BLG/BLA)** | taxable base after reductions | `bases.blg/bla` |
| **Mínimo personal y familiar** | tax-free minimum (applied via the scale, not subtracted) | `MinimoCalculator` |
| **Escala estatal / autonómica / del ahorro** | state / regional / savings rate scales | `config.irpf.*`, `regions.*` |
| **Cuota íntegra (CIE/CIA)** | gross tax, state / regional part | casillas 0545/0546 |
| **Deducción** | tax credit (reduces cuota, not base) | SPEC-006 |
| **Cuota líquida (CL)** | tax after credits | SPEC-002 step 7 |
| **Pagos a cuenta** | prepayments: retenciones + Modelo 130 | casillas 0596/0599/0604 |
| **Resultado: a ingresar / a devolver** | to pay / to be refunded | casilla 0670 |
| **Declaración individual / conjunta** | individual / joint return | `FilingMode` |
| **Devengo / criterio de caja** | accrual / cash accounting criterion | `AccountingCriterion` |
| **Estimación directa simplificada** | the autónomo regime supported in v1 | scope |
| **Tarifa plana** | reduced flat cuota for new autónomos | `seguridadSocial.tarifaPlana` |
| **Regularización** | annual SS true-up by real income | explanations (`SS_REGULARIZACION_AHEAD`) |
| **Casilla** | numbered field of a form | SPEC-008 |
| **Borrador / datos fiscales** | AEAT draft / pre-filled data | `DatosFiscales` document type |
| **Modelo 036/037** | autónomo registration form | `AutonomoRegistration` |
| **ROI / VIES** | EU intra-community operators register | `RegisteredInROI`, `Client.InVies` |
| **Inversión del sujeto pasivo** | reverse charge (EU/US SaaS) | `IvaRegime.ReverseChargeEU` |
| **Imputación de rentas inmobiliarias** | imputed income from unused property | `inmuebles.imputacion` |
| **Fianza** | rental deposit (must be lodged for the rent credit) | SPEC-006 example rule |
