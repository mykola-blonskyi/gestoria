# 05: The 2025 configuration carries real social security tramos and Modelo 130 minoración bands

**Issue:** #5 — https://github.com/mykola-blonskyi/gestoria/issues/5

**What to build:** The two empty arrays that everything in this feature reads from. `seguridadSocial.tramos` and `modelo130.minoracion` are `[]` today, so no cuota and no Modelo 130 reduction can be computed at all.

This is research, not code. The values come from the AEAT Manual and the theory document, and each one is recorded with the source it was verified against.

**Blocked by:** 03 (#4)

**Status:** ready-for-agent

- [ ] `seguridadSocial.tramos` holds the full contribution bands for 2025: band name, net income upper bound, minimum and maximum contribution base, and the resulting cuota
- [ ] `modelo130.minoracion` holds the banded reduction for net income below the threshold
- [ ] `tarifaPlana` is confirmed against the source, not inherited from the example file
- [ ] Every value added carries per-value provenance per ticket 02, citing the AEAT Manual section or the BOE article
- [ ] The file validates against `config/tax-years/schema.json`
- [ ] The `_todo` markers these values replace are removed, and any `_todo` that survives says what still blocks it
- [ ] `2025.example.json` is renamed to `2025.json` once no `_todo` remains, per `plans/current.md`
