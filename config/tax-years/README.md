# Tax year configuration

One file per year: `YYYY.json`, validated against `schema.json` (SPEC-007, ADR-0003). Every value marked 📅 in the theory document lives here, with a source reference. Engine code never contains a tax number — the prototype's hard-coded `IrpfBrackets` is what this folder replaces.

`2025.example.json` is a starter with the values used by the golden tests (national scale, Valencia and Madrid scales, savings scale, minimums, employment relief, activity parameters, casilla map). Fields marked `_todo` must be completed in Phase 0; then rename to `2025.json`.

`2026.json` is the first production file (#47). Every value carries `boe` provenance read against the text in force for 2026. What no norm had published when it was written is a `_todo` gap the engine refuses to compute with, never a borrowed 2025 value: the tarifa plana amount (`seguridadSocial.tarifaPlana`, no LPGE 2026), the renta window and the 2027 días inhábiles (`calendar`, so the Q4 Modelo 130 and renta deadlines are refused while Q1–Q3 compute), and the Modelo 100 casillas. Madrid is filled for both years (ADR-0016).
