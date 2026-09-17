# ADR-0004: `decimal` for money; rounding only at the casilla boundary

**Status:** Accepted · **Date:** 2026-09-17

## Context
Binary floating point cannot represent 0.10 exactly; tax forms require cent precision and reproducibility. AEAT rounds casilla values to 2 decimals. The prototype rounds per bracket (`Math.Round(taxableInBracket * rate, 2)`), which drifts from AEAT results by cents on some inputs.

## Decision
- `Money` is a readonly record struct wrapping `decimal` with EUR currency; arithmetic operators defined; comparisons exact.
- Percentages are `decimal` rates (`0.095m`), never integer percents.
- Intermediate results are **not** rounded. Rounding (`MidpointRounding.AwayFromZero`, 2 dp) is applied by `Modelo100Mapper` when writing casillas, and by 130/303 mappers when writing form lines.
- Python side uses `Decimal` with `ROUND_HALF_UP`; extraction returns amounts as strings ("1060.00") to avoid float parsing.

## Alternatives
- `double` with tolerance-based comparisons: rejected — non-reproducible cents, tolerance hides bugs.
- Integer cents (`long`): exact, but percentages and depreciation shares need fractional intermediates anyway.

## Consequences
- Roslyn banned-API rule forbids `double`/`float` in `Domain`/`Engine`.
- Golden tests assert exact equality on casilla values.
- Where AEAT rounds intermediate lines (documented cases), the mapper applies the same rounding and the trace records it.
