# src — .NET projects

Solution file: `../GestorIA.slnx` (projects under `src/`, test projects under `../tests/`). The root `README.md` explains how to build, test and run everything.

## Current

```
src/GestorIA.Domain/          Money, Rate, BankTransaction (SPEC-001); IStatementParser, Result<T>   — no dependencies
src/GestorIA.Engine/          set-aside estimator, Modelo 130, annual true-up, TGSS cuota, deadlines     — → Domain
src/GestorIA.Infrastructure/  tax-year config loader and validation; the set-aside input file parser; PostgreSQL through EF Core (Persistence/, migrations included) the stored profile (Profiles/) and statement lines (Transactions/); BbvaCsvStatementParser — → Domain, Engine
src/GestorIA.Cli/             console that prints a set-aside estimate from a file (#11)               — → Engine, Infrastructure
src/GestorIA.Api/             /api/v1: health, tax-year configs, set-aside estimate, profiles (#69); openapi/v1.json (#66) — → Engine, Infrastructure
../Directory.Build.props      nullable, warnings-as-errors, analyzers, banned double/float for money (ADR-0004)
```

`GestorIA.Cli/README.md` says how to run the console and describes its input file, which is also the body of `POST /api/v1/set-aside/estimate`. Both parse it with `GestorIA.Infrastructure/SetAside/SetAsideInputFile`, so they refuse the same inputs with the same messages. `dotnet build` rewrites `GestorIA.Api/openapi/v1.json`; commit it with the change that moved it.

After a change to the database model, add a migration with the pinned tool (`dotnet tool restore` once): `dotnet ef migrations add <Name> --project src/GestorIA.Infrastructure --output-dir Persistence/Migrations`. The API applies pending migrations as it starts.

## Target

```
src/
├── GestorIA.Domain/           entities, value objects (SPEC-001)                       — no dependencies
├── GestorIA.Engine/           calculators, mappers, rule evaluator (SPEC-002/003/006/008) — → Domain
├── GestorIA.Application/      use cases, DTOs, validators, ports                      — → Domain, Engine
├── GestorIA.Infrastructure/   EF Core, blob storage, OCR client, parsers, jobs        — → Application
└── GestorIA.Api/              ASP.NET Core minimal API (SPEC-009)                     — → Application, Infrastructure
```

Commands: `dotnet build GestorIA.slnx`, `dotnet test GestorIA.slnx`, `dotnet run --project src/GestorIA.Cli -- <input.json> <tax-year-config.json>`.
