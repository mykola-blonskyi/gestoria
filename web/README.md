# GestorIA web app

The browser side of GestorIA: a Next.js App Router application that shows the engine's answers in Ukrainian, Spanish, English or Russian. It runs on your machine next to the API (ADR-0010). It never computes tax; every figure comes from the engine through the API (ADR-0017, SPEC-012).

Settings edits the taxpayer profile the API stores and deletes everything stored, and the overview shows the set-aside estimate the API computes from it (#66, #69, #74). Transactions imports a bank statement into that profile and lists its movements (#72). Backup downloads all of it in one file (#74) and restores that file into an empty installation (#75). The app opens locked and unlocks with the API key of your installation (#68). Every other page is an honest empty state that says what it will show.

## Run it

You need Node 24 and pnpm (the version is pinned in `package.json`, so `corepack enable` is enough).

```bash
cd web
pnpm install
pnpm dev          # http://localhost:3000
```

The app needs the API: `dotnet run --project src/GestorIA.Api` from the repository root, once its key (below) and its database (the root `README.md`, "Database") are set up. It listens on `http://localhost:5080` and allows calls from `http://localhost:3000` (`Cors:Origins` in its `appsettings.json`; set `Cors__Origins__0` when `pnpm dev` takes another port).

### The API key

Every API endpoint but the health checks refuses a request without the local API key (SPEC-009 §3). Unlocking checks the key, then `/health/ready`, so a stopped database is named on the unlock screen rather than on every page after it. The API is configured with the key's SHA-256, never the key itself (SPEC-013), and refuses to start without it. Set it up once per machine, from the repository root:

```bash
KEY=$(openssl rand -hex 32)
echo "$KEY"    # keep this in your password manager; the app asks for it
dotnet user-secrets set Auth:ApiKeySha256 "$(printf %s "$KEY" | shasum -a 256 | cut -d' ' -f1)" --project src/GestorIA.Api
```

User secrets live in your home directory (`~/.microsoft/usersecrets/`), outside the repository, and the API reads them when it runs as Development, which `dotnet run` does. Anywhere else, set the `Auth__ApiKeySha256` environment variable instead. To change the key, run the three lines again and restart the API; an open tab then returns to the unlock screen at its next request.

The app shows an unlock screen until the API accepts the key: it sends the key to `GET /config/tax-years` and unlocks on a 200. A wrong key, and an API that is not running, each get their own message; the second says how to start it. Once unlocked, `apiFetch` sends the key in the `X-Api-Key` header on every request, and a 401 to the key in use locks the app again with a message saying why.

**Where the key lives, and why.** Only in the memory of the browser tab (`src/data/api-key-store.ts`), for as long as the tab is open. A reload, a new tab or closing the browser forgets it, and the app asks again. It is never written to localStorage, sessionStorage, a cookie or the address, and never logged (SPEC-013); `features/auth/tests/auth-gate.test.tsx` checks all of these, and the input has no `name`, so a submit before the page is interactive sends nothing. The alternative the ticket offered, an httpOnly cookie set by a Next.js route handler, was rejected: the browser calls the API directly on another origin (CORS, #66), so a cookie would need `credentials: "include"` on every call plus a defence against cross-site request forgery, or a proxy in Next.js in front of the whole API. For one user on one machine, typing the key after a reload costs less than either. sessionStorage would survive a reload, but it is storage that any script on the page can read and the browser can write to disk.

### The taxpayer profile

Settings edits the profile (`features/settings`), entered once and stored by the API (`/profiles`, SPEC-009 §2). Local mode keeps one per installation: the first save is a `POST`, every later one a `PUT` to it. The overview (`features/dashboard`) reads it and asks the API for `GET /profiles/{id}/set-aside/estimate?asOf=Qn`, so the browser never sends the profile back to be computed and never computes anything itself. With no profile yet, the overview sends the user to settings.

- **Which quarter.** The overview opens on today's quarter when the profile's tax year is the current one, Q4 for a year that is over and Q1 for one to come, and never before the quarter of the alta, which the engine refuses. The user can pick another.
- **Which tax year a new profile starts on.** The newest year whose configuration declares no gap (`GET /config/tax-years`, `gaps`), else the newest. A year with gaps stays selectable, and the form lists them under the year. Today that means 2025: 2026.json declares its renta window and the Q4 Modelo 130 deadline unpublished (and the tarifa plana amount), and the estimate needs both whatever the quarter, so every 2026 estimate is a 422 `config-gap` until they are published. The overview shows that as "not published yet", with the engine's reason and a link back to settings to pick another year.
- **No closed quarter yet.** The profile has no actuals; they are ledger data and arrive with transactions. Until then the projection covers every month of alta in the year.
- **Where it lives.** In the API's database, and in this tab's query cache while the tab is open, which the lock empties. Never in browser storage, cookies, the address or the console (`features/settings/tests`, `features/dashboard/tests`).

### Transactions

Transactions (`features/transactions`) imports a bank statement into the stored profile and lists what it holds (`/profiles/{id}/bank-statements` and `/profiles/{id}/transactions`, SPEC-009 §1.1).

- **Import.** Pick the CSV file and the bank (BBVA only so far). The browser sends the file itself as the body; a file over 2 MB is refused on the page before it is sent, as the API would refuse it. The answer says how many movements the file held, how many were new and how many were already stored: importing the same statement again, or one that overlaps it, adds only what is missing. A file the API cannot read is refused whole, and each reason it gives (a line number and what is wrong there, never the line itself) is listed.
- **Filters.** The list covers the profile's tax year. The period filter asks the API for one quarter; the kind filter (money in, money out) narrows what is already loaded, from the sign of each amount string, so switching it costs no request.
- **The list.** TanStack Virtual through `shared/ui/virtual-list.tsx`: only the rows in view are in the page, so a year of movements scrolls without delay. Dates and amounts are formatted for the page's language from the API's strings.
- **Where it lives.** In the API's database and in this tab's query cache. Never in browser storage, cookies, the address or the console (`features/transactions/tests`).

### Export, restore and delete

- **Download** (`features/backup`, #74). Backup fetches `GET /profiles/{id}/export` when the button is pressed and hands the document to the browser as `gestoria-export-YYYY-MM-DD.json`, dated by the day in Madrid, the name the API gives it too. It holds the profile and every stored bank movement (#72). The API needs the key in a header, so a plain link cannot fetch the file: it is fetched, turned into a `Blob` and offered through an object URL that is revoked once the download has started. The export is a mutation, not a query, with `gcTime: 0`, so the whole of the user's data never sits in the query cache and leaves the mutation cache once the file is handed over. Everywhere it is offered it is labelled as personal financial data, with a note on where to keep it. The format is SPEC-009 §2.1.
- **Restore** (`features/backup`, #75). Backup reads the chosen file in the browser and shows what it holds before anything is sent: the taxpayer profile's tax year and region, how many bank movements and the first and last booking date, and the day it was exported. A file that is not JSON, not a GestorIA export, of a format version this app does not read, or over 16 MB (the API's limit, `EXPORT_MAX_BYTES`, checked before the file is read) is refused on the page and nothing is sent. Only once the user presses Restore does it send `POST /profiles/restore` with the file's text exactly as read (SPEC-009 §2.2), and the API validates the whole file then. It stores only into an empty installation: when anything is stored, the API refuses with what it holds and the page links to settings to delete it first, after downloading a copy. Restoring the same file again is harmless: the API finds exactly what the file holds and writes nothing. The file's text lives in the component's state until it is sent or cancelled, then as the variables of a mutation with `gcTime: 0`, which leaves the mutation cache once it is done; never in the query cache, browser storage, cookies, the address or the console. A restore invalidates `["profiles"]`, so the download card finds the restored profile.
- **Delete** (`features/settings`, the "Your data" card). It lists what will be deleted, says it cannot be undone and that Spanish tax law expects the records behind a return to be kept at least four years after its deadline, and ten for a loss or deduction carried forward (SPEC-013 §2), and deletes only once the user types the confirmation word of their language (`DELETE`, `BORRAR`, `ВИДАЛИТИ`, `УДАЛИТЬ`). A delete resets every query under `["profiles"]`, so the profile and its estimates leave the cache with it and the profile form starts over. The card links to Backup for a copy first.
- The delete lists the profile and every imported bank movement. A new stored entity kind adds a line there and nothing to the download, which saves whatever the API exports.

| Command | What it does |
|---|---|
| `pnpm dev` | Development server with hot reload. |
| `pnpm build` then `pnpm start` | Production build and server. |
| `pnpm lint` | ESLint, including the layer rules below. Warnings fail. |
| `pnpm typecheck` | Generates the route types, then `tsc`. |
| `pnpm test` | Vitest with Testing Library and jsdom. |
| `pnpm api:types` | Generates `src/data/api-types.ts` from the API's OpenAPI document (see below). |

CI runs install (from the lockfile), a check that `src/data/api-types.ts` is what `pnpm api:types` generates, lint, typecheck, test and build in the `web` job of `.github/workflows/ci.yml`.

### Configuration

| Variable | Default | Meaning |
|---|---|---|
| `NEXT_PUBLIC_API_BASE_URL` | `http://localhost:5080` | Where the API listens. Requests go to `<base>/api/v1/...`. It is inlined into the browser bundle at build time, so it never holds a secret. |

Copy `.env.example` to `.env.local` to change it.

## Structure

```
src/
  app/          routes and layouts only
  data/         fetch client, OpenAPI types, TanStack Query options per API resource
  features/     one directory per capability:
    <name>/       dashboard, payments, transactions, periods, settings, auth, backup
      components/
      hooks/      data queries and mutations assembled for the feature
      tests/
      index.ts    the feature's only public entry
  shared/       the bottom layer
    lib/          cn, money and date formatting, the preference cookie
    ui/           shadcn/ui components (components.json points `ui` here) and small building blocks
    types/        hand-written types unrelated to the API
    constants/    locales, navigation
    shell/        header, navigation, disclaimer, language toggle
    theme/        theme list, provider and toggle
  i18n/         next-intl request config and the four message files
tests/          checks on the whole app: layer rules, theme contrast, folder structure
```

`@/` is `src/`. `tests/structure.test.ts` fails if a layer, a feature or a feature's folder goes missing.

### Layer rules

Enforced by ESLint's `import/no-restricted-paths` in `eslint.config.mjs`. `tests/layers.test.ts` lints a violating import for each rule and checks that it is reported.

| Rule | Why |
|---|---|
| Nothing imports `app`. | Routes are leaves. |
| `data` imports only `shared`. | The API layer knows nothing about screens. |
| `shared` imports nothing from `app`, `features`, `data` or `i18n`. | It is the bottom layer. |
| `i18n` imports only `shared`. | Locales and cookie names live in `shared/constants`. |
| Features do not import each other. | What two features share moves to `shared` or `data`. |
| `app` imports a feature only through its `index.ts`. | A feature can change inside without breaking routes. |

The zones for "features do not import each other" are generated from the folders in `src/features`, so a new feature is covered without editing the config.

`no-console` is an error in `src`, for every file type there: nothing about the user's finances may reach the browser console (SPEC-013). For the same reason `ApiError` messages carry only the status and the problem title, never `detail`. The error object itself still holds `detail` in its `failure`, so a feature shows an API error on the page and never rethrows it: an uncaught error is printed to the console whole. `app/error.tsx` shows a translated message and does not print the error.

## Languages

Ukrainian (`uk`, the default), Spanish (`es`), English (`en`) and Russian (`ru`), with next-intl and no locale in the URL. The locale comes from the `NEXT_LOCALE` cookie; anything else falls back to `uk`. The language toggle in the header writes the cookie and refreshes the page, and the server renders it again in the new language with `<html lang>` to match.

Messages live in `src/i18n/messages/<locale>.json`. Every visible string goes there, in all four files. `src/i18n/messages.test.ts` fails if a key is missing or empty in any of them, and the message types come from `uk.json`, so `t("a.missing.key")` does not compile. Spanish tax terms (Modelo 130, IVA, IRPF, TGSS, Renta, cuota, autónomo) stay as they are in every language.

## Themes

`light`, `dark`, `sepia` (warm, low glare), `contrast` (black and white with yellow accents) and `ocean` (dark blue). The theme toggle sets `data-theme` on `<html>` at once and writes the `theme` cookie. On the next request the server renders `data-theme` from that cookie, so the first paint already has the right colours and there is no flash.

The colours are CSS variables, one block per theme, in `src/app/globals.css`, with shadcn/ui's token names (`--background`, `--foreground`, `--primary`, ...). Tailwind v4 reads them through `@theme inline`, so `bg-background` or `text-muted-foreground` follow the theme. `dark:` utilities apply in `dark` and `ocean`.

`tests/theme-contrast.test.ts` reads that file and computes WCAG contrast for the text and surface pairs the components render: 4.5:1 (AA) in every theme, 7:1 (AAA) in `contrast`, and 3:1 for focus rings and input borders. It also fails if a component draws the ring translucent (`ring-ring/50`), which would show a weaker colour than the one tested. A first visit, with no `theme` cookie yet, gets `light`: the theme is rendered on the server from the cookie, so it cannot follow the system's dark setting before the user picks one (ADR-0017). To add a theme, add it to `src/shared/theme/themes.ts`, add its block to `globals.css` with hex colours, and add its name to the four message files.

## Data layer

- `src/data/client.ts`: `apiFetch<T>(path)` calls `<NEXT_PUBLIC_API_BASE_URL>/api/v1<path>` with the API key from `api-key-store.ts` in `X-Api-Key`, and locks the app on a 401 to that key (see "The API key"). A problem+json answer (RFC 9457) becomes an `ApiError` whose `failure` is `{ kind: "problem", problem }`; another error status is `{ kind: "http", status }`; an unreachable API is `{ kind: "network" }`.
- `src/data/query-provider.tsx`: the TanStack Query provider. Queries retry only an unreachable API or a 5xx; a 4xx, such as a 422 for a declared configuration gap, answers the same every time.
- **Query keys.** One file per API resource in `src/data/`, exporting a key factory and the query or mutation options together, with keys that start with the resource path: `["tax-years"]` (`tax-years.ts`), `["profiles"]` and `["profiles", id, "set-aside", asOf]` (`profiles.ts`), `["profiles", id, "transactions", year, quarter | "year"]` (`transactions.ts`); an import invalidates `["profiles", id, "transactions"]`. Features call those options from their `hooks/`; they never build keys or call `apiFetch` themselves.
- **Saves are mutations, estimates are queries.** Saving the profile is a mutation that invalidates `["profiles"]`, and with it every cached estimate, whose keys start there. The estimate of a stored profile is a query: it is computed from what the API holds, not from what the browser sends. The raw `POST /set-aside/estimate`, which takes the console's whole input file, has no caller in the web app.
- **Problem types.** `PROBLEM_TYPES` in `api-error.ts` names the API's problem `type` URIs: `invalid-input` (400, with `errors` keyed by the JSON path of each refused value, or `taxYear` or `asOf` for a query parameter; a profile names every refused value at once), `config-gap` (422, the configuration lacks or declares unpublished a value the calculation needs), `estimate-refused` (422, the engine cannot estimate the input), `statement-too-large` (413, a bank statement over 2 MB), `api-key-required` (401), `profile-not-found` (404), `profile-exists` (409, local mode keeps one profile), `installation-not-empty` (409, a restore into an installation that holds data, with `taxYear` and `entities` counts), `export-too-large` (413, an export file over 16 MB) and `export-media-type` (415). A refused statement's `errors` are keyed `file` or `line N`.
- **Money and dates.** Amounts arrive as strings with two decimals (SPEC-009) and are shown with `formatMoney(amount, locale)` from `shared/lib/format.ts`, which never turns them into a float. Dates arrive as ISO `yyyy-MM-dd` and are shown with `formatDate`.

### API types

`pnpm api:types` runs `openapi-typescript` on `../src/GestorIA.Api/openapi/v1.json` and writes `src/data/api-types.ts`. `dotnet build` writes that document from the API's code (`Microsoft.Extensions.ApiDescription.Server`, OpenAPI 3.1), and both files are committed, so the web job needs no .NET. CI fails if either is stale, and fails a pull request whose document breaks the one on `main` (oasdiff). After an API change: `dotnet build GestorIA.slnx`, then `pnpm api:types`, and commit both. Nothing in the web app invents an API type.

## Tests

- `shared/lib` formatting across the four locales, including negatives, zero, values beyond float precision and bad input.
- The theme and language toggles, the shell (navigation to every route, skip link, disclaimer in every language), and each feature's page through its `index.ts`.
- The root layout: `<html lang>` and `data-theme` come from the cookies, so the first paint is right (`tests/root-layout.test.tsx`).
- The virtualised list with ten thousand synthetic rows.
- The data client against stubbed `fetch` responses: JSON, 204, problem+json, a non-problem error and a network failure.
- Settings and the overview through their `index.ts`, with `fetch` stubbed by the API's own answers in `tests/fixtures/` (`tests/GestorIA.Api.Tests/WebFixtures.cs` fails when they drift from the API; rerun it with `GESTORIA_WRITE_WEB_FIXTURES=1` to rewrite them). Typing G12's figures saves exactly G12's stored profile, and a stored profile fills the form and is replaced on save; the API's refusals show next to their field; a new profile starts on the newest year without gaps. The overview asks for the stored profile's estimate at the right quarter, sends to settings without a profile, and shows a gap with a way back to settings. Nothing lands in storage, cookies, the address or the console.
- Transactions through its `index.ts`, with `fetch` stubbed by the API's answers for the synthetic 2025 statement: the year's list and Q1's asked with the right query, dates and amounts in four languages, money in and out filtered without a request, the import sent as the file with `text/csv` and followed by a fresh list, the API's refusals and the size limit shown, no profile sending to settings, ten thousand synthetic movements rendered as a window of rows and filtered, and nothing kept in storage, cookies, the address or the console.
- Backup and settings' data card through their `index.ts` (#74, #75): the download is the API's export, the same JSON document, under a name that carries only the date, labelled as personal financial data in every language, and never lands in storage, cookies, the address or the console; the restore shows what the file holds in every language before any request, sends the file's text byte for byte once confirmed, reads the profile again, and shows each refusal (installation not empty, the API's reasons, not JSON, not an export, a newer version, too large, database, network) with nothing sent or stored; the delete lists what goes, stays disabled until the word is typed, sends one `DELETE` and starts the profile form over, and an unreachable API says nothing was deleted. `tests/fixtures/g12-export.json` is the API's own export (`WebFixtures.cs`).
- Each test renders with a fresh QueryClient (`tests/render.tsx`), so no cached answer leaks from one test to the next.
- The auth feature through its `index.ts`: the unlock screen in every language, the right key (then sent on every request), a wrong key, an API that is not running and then is, a 401 later on, the lock button, and the key kept out of storage, cookies, the address, the console and the form's data.
- The structural checks in `tests/`.
