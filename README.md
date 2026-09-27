# GestorIA

GestorIA is a tax engine for one person living in Spain. It works out Spanish income tax (IRPF) and the quarterly tax forms for someone who is an employee, an autónomo (self-employed), or both at once.

The first question it answers is simple: **when a client pays me, how much of that money is not mine?** Part of every payment will later go to Hacienda (income tax) and to the Seguridad Social (TGSS). GestorIA tells you how much to put aside, when each payment is due, and why, step by step.

This is a personal project, not a product and not tax advice. The numbers are only as good as the rules and the configuration behind them. Check anything important with a gestor or the AEAT.

## Contents

1. [What it does today](#what-it-does-today)
2. [What it does not do yet](#what-it-does-not-do-yet)
3. [Quick start](#quick-start)
4. [Running the console](#running-the-console)
5. [Reading the estimate](#reading-the-estimate)
6. [How the project is organised](#how-the-project-is-organised)
7. [Tax-year configuration](#tax-year-configuration)
8. [Tests](#tests)
9. [Working on the project](#working-on-the-project)
10. [Where to find things](#where-to-find-things)
11. [Troubleshooting](#troubleshooting)

## What it does today

- **Set-aside estimate.** Given your salary, your self-employed income and costs, and the quarter you are in, it computes:
  - the share of each payment you receive that you should put aside;
  - the next Modelo 130 payment (the quarterly income-tax advance) and the days you can pay it;
  - your monthly Seguridad Social cuota (RETA), including the tarifa plana while it lasts;
  - how much more the annual return (Renta, Modelo 100) will ask for on top of the Modelo 130 advances, and by when;
  - the IVA to put aside (zero for this profile, with the reason).
- **Two regions.** The Comunitat Valenciana (`VC`) and the Comunidad de Madrid (`MD`), each with its own tax scale, personal allowances (mínimos) and holidays (ADR-0013, ADR-0016).
- **Two tax years.** 2025 and 2026. Every rate and threshold lives in a JSON file per year, with the law it comes from. No tax number is written in the code.
- **A console program** that prints the estimate, the full calculation step by step, and any warnings.
- **Checks on its own data.** The yearly configuration files are validated against a schema and extra rules every time they load, so a typo in a rate is caught before it changes a figure.

The estimate always leans towards putting aside *more* rather than less. Having idle money is better than facing a bill you cannot pay. Wherever the engine makes such a choice, the step-by-step output says so.

## What it does not do yet

- **No filled-in forms.** It computes the figures behind Modelo 130, 303 and 349, and the configuration knows which box (casilla) each figure goes in, but it does not yet produce the forms themselves.
- **No 2027 configuration.** The 2027 values are published in the BOE around December 2026 (issue #12). Until then, 2027 cannot be computed.
- **Some 2026 values are not published yet.** The engine refuses to guess them and tells you exactly what is missing (see [Tax-year configuration](#tax-year-configuration)).
- **No web app, API, database or document reading (OCR).** These are planned (`plans/DEVELOPMENT_PLAN.md`). `src/GestorIA.Api` is an empty project today, and `services/ocr` holds only a description.
- **No tax credits (deducciones) and no savings income** in the estimate. Leaving them out can only make the estimate higher, never lower.

## Quick start

### What you need

- **Git.**
- **The .NET 10 SDK** (version `10.0.x`). Check with `dotnet --version`.
- **The GitHub CLI (`gh`)**, only if you want to work with issues and pull requests from the terminal.

### Get the code and build it

```bash
git clone https://github.com/mykola-blonskyi/gestoria.git
cd gestoria
git config core.hooksPath .githooks
dotnet restore GestorIA.slnx
dotnet build GestorIA.slnx
dotnet test GestorIA.slnx
```

What each step does:

| Command | Why |
|---|---|
| `git config core.hooksPath .githooks` | Turns on the repository's git hooks. The `commit-msg` hook rejects commits that are not authored by the configured `user.email` or that carry attribution lines. Run it once per clone. |
| `dotnet restore` | Downloads the NuGet packages. |
| `dotnet build` | Compiles everything. Warnings are treated as errors, so a clean build prints `0 Warning(s)` and `0 Error(s)`. |
| `dotnet test` | Runs every test. All of them must pass. |

### See an estimate

```bash
dotnet run --project src/GestorIA.Cli -- src/GestorIA.Cli/set-aside-input.example.json config/tax-years/2025.json
```

This uses a made-up example. The next section shows how to run it with your own figures.

## Running the console

The console takes two files:

```bash
dotnet run --project src/GestorIA.Cli -- <input.json> <tax-year-config.json>
```

- `<input.json>` describes you: region, salary, when you registered as autónomo, what you have invoiced so far and what you expect for the rest of the year.
- `<tax-year-config.json>` is the year's rules, for example `config/tax-years/2025.json` or `config/tax-years/2026.json`. You always name the file yourself. The console never picks a year for you, so every answer says which file produced it.

### Using your own figures

1. **Copy the example somewhere outside this repository.** Your file will contain personal financial data, which must never be committed (SPEC-013).

   ```bash
   mkdir -p ~/gestoria-private
   cp src/GestorIA.Cli/set-aside-input.example.json ~/gestoria-private/my-2025.json
   ```

2. **Edit the copy.** Every field is required and no extra fields are allowed. Amounts are text with a decimal point and no thousands separator, such as `"27000.00"`. The main fields:

   | Field | What to put |
   |---|---|
   | `asOf` | The quarter you want the estimate for: `"Q1"`, `"Q2"`, `"Q3"` or `"Q4"`. |
   | `profile.region` | `"VC"` or `"MD"`. |
   | `profile.employment` | Your yearly salary and the Seguridad Social taken from it. Use `"0.00"` if you have no salary. |
   | `profile.activity.alta` | The date you registered as autónomo, `"yyyy-MM-dd"`. |
   | `profile.activity.previousYear` | `"noActivity"` if you had no self-employed income last year, otherwise last year's net. |
   | `profile.activity.newActivity` | `"established"`, or the new-activity details if you started recently (a 20 % reduction applies in the first profitable years). |
   | `activity.actuals` | What already happened: one entry per finished quarter, with totals from 1 January (income, costs, and the RETA cuotas you actually paid). Leave it `[]` if no quarter has finished yet. |
   | `activity.projection` | What you expect for the rest of the year (income and costs), and the monthly base de cotización you pay in Import@ss. |

   The full description of every field is in [`src/GestorIA.Cli/README.md`](src/GestorIA.Cli/README.md).

3. **Run it.**

   ```bash
   dotnet run --project src/GestorIA.Cli -- ~/gestoria-private/my-2025.json config/tax-years/2025.json
   ```

### What comes out

The console prints three parts, in this order:

1. **The calculation**, every step numbered, with its inputs, the formula with real numbers, the result and the law it comes from. Figures here are shown to the cent; the engine keeps them unrounded.
2. **The estimate**, a short block with the figures you act on.
3. **Notices**, warnings first. Read these before trusting the figures. They are printed last so they stay on screen.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | An estimate was printed. |
| `1` | The input file, the configuration or the engine refused. The reason is printed, for example `$.activity.projection.ingresos is "-5"; it must be zero or more.` |
| `2` | The command was used wrongly (for example, a file is missing from the command line). |

## Reading the estimate

A real run of the example looks like this:

```
Estimate
  Hold back from every payment received   19.47 %
  Next Modelo 130, Q2                     1507.40 €, due 2025-07-01 to 2025-07-21
                                          (municipal holidays where the taxpayer lives are not applied, so the date shown can be early but never late)
  Cuota SS per month this quarter         80.00 €
  Annual return (Renta) gap               0.00 €, payable by the end of 2026-06
  IVA to set aside                        0.00 €
  Tax year                                2025
  Configuration                           2025.json, sha256 4046cf06…
```

| Line | Meaning |
|---|---|
| **Hold back from every payment received** | Move this share of every client payment to a separate account. It covers the Modelo 130 advances, the RETA cuotas and any extra the annual return will want, spread over the year's income. |
| **Next Modelo 130** | The next quarterly income-tax advance and the days you can pay it. A deadline on a weekend or a national or regional holiday moves to the next working day. Local (town) holidays are not known, so the date can be a little early but never late. |
| **Cuota SS per month** | What TGSS will take from your account each month this quarter. |
| **Annual return (Renta) gap** | What the June tax return will want on top of the Modelo 130 advances. With a salary as well, this is often large, because the self-employed income is taxed at your top rate, not at the flat 20 % of Modelo 130. |
| **IVA to set aside** | Zero when all your clients are EU businesses or outside the EU: EU business clients account for the IVA themselves, and US clients are outside Spanish IVA. |
| **Tax year / Configuration** | Which rules produced the answer. The long code (SHA-256) changes whenever the file changes, so two answers with the same code came from exactly the same rules. |

## How the project is organised

```
config/tax-years/   one JSON file per tax year, plus schema.json that checks them
src/
  GestorIA.Domain/          basic types (Money, Rate) and the prototype bank-transaction model
  GestorIA.Engine/          every tax calculation; pure code, no files, no network, no clock
  GestorIA.Infrastructure/  reads and checks the yearly configuration files; prototype BBVA statement parser
  GestorIA.Cli/             the console program
  GestorIA.Api/             empty for now (planned web API)
tests/
  GestorIA.Domain.Tests/    tests for Money, Rate and the BBVA statement parser
  GestorIA.Engine.Tests/    calculator tests, configuration checks, golden tests
  GestorIA.Cli.Tests/       input reading and output formatting of the console
  golden/2025/              golden cases: full scenarios with their expected results
docs/               specifications (specs/), decisions (adr/), conventions, architecture
knowledge/          business rules, domain model, glossary of Spanish tax terms
plans/              current plan, backlog, development plan
services/ocr/       planned document-reading service (not built)
```

A few rules the code follows everywhere:

- **Money is always `decimal`, never `double` or `float`.** Using a floating-point type for money does not compile (ADR-0004).
- **Nothing is rounded until the very end.** Rounding to the cent happens only where a figure goes into a form box or is shown to you.
- **The engine is pure.** It gets everything it needs as input and returns a result plus a step-by-step trace. The same input always gives exactly the same output.
- **No tax number in code.** Every rate, threshold and date comes from `config/tax-years/`.
- **Spanish tax terms stay in Spanish** in the code (`CuotaIntegra`, `Retencion`), so they match the AEAT forms. Everything else is in English.

## Tax-year configuration

Each tax year has one file: `config/tax-years/2025.json`, `config/tax-years/2026.json`. Each file holds that year's tax scales, allowances, Seguridad Social tables, Modelo 130/303/349 details, deadlines and holidays, for both regions.

**Every value says where it comes from.** The `provenance` block in each file points each value to its source, usually an article in the BOE, and says when it was checked. See SPEC-007 §1.1.

**Missing values are declared, never guessed.** When a value has not been published yet, the file marks it with a `_todo` note explaining what is missing. The engine then refuses any calculation that would need it, and says why, instead of borrowing last year's value. Today `2026.json` has three such gaps:

- the tarifa plana amount for people registering in 2026 (no law has set it yet);
- the 2027 holidays and the Renta 2026 window, so the Q4 2026 Modelo 130 and the annual gap for 2026 are refused, while Q1 to Q3 2026 work;
- the Modelo 100 casillas for 2026.

**The files are checked every time they load.** `schema.json` checks the shape, and extra rules in `src/GestorIA.Infrastructure/TaxYears/TaxYearRules.cs` check what a schema cannot: that scales go up in order, that Seguridad Social bands do not leave gaps, that every provenance entry points at a real value, and more (SPEC-007 §2). A file that breaks a rule is rejected with a message naming the exact place.

**Adding a new tax year** (for example 2027, issue #12):

1. Copy the previous year's file to `config/tax-years/YYYY.json` and set `taxYear`.
2. Replace every value that changed, reading the new law in the BOE, and update its `provenance` entry with the source and today's date.
3. Mark anything not yet published with `_todo`, and register it in `tests/GestorIA.Engine.Tests/TaxYearGapsAreRegistered.cs`.
4. Run `dotnet test`. The configuration tests load every file in the folder automatically.
5. In the pull request, list every value that changed from the previous year and say whether any golden test is affected (docs/CONVENTIONS.md).

The full runbook is SPEC-007 §4.

## Tests

```bash
dotnet test GestorIA.slnx
```

There are four kinds of tests:

- **Example tests.** Small cases for one calculator, with the figures worked out by hand in the test.
- **Property tests** (FsCheck). They try many random inputs and check rules that must always hold, for example that tax never goes down when income goes up.
- **Configuration tests.** They load every file in `config/tax-years/`, check it against the schema and rules, and also break a copy of the file on purpose to prove each rule really catches the mistake (`TaxYearValidationRejectsBadFiles`).
- **Golden tests.** Full scenarios in `tests/golden/2025/*.json`: the inputs, the expected results, and where those expected results came from. SPEC-011 lists them all.

**Where a golden's expected numbers come from matters.** Each golden records an `oracle`:

| Oracle | Meaning |
|---|---|
| `aeat-simulator` | The figure was entered into the official AEAT simulator. The strongest. |
| `published-example` | The figure comes from a worked example in an official or published source. |
| `theory` | The figure was worked out by hand or by a separate script from the law. The weakest. |

Today every golden is `theory`. Each one also says, in `oracleRef`, exactly which law and which arithmetic produced its numbers. Expected values are never copied from the engine's own output (ADR-0011).

## Working on the project

### Issues

Work is tracked as GitHub issues in this repository. Labels:

| Label | Meaning |
|---|---|
| `needs-triage` | Needs a decision from the owner before anyone works on it. |
| `ready-for-agent` | Fully specified; can be picked up. |
| `ready-for-human` | Needs a person to do it (for example, entering a case into the AEAT simulator). |
| `needs-info` | Waiting for more information. |
| `wontfix` | Will not be done. |

Dependencies between issues use GitHub's "blocked by" links. An issue is ready to start when everything blocking it is closed. See `docs/agents/issue-tracker.md`.

### Making a change

1. Branch from the latest `main`, named after the issue: `ticket-<number>`.
2. Make the change. Keep `plans/current.md`, the specs in `docs/specs/` and `knowledge/` in sync with it.
3. Build and test. The build must show `0 Warning(s)`, and every test must pass.
4. Commit with a conventional message that names the issue, for example `feat(engine): project the Modelo 130 payment (#8)`.
   - Only the repository owner authors commits. No `Co-authored-by` lines and no tool or AI attribution. The `commit-msg` hook enforces this; never bypass it with `--no-verify`.
5. Push and open a pull request with `Closes #<number>` in the description. If you changed a file in `config/tax-years/`, say whether any golden test is affected.
6. CI (`.github/workflows/ci.yml`) builds and tests every pull request. `main` must always be green. Pull requests are squash-merged.

### Things that are easy to get wrong

- **Some files use Windows line endings (CRLF).** Keep them as they are. Before committing, `git diff --stat` and `git diff --stat --ignore-space-at-eol` should show the same numbers. If they differ, an editor changed line endings.
- **A test that only checks "something failed" can pass for the wrong reason.** When a test proves that something is refused, check the specific error, and break the code on purpose once to see the test fail.
- **A new C# idiom gets a one-line comment the first time it appears** in the repository, explaining it in TypeScript terms. The owner is learning C# (`.claude/CLAUDE.local.md`).

## Where to find things

| You want to know | Look in |
|---|---|
| What each part of the system must do | `docs/specs/SPEC-001` to `SPEC-013` |
| Why a decision was taken | `docs/decisions.md` (index) and `docs/adr/` |
| The tax rules the code must respect | `knowledge/business-rules.md` |
| What a Spanish tax term means | `knowledge/glossary.md` |
| Coding and git conventions | `docs/CONVENTIONS.md` |
| The overall design | `docs/architecture.md` |
| What is being worked on now | `plans/current.md` |
| What comes later | `plans/backlog.md`, `plans/DEVELOPMENT_PLAN.md` |
| The golden test cases | `docs/specs/SPEC-011-test-cases.md` |
| The configuration format | `docs/specs/SPEC-007-year-config-schema.md`, `config/tax-years/README.md` |
| The console input format | `src/GestorIA.Cli/README.md` |

The theory behind the rules (explanations and worked examples) is kept outside this repository, in the owner's notes. Specs point to it as `Theory §x.y`.

## Troubleshooting

**`No estimate: ... is declared incomplete in this configuration`.** The year's file does not have a value the calculation needs yet, usually because the law has not been published. The message names the missing value. Use another year, or wait for the value to be published and added.

**`No estimate: $.something is missing` (or `must be ...`).** Your input file has a missing, extra or badly written field. The part after `$` is the path to it in the JSON.

**`ConfigNotFoundException` / "Region XX is not in this configuration".** The region code in your input is not `VC` or `MD`, or the year's file does not include it.

**Tests fail right after pulling, or after editing code that tests depend on.** A stale build can run old code. Rebuild from scratch: `dotnet build GestorIA.slnx --no-incremental`, then run the tests again.

**The build fails with a warning.** Warnings are errors in this repository. Read the warning and fix it; do not turn warnings off.

**A commit is rejected by the hook.** The `commit-msg` hook refuses commits whose author is not your configured `user.email`, and commits with attribution lines such as `Co-authored-by`. Check `git config user.email` and the message.
