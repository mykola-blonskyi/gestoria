# Future Work

## High Priority (v1.x, right after v1.0)

Cut from v1.0 by ADR-0012, in the order they would come back:

- [ ] Autónomo scope: activity income, issued and received invoices, difícil justificación, SS tramos (SPEC-001, SPEC-002 step 2)
- [ ] Modelo 130 and Modelo 303 (SPEC-003), with the `_todo` config blocks they need
- [ ] OCR service and document ingestion (SPEC-005), and the classifier's expense side (SPEC-004)
- [ ] Madrid and the remaining common-regime regions

- [ ] Hosted deployment: VPS, Caddy TLS, OIDC, upload rate limiting, encrypted backups with a rehearsed restore, GDPR export and delete (deferred by ADR-0010; needs a real second user first)
- [ ] `config/tax-years/2027.json` when BOE publishes the changes; rehearse the new-year runbook
- [ ] gRPC contract between API and OCR if throughput demands it (revisit ADR-0005)
- [ ] RabbitMQ implementation of `IExtractJobQueue` when OCR moves to separate hardware (revisit ADR-0009)
- [ ] Broker report importers (real FIFO from exports: DEGIRO, IBKR, Trade Republic…)

## Medium Priority

- [ ] Fine-tuned invoice extraction model on accumulated, consented data
- [ ] Modelo 720/721 threshold reminders (no filing)
- [ ] Foreign-tax credit (deducción por doble imposición internacional)
- [ ] Modelo 349 and 390 forms (currently only aggregates)
- [ ] Criterio de caja fully supported (today: flag + warning, calculation by devengo)
- [ ] Depreciation table per asset class completed; multi-year asset tracking

## Low Priority

- [ ] Gestor mode: one gestor manages many clients (multi-tenant)
- [ ] AEAT XML import file generation for Renta WEB
- [ ] Blazor back-office for gestor mode (ADR-0007 allows it)
- [ ] Mobile capture flow (photo → OCR → review) as PWA
- [ ] Estimación objetiva (módulos), IRNR, foral regimes — explicitly out of v1
