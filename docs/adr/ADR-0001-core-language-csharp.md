# ADR-0001: C#/.NET for the tax engine and API

**Status:** Accepted · **Date:** 2026-09-17 · **Deciders:** project owner

## Context
The core is a rule-heavy financial calculator: progressive scales, dozens of typed aggregates (casillas), yearly-versioned parameters, and correctness to the cent. The author's primary language is TypeScript; C# and Python are being learned on this project. Candidates: C#, TypeScript/Node, Python, Kotlin/Java, Go, Rust.

## Decision
Use C# on .NET 10 LTS for `Domain`, `Engine`, `Application`, `Infrastructure` and `Api`.

## Alternatives considered
| Option | Native decimal | Static typing | Web stack | Fit for rule-heavy domain | Learning value |
|---|---|---|---|---|---|
| C# / .NET | yes (`decimal`) | strong | ASP.NET Core | high | high (target language) |
| TypeScript / Node | no (lib) | structural, `any` escape | Express/Nest | medium; money bugs likely | none (already known) |
| Python | yes (`Decimal`) | optional | FastAPI | medium (runtime errors) | medium |
| Kotlin / Java | `BigDecimal` (verbose) | strong | Spring | high | medium |
| Go | no (lib) | strong, minimal | net/http | low (verbose rules) | low |
| Rust | yes (crate) | strongest | axum | high but slow to iterate | low for this goal |

## Trade-offs
C# gives first-class `decimal`, operator overloading on it, records for immutable value objects, LINQ for aggregations, and a mature web stack — at the cost of being new to the author. TypeScript would be faster to start but weak on money arithmetic and would not meet the learning goal. Rust's rigor is not worth the iteration cost while the domain rules are still being encoded.

## Consequences
- Engine is a pure class library, unit-tested with xUnit; Stryker.NET for mutation testing.
- Analyzer rule bans `double`/`float` in `Domain` and `Engine` (ADR-0004).
- Learning track in `plans/DEVELOPMENT_PLAN.md §8` is part of the plan, not an afterthought.
