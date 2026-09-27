# ADR-0016: Madrid is a v1.0 region, in every tax year

**Status:** Accepted · **Date:** 2026-09-27 · **Supersedes:** the Madrid part of [ADR-0013](ADR-0013-v1-scope-employee-and-autonomo.md) (its employee and autónomo scope and its OCR cut stand)

## Context
ADR-0013 kept Madrid out of v1.0 and relied on a fake region in a test fixture to prove the region model is data-driven. `2025.example.json` carried Madrid as an empty block with a `_todo` note, and `2026.json` left it out.

On 2026-09-27 the author reversed that cut and asked for Madrid everywhere a region is taken. ADR-0013 is Accepted and is not edited, so this record replaces its Madrid clause and leaves the rest of it in force.

The engine was already region-agnostic. `RegionTable.For(code)` serves any complete block, and the scale, the mínimos and the holidays all come from config. Adding Madrid is therefore a data task against primary sources, plus tests that stop using Madrid as the example of an unusable region.

## Premises

| Premise | Stated or concluded | Source |
|---|---|---|
| Madrid is in v1.0, in every tax-year file and every calculation that takes a region | Stated | Author, 2026-09-27 (#60) |
| The OCR service stays out of v1.0 | Stated | Author, 2026-09-18 (ADR-0013), not revisited |
| Madrid's scale and mínimos for 2025 and 2026 are DL 1/2010 arts. 1 to 2 quater as worded by Ley 13/2023, unchanged since | Concluded, from boe.es consolidated BOCM-m-2010-90068 (last updated 2026-07-10) and the AEAT Manual práctico Renta 2025, read 2026-09-27 | #60 |
| No Madrid law after that update changes those articles for 2026 | **Unverified after 2026-07-10.** Madrid has changed its scale late in a year before, with Ley 13/2023 in December 2023 applying to 2023 | Re-check with `2027.json` |
| A synthetic region still proves that an incomplete region is refused | Concluded. The refusal is behaviour of `RegionTable`, not of Madrid | This ADR |

## Decision
v1.0 covers two regions, Comunitat Valenciana (`VC`) and Comunidad de Madrid (`MD`). Both are complete in every tax-year file, and the schema requires both.

Madrid's block follows VC's shape: the regional scale, the full `minimosOverride` (Madrid approves its own amounts, so the state ones do not apply to its scale), and its días inhábiles from the AGE resolutions, each with `boe` provenance. The 2027 holidays stay a declared gap in `2026.json`, as for VC.

The tests that used Madrid's `_todo` block as the declared-incomplete region now add a synthetic block to a copy of the file.

## Alternatives
- **Keep Madrid as a `_todo` block until someone needs it.** Rejected by the author. It also left the region model exercised by one real region only.
- **Fill Madrid in `2026.json` only, since 2026 is the first production year.** The 2025 file is the golden corpus, so Madrid goldens need 2025 values, and the law is the same text for both years.

## Consequences
- `regions.MD` is filled in `2025.example.json` and added to `2026.json`. `schema.json` requires `MD` next to `VC`.
- `2025.example.json` loses its Madrid `_todo`, which unblocks the rename to `2025.json` (#59).
- G21 and G22 run G11 and G16 for a Madrid taxpayer, at tier `theory`. No VC golden changes.
- Regional credits for Madrid (SPEC-006) arrive with the credits work, as VC's do. Nothing computes a regional credit today.
- A Madrid-only deadline is now tested: Easter Monday is a holiday in Valencia and a working day in Madrid, so the Q1 2025 Modelo 130 is due on 21 April in Madrid and 22 April in Valencia.
