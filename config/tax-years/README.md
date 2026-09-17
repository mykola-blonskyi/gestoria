# Tax year configuration

One file per year: `YYYY.json`, validated against `schema.json` (SPEC-007, ADR-0003). Every value marked 📅 in the theory document lives here, with a source reference. Engine code never contains a tax number — the prototype's hard-coded `IrpfBrackets` is what this folder replaces.

`2025.example.json` is a starter with the values used by the golden tests (national scale, Valencia scale, savings scale, minimums, employment relief, activity parameters, casilla map). Fields marked `_todo` must be completed in Phase 0; then rename to `2025.json`.
