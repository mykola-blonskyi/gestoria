# GestorIA

GestorIA is a tax helper for one person living in Spain who is self-employed (autónomo), an employee, or both at once. It runs on your own computer. You tell it about yourself once, feed it your bank statements, and it tells you how much of every payment you receive belongs to Hacienda and the Seguridad Social, what you have to file, and when.

This is a personal project, not a product and not tax advice. The numbers are only as good as the rules and the configuration behind them. Check anything important with a gestor or the AEAT.

## Contents

1. [What GestorIA is and who it is for](#what-gestoria-is-and-who-it-is-for)
2. [What it does today](#what-it-does-today)
3. [What it does not do yet, and known limits](#what-it-does-not-do-yet-and-known-limits)
4. [Set it up from zero](#set-it-up-from-zero)
5. [First use](#first-use)
6. [Everyday tasks](#everyday-tasks)
7. [The console program](#the-console-program)
8. [Tax-year configuration](#tax-year-configuration)
9. [How the project is organised](#how-the-project-is-organised)
10. [Tests](#tests)
11. [Working on the project](#working-on-the-project)
12. [Where to find things](#where-to-find-things)
13. [Troubleshooting](#troubleshooting)

## What GestorIA is and who it is for

Being autónomo in Spain means paying tax in advance, every quarter, on a year you have not finished earning. Part of every client payment is not yours. Some of it goes to Hacienda as the Modelo 130 income-tax advance. Some goes to the Seguridad Social (TGSS) as the monthly RETA cuota. And in spring the annual return (the Renta) may want more on top, especially if you also have a salary. GestorIA answers the question behind all of that. **When a client pays me, how much of that money is not mine?**

It is built for one person: the author, a profesional with EU business clients and US clients, living in the Comunitat Valenciana or the Comunidad de Madrid. It assumes that client mix everywhere. Your clients withhold no retención, and you charge them no IVA. If your situation is different, the figures will be wrong for you.

Everything stays on your machine. The data lives in a PostgreSQL database in Docker, the API that does the calculations runs with `dotnet run`, and the web app runs in your browser with `pnpm dev`. Nothing is sent anywhere else. The app is locked by a key only you know.

GestorIA always leans towards putting aside *more* rather than less. Idle money in a separate account is better than a bill you cannot pay. Every figure comes with the step-by-step calculation behind it, and each step names the law it comes from.

## What it does today

The web app has seven pages, in the order of its menu. It opens in Ukrainian; pick another language from the menu at the top right.

### Overview: the set-aside estimate

The first page answers the main question for the quarter you pick:

- **Hold back X % of every payment you receive.** Move that share of each client payment to a separate account. It covers the Modelo 130 advances, the RETA cuotas and whatever the Renta will want on top, spread over the year's income.
- **Next Modelo 130** for that quarter, with the amount and the days you can file it. A deadline on a weekend or a national or regional holiday moves to the next working day. Town holidays are not known, so a date can be early but never late. When the amount is zero or negative, it says the return is still filed, and whether it goes in as "a deducir" or "negativa".
- **TGSS cuota per month** this quarter, including the tarifa plana while it lasts.
- **Renta on top of the Modelo 130 advances**, and the window to file it.
- **IVA to set aside**, zero for this client mix, with the reason.
- **Notices** to read before trusting the figures, warnings first.
- **How it was calculated**, every step grouped by section, with formulas, real numbers and sources.

A sentence above the estimate says what it is based on. With nothing imported, the projection you entered in Settings covers the whole year. Once your bank movements for a closed quarter are imported and reviewed, the estimate uses what really happened in that quarter instead (see [Transactions](#transactions-import-review-classify)).

### Settings: your profile

You enter your taxpayer profile once: tax year, region, salary, when you registered as autónomo, last year's activity, and what you expect to invoice and spend this year. GestorIA stores it in the database and every page computes from it. [First use](#first-use) explains each field in plain words.

Settings also holds the language and colour theme, and the "Your data" card, which deletes everything stored.

### Transactions: import, review, classify

**Import.** You pick a BBVA statement in CSV format and press Import. GestorIA reads every line and stores the movements it does not have yet. Importing the same statement twice, or one that overlaps an earlier one, adds only what is missing. A file it cannot read is refused whole, with the line numbers and what is wrong on each line. A file over 2 MB is refused.

**The list.** Every movement of the profile's tax year, filtered by quarter and by money in or money out.

**Classification.** Every movement gets one of eight classes:

| Class | Counts in the estimate? |
|---|---|
| Income from my activity | Yes, as the quarter's ingresos. |
| Cuota to the TGSS (RETA) | Yes, as the cuota actually charged. |
| Expense of my activity | Not yet. A deductible expense needs a linked invoice, and GestorIA cannot store invoices yet. |
| Payment to the AEAT | No. |
| Salary from employment | No. Your salary comes from Settings. |
| Interest or dividends | No. |
| Transfer between my accounts | No. |
| Personal, not the activity | No. |

Rules in `config/transaction-rules.json` look at each description. A rule can be *certain*, which settles the movement on its own, or it can only *suggest* a class. A movement no rule matches is *unclear*. Suggested and unclear movements wait in the review queue until you decide. Your decision always wins over a rule.

**Why money coming in is never classified automatically.** Any credit could be income from your activity, and income raises your tax. A rule that guessed wrong would change your figures without you noticing. So the only certain rules are for money going out whose class never enters a figure: payments to the AEAT and everyday personal spending. Every credit, and every TGSS debit, waits for you. The API refuses to start if someone writes a certain rule for a class that counts.

**The review queue** sits above the list. Each movement shows its date, amount, description, the question "Money in: what is it?" or "Money out: what is it?", and one button per class. A rule's suggestion comes first and is marked "A rule suggests: ...". You can work through it with the keyboard only:

- the arrow keys, Home and End move between movements;
- the digit shown on a button picks that class;
- Tab reaches the buttons of the movement you are on.

After each choice the app says "Classified as ..." and moves to the next movement. When the queue is empty, it says "No movements to review".

**When a quarter switches from the projection to what really happened.** All of these must hold:

1. the quarter has ended (today is past its last day);
2. none of its movements is waiting in the review queue;
3. your imported statements cover every day of it and reach past its last day;
4. every earlier quarter of the year since your alta has also switched.

Then the estimate counts that quarter's activity income and the RETA cuotas TGSS actually charged, and keeps the projection only for the months still ahead. The overview says through which quarter it counts actuals, how many movements are still waiting (with a link to them), and how many expenses are not counted because they need an invoice.

### Periods: a quarter or the whole year

Pick **Quarter** to see one quarter's Modelo 130 with every figure next to its box number (casilla) on the AEAT form, taken from the tax year's configuration. Pick **Tax year** to see the annual true-up: the gap between what the Renta will want and the Modelo 130 advances already paid. Both show the calculation step by step and say what they are based on. A result of zero says nothing is to be paid but the return is still filed, as "negativa" or "a deducir".

### Payments: calendar and ICS

Every obligation of the tax year in date order: the monthly TGSS cuota, Modelo 130, 303 and 349 each quarter, and the Renta true-up, each with its window and its amount when GestorIA knows it. Modelo 303 and 349 have no calculator yet, so their amount says "Not known yet". Modelo 349 only applies if you had intra-EU operations that quarter.

**Export to your calendar** downloads an `.ics` file of the dates still to come, for Apple Calendar, Google Calendar or Outlook. Amounts stay out of event titles unless you tick "Include amounts in event titles". When nothing is still to come, as for a tax year that is over, the button is disabled and the page says so.

### Backup: export, restore, delete

- **Download my data** saves one JSON file, `gestoria-export-YYYY-MM-DD.json`, with your profile and every imported movement, classifications included. It is personal financial data. Keep it somewhere only you can open, such as an encrypted drive or your password manager, and do not send it by email. The button appears once something is stored.
- **Restore my data** loads such a file back, for example on a new laptop. It first shows what the file holds (the profile's tax year and region, how many movements and their first and last dates, the export date), and stores nothing until you press Restore. It only restores into an installation that holds nothing. Restoring the same file again changes nothing.
- **Delete everything** is in Settings, in the "Your data" card. It lists what will go, reminds you that Spanish tax law expects you to keep the records behind a return for at least four years after its deadline (ten for a loss or deduction carried forward), and only works once you type the confirmation word of your language (`DELETE`, `BORRAR`, `ВИДАЛИТИ` or `УДАЛИТЬ`).

### Access

The app opens locked and asks for your API key. The key stays only in the memory of that browser tab. Reloading the page, opening a new tab or closing the browser locks the app again. The Access page says the app is unlocked and has a **Lock now** button.

## What it does not do yet, and known limits

- **The annual return (Modelo 100) is not computed.** Periods shows only the annual true-up, the gap beyond the Modelo 130 advances. No tax credits (deducciones) and no savings income are included. Leaving them out can only make the estimate higher, never lower.
- **No forms are produced.** GestorIA computes the figures behind Modelo 130 and knows which casilla each goes in, but you file with the AEAT yourself. Modelo 303 and 349 amounts are not computed.
- **Expenses do not count yet.** A deductible expense needs a linked invoice, and GestorIA cannot store documents yet (no invoices, no OCR). Your expected gastos in Settings stay in the projection.
- **2026 is refused today, and 2027 does not exist yet.** The 2026 configuration declares three values as not published: the tarifa plana amount for 2026, the Renta window for tax year 2026 together with the 2027 holidays, and the Modelo 100 casillas. Every 2026 estimate, period and payments calendar is refused with "Not published yet" until they are. The 2027 values come out in the BOE around December 2026 (issue #12). See [Tax-year configuration](#tax-year-configuration).
- **A closed quarter can stay on the projection.** An import covers the days from the first to the last line it newly stored. Overlapping statements, or monthly statements with quiet days between them, can leave gaps, and then the quarter keeps using the projection. The last quarter of a year needs a statement that reaches into January. Your figures stay safe (they over-reserve), but actuals switch on less often than they should. Issue #88 fixes this.
- **Only BBVA, only CSV.** No other bank, and no BBVA Excel (`.xlsx`) files.
- **One profile per installation, for one tax year at a time.** Movements of other years stay stored and in your backups, but the pages show only the profile's year.
- **Local only.** It runs on one computer. There is no sync, no phone app and no hosted version. The database and the API listen on your own machine only.
- **Town holidays are not applied** to deadlines, so a filing date can be a day early, never late. A TGSS debit date can be a day late, so pay a day early.

## Set it up from zero

These steps take a new Mac to a running GestorIA. Each step says what to type in Terminal and what you should see. Windows and Linux differences are at the end of this section. Plan for about half an hour, most of it downloads.

You end up with three things running: the database (in Docker), the API (in one Terminal window) and the web app (in a second Terminal window).

### Tools

**1. Install the tools.** You need Git, the .NET 10 SDK, Node 24 with pnpm, and Docker Desktop. With [Homebrew](https://brew.sh):

```bash
xcode-select --install                          # Git, if macOS asks for it
brew install --cask dotnet-sdk docker-desktop
brew install node@24
echo 'export PATH="/opt/homebrew/opt/node@24/bin:$PATH"' >> ~/.zshrc
```

Without Homebrew, use the installers from [dot.net](https://dotnet.microsoft.com/download/dotnet/10.0), [nodejs.org](https://nodejs.org) (version 24) and [docker.com](https://www.docker.com/products/docker-desktop/). Then open **Docker Desktop** from Applications once and wait until it says it is running.

Open a new Terminal window and check:

```bash
dotnet --version     # 10.0.something
node --version       # v24.something
docker --version     # Docker version 29 or later
```

Turn on pnpm, the web app's package manager. Node ships it through corepack, which uses the exact version the project asks for:

```bash
corepack enable
```

**2. Get the code.**

```bash
git clone https://github.com/mykola-blonskyi/gestoria.git
cd gestoria
```

Every later command runs from this `gestoria` folder unless it says `web/`.

### Database

The database holds personal financial data, so its password lives in a file outside git (`.env`), and it listens on `127.0.0.1` only.

**3. Create `.env` with a random password.**

```bash
cp .env.example .env
PASSWORD=$(openssl rand -hex 24)
sed -i '' "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$PASSWORD/" .env
```

You can also open `.env` in a text editor and type a long password after `POSTGRES_PASSWORD=`. The same file has `POSTGRES_PORT=5432`. Change it only if something else already uses port 5432.

**4. Start the database.**

```bash
docker compose up -d postgres
docker compose ps
```

The second command should show the `postgres` service `Up ... (healthy)` on `127.0.0.1:5432->5432/tcp`. Docker starts it again whenever Docker Desktop starts. The data lives in a Docker volume and survives restarts.

**5. Tell the API how to reach the database.** The connection string goes into .NET *user secrets*, a file in your home folder (`~/.microsoft/usersecrets/`), outside the repository:

```bash
PASSWORD=$(grep '^POSTGRES_PASSWORD=' .env | cut -d= -f2)
dotnet user-secrets set ConnectionStrings:Gestoria "Host=localhost;Port=5432;Database=gestoria;Username=gestoria;Password=$PASSWORD" --project src/GestorIA.Api
```

On a new machine, the first `dotnet` command first prints a "Welcome to .NET" text about telemetry and a development certificate. That is normal. At the end you should see `Successfully saved ConnectionStrings:Gestoria to the secret store.` If you changed `POSTGRES_PORT`, put that port in place of 5432.

### The API key

The app is locked with a key you choose. The API keeps only the key's SHA-256 hash, never the key itself, and refuses to start without it.

**6. Make a key, keep it, and store its hash.**

```bash
KEY=$(openssl rand -hex 32)
echo "$KEY"
dotnet user-secrets set Auth:ApiKeySha256 "$(printf %s "$KEY" | openssl dgst -sha256 -r | cut -d' ' -f1)" --project src/GestorIA.Api
```

The second line prints the key: 64 letters and digits. Save it in your password manager now. The app asks for it every time you open it, and nothing can recover it. The last line should print `Successfully saved Auth:ApiKeySha256 to the secret store.`

User secrets apply when the API runs with `dotnet run`. Anywhere else, set the environment variables `ConnectionStrings__Gestoria` and `Auth__ApiKeySha256` instead (two underscores).

### The API

**7. Run the API** and leave this Terminal window open:

```bash
dotnet run --project src/GestorIA.Api
```

The first run builds everything, which takes a minute. The API then brings the database up to date and ends with:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5080
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

On the very first start, EF Core also logs a `fail:` line about `__EFMigrationsHistory` just before it creates that table, then `Applying migration ...` lines. That is expected. `Ctrl+C` stops the API.

**8. Check that the API and the database answer.** In a second Terminal window:

```bash
curl -i http://localhost:5080/api/v1/health/ready
```

The first line should be `HTTP/1.1 204 No Content`. A `503 Service Unavailable` means the API runs but the database does not (see [Troubleshooting](#troubleshooting)).

### The web app

**9. Install and run the web app** in that second window, and leave it open:

```bash
cd web
pnpm install
pnpm dev
```

`pnpm install` ends with `Done in ...`. `pnpm dev` prints:

```
▲ Next.js 16.3.6 (Turbopack)
- Local:         http://localhost:3000
✓ Ready in ...
```

The first time, Next.js also prints a note about anonymous telemetry. That is normal.

The API accepts calls from `http://localhost:3000` only. If `pnpm dev` says it uses another port because 3000 is taken, see [Ports](#ports).

**10. Open the app and unlock it.** Go to <http://localhost:3000>. The app opens in Ukrainian on the unlock screen; the language menu (Мова) is at the top right. Paste the key from step 6 and press the button. You should land on the overview, which says there is no taxpayer profile yet and links to Settings.

A wrong key shows "The API did not accept this key. Check it and try again." Other messages are in [Troubleshooting](#troubleshooting).

### Ports

The defaults are the database on 5432, the API on 5080 and the web app on 3000. To use other ports, all three places must agree:

| What | Where to change it |
|---|---|
| Database port | `POSTGRES_PORT` in `.env`, and the `Port=` in the connection string (step 5). |
| API port | Start it with `dotnet run --project src/GestorIA.Api --urls http://localhost:5190`, and put `NEXT_PUBLIC_API_BASE_URL=http://localhost:5190` in `web/.env.local` (copy `web/.env.example`). Restart `pnpm dev` after changing it. |
| Web app port | Start it with `pnpm dev -p 3190`, and start the API with `Cors__Origins__0=http://localhost:3190 dotnet run --project src/GestorIA.Api`. |

### Windows and Linux

- **Windows.** Install [Git for Windows](https://git-scm.com/download/win), the .NET 10 SDK, Node 24 and Docker Desktop (which needs WSL 2). Run every command above in **Git Bash**, which comes with Git and has `openssl`, `grep`, `sed` and `curl`. In step 3, write `sed -i` without the `''`. User secrets live in `%APPDATA%\Microsoft\UserSecrets\` instead of your home folder.
- **Linux.** Install the .NET 10 SDK and Node 24 from your distribution or the official sites, and Docker Engine with the Compose plugin. In step 3, write `sed -i` without the `''`. Everything else is the same.

## First use

Unlock the app (step 10 above), then work through these in order.

### 1. Fill in your profile in Settings

Open **Settings**. The "Your taxpayer profile" card asks for:

| Field | What to put |
|---|---|
| **Tax year** | The year you want figures for. A new profile starts on the newest year GestorIA can compute. A year with unpublished values is listed with what is missing. |
| **Region** | Where you live: Comunitat Valenciana or Comunidad de Madrid. For income tax this is where you spent most days of the year. |
| **Gross salary** | Your whole year's gross pay as an employee, before anything is taken off. `0.00` if you have no job. |
| **Your own Seguridad Social on the salary** | The employee's Seguridad Social taken from your pay over the year, as your payslips or the employer's certificate show it. `0.00` without a job. |
| **Date of alta** | The day you registered as autónomo. |
| **Activity last year** | "No activity last year", or "Activity with a known net" and last year's rendimiento neto: what the activity earned minus its expenses. Negative after a loss. |
| **New activity (LIRPF art. 32.3)** | If you started recently with no activity before, the net of your first year with a profit, and of the year after it, is reduced by 20 %. Pick "First period with a positive net", "The period after it", or "Not a new activity". |
| **Ingresos from last year's employer** | Shown for a new activity. What you expect to invoice this year to a company that paid you a salary last year. If that is more than half of your activity income, the 20 % reduction does not apply. |
| **Expected ingresos** | What you expect to invoice in the whole year, for the months you are registered. Without IVA. |
| **Expected gastos, without the RETA cuota** | What you expect the activity to spend in the year. Leave the Seguridad Social cuota out; GestorIA adds it. |
| **Monthly base de cotización you pay** | The monthly contribution base you chose with the TGSS (in Import@ss), in euros of base, not the cuota. It is ignored while the tarifa plana lasts. |

Press **Save the profile**. You should see "Saved. The overview now estimates from this profile." Save again whenever something changes; there is only ever one profile.

### 2. Read the overview

Open **Overview**. It opens on the current quarter, or Q4 for a year that is over. The first sentence says what the figures rest on, for example:

```
Estimated from your profile for 2025, with no closed quarter recorded: the projection covers the whole year.
```

Read the notices first, then the figures. [Overview](#overview-the-set-aside-estimate) above explains each line. "How it was calculated" opens every step of the calculation.

### 3. Import a BBVA statement

**Get the file from BBVA.** In BBVA online banking, open your account's movements, choose the dates you want (a whole quarter or year is best), and download them. GestorIA reads only the CSV format. The file must start with this header line, then one movement per line:

```
Fecha;Fecha Valor;Concepto;Importe;Saldo
02/01/2025;02/01/2025;TRANSFERENCIA RECIBIDA CLIENTE SINTETICO UNO;2.345,67;8.345,67
```

Dates are `dd/mm/yyyy`, amounts are Spanish style (`2.345,67`), and fields are separated by `;`. If BBVA gives you an Excel file (`.xlsx`), GestorIA refuses it with "The file is an XLSX workbook or another ZIP archive; export the statement as CSV." Open it in a spreadsheet set to Spanish, delete any rows above the header, keep those five columns in that order, and save it as CSV separated by semicolons. `tests/fixtures/bank/bbva-2025-synthetic.csv` in this repository is a made-up example of the right shape.

Keep your statements outside this folder. They are personal financial data and must never be committed.

**Import it.** Open **Transactions**, press "Statement file (CSV)", pick the file, leave Bank on BBVA, and press **Import**. With the example file you see:

```
18 movements read: 18 new, 0 already imported.
```

and a heading such as "13 movements to review".

### 4. Work through the review queue

Click the first movement in the queue (on its text, not a button), or Tab into it. Then, for each movement, press the digit of the right class. The next movement comes up by itself. For example, with the example file:

| Movement | Press | Class |
|---|---|---|
| TRANSFERENCIA RECIBIDA CLIENTE SINTETICO UNO, €2,345.67 | `1` | Income from my activity |
| CUOTA AUTONOMOS TGSS, -€87.61 (suggested) | `1` | Cuota to the TGSS (RETA) |
| COMPRA SUSCRIPCION SOFTWARE EJEMPLO, -€23.79 | `2` | Expense of my activity |
| RECIBO LUZ; FEBRERO, -€64.32 | `8` | Personal, not the activity |
| TRANSFERENCIA A ES12 ..., -€150.00 | `7` | Transfer between my accounts |

The digits follow the buttons on screen. On a movement with a suggestion, `1` is the suggestion, so the digit for a class can change from one movement to the next. Look at the buttons before you press. Each choice shows "Classified as ...", and the last one leaves "No movements to review".

Go back to **Overview**. The first sentence now names the actuals, for example:

```
Estimated for 2025 from actuals through Q3, 5 classified movements; the projection covers the rest of the year.
2 expenses await an invoice and are not counted: a deductible expense needs a linked invoice, and GestorIA cannot store invoices yet.
```

Here Q4 stays on the projection because the example statement ends on 31 December and does not reach past the quarter (see [known limits](#what-it-does-not-do-yet-and-known-limits)).

You can change a classification later. Only your decision is stored, and the latest one counts.

### 5. Read Periods and Payments

Open **Periods**. With **Quarter**, pick a quarter to see its Modelo 130 box by box, for example "Casilla 01 · Income since 1 January (ingresos)". With **Tax year**, see the gap the Renta will add, with the note that the full Modelo 100 is not computed yet.

Open **Payments** for every date of the year. Use it to plan cash, and file each return inside its window.

### 6. Export the calendar

On **Payments**, under "Export to your calendar", tick "Include amounts in event titles" if you want them, and press **Download .ics**. Open the file with your calendar app. It holds only the dates still to come. For a tax year that is over, the button is disabled and the page says "Nothing is still to come in this tax year, so there is no date to export."

### 7. Make a backup

Open **Backup** and press **Download my data (personal financial data)**. Your browser saves `gestoria-export-YYYY-MM-DD.json`. Move it somewhere only you can open. Make a new one after every import session.

## Everyday tasks

### Start GestorIA after a restart

1. Open Docker Desktop. The database starts with it.
2. In one Terminal window: `cd gestoria`, then `dotnet run --project src/GestorIA.Api`.
3. In another: `cd gestoria/web`, then `pnpm dev`.
4. Open <http://localhost:3000> and paste your key.

### Add a new statement

Download the new period from BBVA and import it on **Transactions**. Overlapping an earlier statement is fine; only new lines are stored. Classify what lands in the queue. The overview switches each finished quarter to actuals once it is reviewed and your statements cover it.

### Move to a new laptop

1. On the old laptop, make a backup on **Backup**.
2. On the new one, follow [Set it up from zero](#set-it-up-from-zero). You can pick a new key.
3. Unlock, open **Backup**, choose the file under "Restore my data from an export", check what it says it holds, and press **Restore**. You should see a line like "Restored: your profile for 2025 and 18 bank movements." with your own numbers.

Restore works only into an empty installation. If it refuses because data is already stored, download a copy of that data first, delete it in Settings, then restore.

### Delete everything

1. Download a backup first if you may need the data. Spanish law expects you to keep tax records for at least four years.
2. Open **Settings**, find "Your data", type the confirmation word shown (for example `DELETE` in English), and press **Delete everything**.
3. You should see "Everything was deleted. Nothing about you is stored on this installation any more."

To also remove the database itself, stop the API and run `docker compose down -v` from the `gestoria` folder. This deletes the Docker volume for good.

### Change the language or the theme

Use the two menus at the top right of every page, or the "Appearance and language" card in Settings. The languages are Ukrainian (the default), Spanish, English and Russian. The themes are light, dark, sepia, high contrast and ocean. This browser remembers both in cookies. The app stays unlocked when you switch.

### Start a new tax year

GestorIA holds one profile, for one tax year at a time.

1. Make a backup of the year you are leaving.
2. In **Settings**, pick the new **Tax year**, check every field (the salary, last year's net, the new-activity period and the projection all change from year to year), and save.
3. Import the new year's statements.

Movements of other years stay stored and in your backups, but the pages show only the profile's year. Settings lists a year whose rules are not published yet with what is missing, and its estimate is refused until the configuration is updated. Today that is 2026, and 2027 has no configuration yet.

### Change the API key

Run step 6 of the setup again with a new key, then stop the API with `Ctrl+C` and start it again. An open tab goes back to the unlock screen at its next request.

## The console program

The console prints the same set-aside estimate from a JSON file, without the database, the API or the web app. It is handy for trying figures quickly.

```bash
dotnet run --project src/GestorIA.Cli -- <input.json> <tax-year-config.json>
```

- `<input.json>` describes you: region, salary, when you registered as autónomo, what you have invoiced so far and what you expect for the rest of the year.
- `<tax-year-config.json>` is the year's rules, for example `config/tax-years/2025.json`. You always name the file yourself. The console never picks a year for you, so every answer says which file produced it.

Try it with the made-up example:

```bash
dotnet run --project src/GestorIA.Cli -- src/GestorIA.Cli/set-aside-input.example.json config/tax-years/2025.json
```

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

   The full description of every field is in [`src/GestorIA.Cli/README.md`](src/GestorIA.Cli/README.md). The same file is the body of the API's `POST /api/v1/set-aside/estimate`.

3. **Run it.**

   ```bash
   dotnet run --project src/GestorIA.Cli -- ~/gestoria-private/my-2025.json config/tax-years/2025.json
   ```

### What comes out

The console prints three parts, in this order:

1. **The calculation**, every step numbered, with its inputs, the formula with real numbers, the result and the law it comes from. Figures here are shown to the cent; the engine keeps them unrounded.
2. **The estimate**, a short block with the figures you act on.
3. **Notices**, warnings first. Read these before trusting the figures. They are printed last so they stay on screen.

A real run of the example prints this estimate:

```
Estimate
  Hold back from every payment received   19.47 %
  Next Modelo 130, Q2                     1507.40 €, due 2025-07-01 to 2025-07-21
                                          (municipal holidays where the taxpayer lives are not applied, so the date shown can be early but never late)
  Cuota SS per month this quarter         80.00 €
  Annual return (Renta) gap               0.00 €, payable by the end of 2026-06
  IVA to set aside                        0.00 €
  Tax year                                2025
  Configuration                           2025.json, sha256 929fff34adecb4c34da92a22c6523a3b4973a9efc6ed55e237a1464bf7554e21
```

| Line | Meaning |
|---|---|
| **Hold back from every payment received** | Move this share of every client payment to a separate account. |
| **Next Modelo 130** | The next quarterly income-tax advance and the days you can pay it. |
| **Cuota SS per month** | What TGSS will take from your account each month this quarter. |
| **Annual return (Renta) gap** | What the tax return will want on top of the Modelo 130 advances. With a salary as well, this is often large, because the self-employed income is taxed at your top rate, not at the flat 20 % of Modelo 130. |
| **IVA to set aside** | Zero when all your clients are EU businesses or outside the EU. EU business clients account for the IVA themselves, and US clients are outside Spanish IVA. |
| **Tax year / Configuration** | Which rules produced the answer. The SHA-256 changes whenever the file changes, so two answers with the same code came from exactly the same rules. |

### Exit codes

| Code | Meaning |
|---|---|
| `0` | An estimate was printed. |
| `1` | The input file, the configuration or the engine refused. The reason is printed, for example `$.activity.projection.ingresos is "-5"; it must be zero or more.` |
| `2` | The command was used wrongly (for example, a file is missing from the command line). |

The console is refused on `2026.json` just like the web app, because it runs the same estimator (see the next section).

## Tax-year configuration

Each tax year has one file: `config/tax-years/2025.json`, `config/tax-years/2026.json`. Each holds that year's tax scales, allowances, Seguridad Social tables, Modelo 130/303/349 details, deadlines and holidays, for both regions. No tax number is written in the code.

**Every value says where it comes from.** The `provenance` block in each file points each value to its source, usually an article in the BOE, and says when it was checked. See SPEC-007 §1.1.

**Missing values are declared, never guessed.** When a value has not been published yet, the file marks it with a `_todo` note explaining what is missing. The engine then refuses any calculation that would need it, and says why, instead of borrowing last year's value. Today `2026.json` has three such gaps:

- the tarifa plana amount for months of 2026 (no law has set it yet);
- the Renta window for tax year 2026 and the 2027 holidays, which the Q4 2026 Modelo 130 deadline needs;
- the Modelo 100 casillas for 2026.

The estimate always works out the whole year's annual true-up next to the quarter you ask for, so every 2026 estimate is refused, Q1 to Q3 included. That covers the overview, Periods, Payments and the console. Which gap you see depends on the profile. With an alta in 2026 the engine stops at the tarifa plana:

```
seguridadSocial.tarifaPlana.amount, the cuota for 2026-01 under tarifa plana, is declared incomplete in this configuration: ...
```

With an older alta it stops at the calendar:

```
calendar.modelo130 Q4 of tax year 2026 ends on 2027-01-30, and this configuration declares the calendar after 2026 incomplete: ...
```

The API answers these as `422` with the problem type `config-gap`. The web app shows them as "Not published yet" with the engine's reason and a link back to Settings.

**The files are checked every time they load.** `schema.json` checks the shape, and extra rules in `src/GestorIA.Infrastructure/TaxYears/TaxYearRules.cs` check what a schema cannot: that scales go up in order, that Seguridad Social bands do not leave gaps, that every provenance entry points at a real value, and more (SPEC-007 §2). A file that breaks a rule is rejected with a message naming the exact place.

**Adding a new tax year** (for example 2027, issue #12):

1. Copy the previous year's file to `config/tax-years/YYYY.json` and set `taxYear`.
2. Replace every value that changed, reading the new law in the BOE, and update its `provenance` entry with the source and today's date.
3. Mark anything not yet published with `_todo`, and register it in `tests/GestorIA.Engine.Tests/TaxYearGapsAreRegistered.cs`.
4. Run `dotnet test GestorIA.slnx`. The configuration tests load every file in the folder automatically.
5. In the pull request, list every value that changed from the previous year and say whether any golden test is affected (`docs/CONVENTIONS.md`).

The full runbook is SPEC-007 §4.

## How the project is organised

```
config/
  tax-years/                one JSON file per tax year, plus schema.json that checks them
  transaction-rules.json    the rules that classify bank movements (SPEC-004 §3)
src/
  GestorIA.Domain/          basic types (Money, Rate) and a bank statement line (BankTransaction)
  GestorIA.Engine/          every tax calculation; pure code, no files, no network, no clock
  GestorIA.Infrastructure/  reads and checks the configuration files and the console's input file; the database
                            (EF Core and its migrations); the BBVA statement parser; the classification rules
  GestorIA.Cli/             the console program
  GestorIA.Api/             the web API (/api/v1) and its OpenAPI document, openapi/v1.json
tests/
  GestorIA.Api.Tests/       the API over HTTP against a real PostgreSQL (Testcontainers), and its OpenAPI document
  GestorIA.Domain.Tests/    Money, Rate, the BBVA parser and the classification rules
  GestorIA.Engine.Tests/    calculator tests, configuration checks, golden tests
  GestorIA.Cli.Tests/       input reading and output formatting of the console
  golden/2025/              golden cases: full scenarios with their expected results
  fixtures/bank/            a synthetic BBVA statement
web/                the web app (Next.js); see web/README.md
compose.yaml        the local database (PostgreSQL 16) in Docker
docs/               specifications (specs/), decisions (adr/), conventions, architecture
knowledge/          business rules, domain model, glossary of Spanish tax terms
plans/              current plan, backlog, development plan
services/ocr/       planned document-reading service (not built)
```

The browser talks only to the API, on `http://localhost:5080/api/v1`, and sends the key in the `X-Api-Key` header. The API reads the stored profile and movements from PostgreSQL, turns them into the engine's input, runs the engine, and returns the result with its trace. The web app never computes tax itself (ADR-0017). Every endpoint except `/api/v1/health/live` and `/api/v1/health/ready` needs the key.

A few rules the code follows everywhere:

- **Money is always `decimal`, never `double` or `float`.** Using a floating-point type for money does not compile (ADR-0004).
- **Nothing is rounded until the very end.** Rounding to the cent happens only where a figure goes into a form box or is shown to you.
- **The engine is pure.** It gets everything it needs as input and returns a result plus a step-by-step trace. The same input always gives exactly the same output.
- **No tax number in code.** Every rate, threshold and date comes from `config/tax-years/`.
- **Nothing unconfirmed enters a calculation.** A movement counts only once you, or a certain rule, have classified it (`knowledge/business-rules.md`).
- **Spanish tax terms stay in Spanish** in the code (`CuotaIntegra`, `Retencion`), so they match the AEAT forms. Everything else is in English.

## Tests

From the repository root, with Docker running:

```bash
dotnet restore GestorIA.slnx
dotnet build GestorIA.slnx --no-restore
dotnet test GestorIA.slnx --no-build
```

A clean build prints `0 Warning(s)` and `0 Error(s)`; warnings are errors in this repository. `dotnet build` also rewrites the API's OpenAPI document, `src/GestorIA.Api/openapi/v1.json`, which is committed. The API's tests start a throwaway PostgreSQL from the image in `compose.yaml` and remove it when they finish.

For the web app, in `web/`:

| Command | Why |
|---|---|
| `pnpm install --frozen-lockfile` | Installs exactly the locked versions, as CI does. |
| `pnpm lint` | Checks the code, including the rules on which folder may import which. Warnings fail. |
| `pnpm typecheck` | Checks the TypeScript types. |
| `pnpm test` | Runs the web tests. |
| `pnpm build` | Builds the production app, as CI does. |
| `pnpm api:types` | Regenerates `src/data/api-types.ts` from the OpenAPI document, after `dotnet build` has refreshed it. |

There are four kinds of .NET tests:

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

1. Turn on the repository's git hooks once per clone: `git config core.hooksPath .githooks`. The `commit-msg` hook rejects commits not authored by your configured `user.email`, and commits with attribution lines.
2. Branch from the latest `main`, named after the issue: `ticket-<number>`.
3. Make the change. Keep `plans/current.md`, the specs in `docs/specs/`, `knowledge/`, this README and `web/README.md` in sync with it.
4. Build and test as [Tests](#tests) shows. The build must show `0 Warning(s)`, and every test must pass. If you changed the API, run `pnpm api:types` in `web/` and commit both generated files.
5. Commit with a conventional message that names the issue, for example `feat(engine): project the Modelo 130 payment (#8)`. Only the repository owner authors commits. No `Co-authored-by` lines and no tool or AI attribution. Never bypass the hook with `--no-verify`.
6. Push and open a pull request with `Closes #<number>` in the description. If you changed a file in `config/tax-years/`, say whether any golden test is affected.
7. CI (`.github/workflows/ci.yml`) builds and tests every pull request, checks that the OpenAPI document and `web/src/data/api-types.ts` are current, and fails a document that breaks the one on `main`. `main` must always be green. Pull requests are squash-merged.

### Things that are easy to get wrong

- **Some files use Windows line endings (CRLF).** Keep them as they are. Before committing, `git diff --stat` and `git diff --stat --ignore-space-at-eol` should show the same numbers. If they differ, an editor changed line endings.
- **A test that only checks "something failed" can pass for the wrong reason.** When a test proves that something is refused, check the specific error, and break the code on purpose once to see the test fail.
- **A new C# idiom gets a one-line comment the first time it appears** in the repository, explaining it in TypeScript terms. The owner is learning C# (`.claude/CLAUDE.local.md`).
- **Real personal data never goes into the repository**, not in fixtures, logs or examples (SPEC-013). Git ignores `.env` and `web/.env.local`.

## Where to find things

| You want to know | Look in |
|---|---|
| What each part of the system must do | `docs/specs/SPEC-001` to `SPEC-013` |
| Why a decision was taken | `docs/decisions.md` (index) and `docs/adr/` |
| The tax rules the code must respect | `knowledge/business-rules.md` |
| What a Spanish tax term means | `knowledge/glossary.md` |
| Coding and git conventions | `docs/CONVENTIONS.md` |
| The overall design | `docs/architecture.md` |
| How the web app is built | `web/README.md` |
| The API's endpoints | `docs/specs/SPEC-009-api.md`, `src/GestorIA.Api/openapi/v1.json` |
| How movements are classified | `docs/specs/SPEC-004-transaction-classifier.md`, `config/transaction-rules.json` |
| What is being worked on now | `plans/current.md` |
| What comes later | `plans/backlog.md`, `plans/DEVELOPMENT_PLAN.md` |
| The golden test cases | `docs/specs/SPEC-011-test-cases.md` |
| The configuration format | `docs/specs/SPEC-007-year-config-schema.md`, `config/tax-years/README.md` |
| The console input format | `src/GestorIA.Cli/README.md` |

The theory behind the rules (explanations and worked examples) is kept outside this repository, in the owner's notes. Specs point to it as `Theory §x.y`.

## Troubleshooting

### Starting the API

**`ConnectionStrings:Gestoria is not set; README.md, "Database", shows how to set it.`** The API stops at once. Do step 5 in [Database](#database). The API checks this before the key.

**`Auth:ApiKeySha256 must be the SHA-256 of a non-empty local API key, as 64 hex characters`.** The key hash is missing or malformed. Do step 6 in [The API key](#the-api-key). The value must be the 64-character hash, not the key itself.

**The database is not reachable**, and `/api/v1/health/ready` answers `503`:

```
{"type":"https://gestoria.local/problems/database-unavailable","title":"The database is not reachable","status":503,"detail":"The API is running but cannot reach its database. Start PostgreSQL (docker compose up -d postgres) and try again.", ...}
```

Start Docker Desktop, run `docker compose up -d postgres`, and check that `docker compose ps` says `healthy`. If it still fails, the password or port in the connection string does not match `.env`; run step 5 again.

**`address already in use` for port 5080 or 5432.** Something else uses the port. Pick another one as [Ports](#ports) shows.

### Unlocking the web app

**"The API did not accept this key. Check it and try again."** The key does not match the stored hash. Paste it again without spaces. If you lost it, make a new one (step 6) and restart the API.

**"The GestorIA API is not running."** Either the API is stopped (start it, step 7), or the browser was not allowed to call it. The second case happens when the web app runs on a port the API does not accept (CORS), or when `web/.env.local` points at the wrong API port. The browser's developer console then shows `blocked by CORS policy`. Make the ports agree as [Ports](#ports) shows, and restart both.

**"The GestorIA API is running, but its database is not."** Start the database with `docker compose up -d postgres`.

**The app asks for the key again.** That is by design. The key lives only in the memory of the tab, so a reload, a new tab or a browser restart locks the app.

**A page says "The GestorIA database is not answering."** The database stopped while you worked. Start it again and reload.

### Figures and pages

**"Not published yet" on the overview, Periods or Payments** (the API's `422 config-gap`). The profile's tax year needs a value its configuration marks as unpublished. Today that is every 2026 estimate. Pick 2025 in Settings, or wait for the value to be published and added. See [Tax-year configuration](#tax-year-configuration).

**A finished quarter stays on the projection.** Check, in this order:

1. Transactions shows no movement of that quarter in the review queue.
2. Every earlier quarter since your alta has switched already.
3. Your imported statements cover every day of the quarter and have a line after its last day. For Q4, import a statement that reaches into January.

Under "How it was calculated", the step "Trimestre no cubierto por los movimientos importados" (`ledger.coverage`) names the first day no import covers. A gap left by overlapping or monthly statements does not close by importing them again (issue #88).

**The ICS download is disabled** and the page says "Nothing is still to come in this tax year". Every date of that tax year has passed. The API answers the same request with `422 no-upcoming-obligations`.

**An import is refused.** The page lists each reason. "The first line is not the header of a BBVA CSV statement" means extra rows sit above `Fecha;Fecha Valor;Concepto;Importe;Saldo`, or the separator is not `;`. "The file is an XLSX workbook" means the file is Excel; save it as CSV (see [Import a BBVA statement](#3-import-a-bbva-statement)).

**Restore is refused because the installation is not empty.** Download what is stored, delete it in Settings, then restore.

### Console

**`No estimate: ... is declared incomplete in this configuration`.** The year's file does not have a value the calculation needs yet, usually because the law has not been published. The message names the missing value. Use another year, or wait for it to be added.

**`No estimate: $.something is missing` (or `must be ...`).** Your input file has a missing, extra or badly written field. The part after `$` is the path to it in the JSON.

**`ConfigNotFoundException` / "Region XX is not in this configuration".** The region code in your input is not `VC` or `MD`, or the year's file does not include it.

### Building and testing

**The API's tests fail with a Docker error.** They start their own PostgreSQL in Docker. Start Docker Desktop and run them again.

**The API's tests fail with `53300: too many clients`.** Too many database connections were open at once in the shared test container. Run one test run at a time, rebuild from scratch (`dotnet build GestorIA.slnx --no-incremental`) and run the tests again.

**Tests fail right after pulling, or after editing code that tests depend on.** A stale build can run old code. Rebuild with `--no-incremental`, then run the tests again.

**The build fails with a warning.** Warnings are errors in this repository. Read the warning and fix it; do not turn warnings off.

**`pnpm dev` wrote `web/AGENTS.md` or `web/CLAUDE.md`.** Next.js writes these when it detects an AI coding agent in the terminal. They are not part of the project. Delete them and do not commit them.

**A commit is rejected by the hook.** The `commit-msg` hook refuses commits whose author is not your configured `user.email`, and commits with attribution lines such as `Co-authored-by`. Check `git config user.email` and the message.
