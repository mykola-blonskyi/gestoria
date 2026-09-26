# GestorIA.Cli

Prints a set-aside estimate (#2) from a file, so the estimate can be read without a test runner (#11). It is the smallest possible surface, not the interface decision, which `docs/decisions.md` defers to M3.

```
dotnet run --project src/GestorIA.Cli -- <input.json> <tax-year-config.json>
```

For example, with the fictitious input in this folder:

```
dotnet run --project src/GestorIA.Cli -- src/GestorIA.Cli/set-aside-input.example.json config/tax-years/2025.example.json
```

The output is the calculation trace step by step, then the estimate, then the warnings. The tax year and the configuration's file name and SHA-256 hash are printed with the estimate, so an answer can be traced back to the configuration that produced it. Exit code 0 is an estimate, 1 an input, configuration or engine rejection with its reason on stderr, 2 a usage error.

## Real figures stay outside the repository

The input file holds personal financial data, so a real one never lives in this repository (SPEC-013). Copy the example to a folder outside the repository, fill it in there, and pass its path.

## The configuration is named explicitly

The console takes the tax-year file by path and never looks one up by year. `config/tax-years/2025.json` does not exist yet: the 2025 values live in `2025.example.json`, which still has `_todo` blocks and is renamed once they are filled in. Falling back from `2025.json` to the example would hide which file produced the numbers, and SPEC-007 §3 makes a missing tax year an error, never a default.

## Input format

The input is the `setAside.inputs` object of a set-aside golden (`tests/golden/2025/G12.json` to `G19.json`), so any golden's inputs can be pasted in as they are. `set-aside-input.example.json` holds G14's figures. Every field is required. Amounts are strings with a decimal point and no thousands separator, such as `"27000.00"`.

| Field | Value |
|---|---|
| `asOf` | The quarter the estimate is for: `"Q1"` to `"Q4"` |
| `profile.region` | `"VC"` |
| `profile.employment.ingresos`, `.seguridadSocial` | The year's salary and the employee's own SS, `"0.00"` without employment |
| `profile.activity.alta` | The date of alta, `"yyyy-MM-dd"` |
| `profile.activity.previousYear` | `"noActivity"`, or `{ "rendimientoNeto": "8000.00" }` for the previous year's activity net |
| `profile.activity.newActivity` | `"established"`, or `{ "period": "first" \| "following", "ingresosFromFormerEmployer": "0.00" }` (LIRPF art. 32.3) |
| `activity.retenciones` | `"foreignPayersOnly"`, the only case the estimator covers (SPEC-003 §0) |
| `activity.actuals` | The closed quarters in order, each `{ "quarter", "ingresosYtd", "gastosYtd" }` cumulative from 1 January with the RETA cuotas in gastos; `[]` when none is closed |
| `activity.projection.ingresos`, `.gastos` | What the rest of the year is expected to invoice and spend, the RETA cuota excluded |

A missing field or a value of the wrong shape is rejected with its JSON path and what it must be. What only the engine can judge, such as actuals out of order, the engine rejects with its own reason.
