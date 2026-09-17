# Future Work

## High Priority (v1.x, right after v1.0)

- [ ] Local-only deployment mode: end-user `docker-compose.yml`, install guide, smoke tests on macOS/Windows/Linux with Docker Desktop (deferred by ADR-0008)
- [ ] Remaining 13 common-regime regions (scales + credits as JSON only)
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
