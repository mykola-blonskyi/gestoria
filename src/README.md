# src — .NET projects

Solution file: `../GestorIA.slnx` (projects under `src/`, test projects under `../tests/`).

## Current (prototype)

```
src/GestorIA.Domain/          Transaction, IrpfCalculationResult, IrpfTaxCalculator (hard-coded scale), IStatementParser, Result<T>
src/GestorIA.Infrastructure/  BbvaCsvStatementParser
src/GestorIA.Api/             empty
```

## Target (Phase 0 adds the missing projects; Phase 1 replaces the prototype)

```
src/
├── GestorIA.Domain/           entities, value objects (SPEC-001)                       — no dependencies
├── GestorIA.Engine/           calculators, mappers, rule evaluator (SPEC-002/003/006/008) — → Domain
├── GestorIA.Application/      use cases, DTOs, validators, ports                      — → Domain, Engine
├── GestorIA.Infrastructure/   EF Core, blob storage, OCR client, parsers, jobs        — → Application
└── GestorIA.Api/              ASP.NET Core minimal API (SPEC-009)                     — → Application, Infrastructure
../Directory.Build.props       nullable, warnings-as-errors, analyzers, banned double/float for money (ADR-0004)
```

Commands: `dotnet build`, `dotnet test`, `dotnet run --project src/GestorIA.Api`.
