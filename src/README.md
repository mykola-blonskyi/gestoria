# src — .NET projects

Solution file: `../GestorIA.slnx` (projects under `src/`, test projects under `../tests/`). The root `README.md` explains how to build, test and run everything.

## Current

```
src/GestorIA.Domain/          Money, Rate (SPEC-001); prototype Transaction, IStatementParser, Result<T>   — no dependencies
src/GestorIA.Engine/          set-aside estimator, Modelo 130, annual true-up, TGSS cuota, deadlines     — → Domain
src/GestorIA.Infrastructure/  tax-year config loader and validation; prototype BbvaCsvStatementParser   — → Domain, Engine
src/GestorIA.Cli/             console that prints a set-aside estimate from a file (#11)               — → Engine, Infrastructure
src/GestorIA.Api/             no code yet                                                              — → Infrastructure
../Directory.Build.props      nullable, warnings-as-errors, analyzers, banned double/float for money (ADR-0004)
```

`GestorIA.Cli/README.md` says how to run the console and describes its input file.

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
