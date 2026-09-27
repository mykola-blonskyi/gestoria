# GestorIA web app

The browser side of GestorIA: a Next.js App Router application that shows the engine's answers in Ukrainian, Spanish, English or Russian. It runs on your machine next to the API (ADR-0010). It never computes tax; every figure comes from the engine through the API (ADR-0017, SPEC-012).

The overview shows the set-aside estimate from the API (#66). Every other page is an honest empty state that says what it will show.

## Run it

You need Node 24 and pnpm (the version is pinned in `package.json`, so `corepack enable` is enough).

```bash
cd web
pnpm install
pnpm dev          # http://localhost:3000
```

The overview needs the API: `dotnet run --project src/GestorIA.Api` from the repository root. It listens on `http://localhost:5080` and allows calls from `http://localhost:3000` (`Cors:Origins` in its `appsettings.json`; set `Cors__Origins__0` when `pnpm dev` takes another port).

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

- `src/data/client.ts`: `apiFetch<T>(path)` calls `<NEXT_PUBLIC_API_BASE_URL>/api/v1<path>`. A problem+json answer (RFC 9457) becomes an `ApiError` whose `failure` is `{ kind: "problem", problem }`; another error status is `{ kind: "http", status }`; an unreachable API is `{ kind: "network" }`.
- `src/data/query-provider.tsx`: the TanStack Query provider. Queries retry only an unreachable API or a 5xx; a 4xx, such as a 422 for a declared configuration gap, answers the same every time.
- **Query keys.** One file per API resource in `src/data/`, exporting a key factory and the query or mutation options together, with keys that start with the resource path: `["tax-years"]` (`tax-years.ts`), `["set-aside", "estimate"]` (`set-aside.ts`). Features call those options from their `hooks/`; they never build keys or call `apiFetch` themselves.
- **Calculations are mutations.** `POST /set-aside/estimate` is a TanStack Query mutation, not a query: its input is what the user typed, personal financial data, and it stays in the tab's memory, never under a cache key or in browser storage (SPEC-013).
- **Problem types.** `PROBLEM_TYPES` in `api-error.ts` names the API's problem `type` URIs: `invalid-input` (400, with `errors` keyed by the JSON path of each refused value, or `taxYear` for the query parameter), `config-gap` (422, the configuration lacks or declares unpublished a value the calculation needs) and `estimate-refused` (422, the engine cannot estimate the input).
- **Money and dates.** Amounts arrive as strings with two decimals (SPEC-009) and are shown with `formatMoney(amount, locale)` from `shared/lib/format.ts`, which never turns them into a float. Dates arrive as ISO `yyyy-MM-dd` and are shown with `formatDate`.

### API types

`pnpm api:types` runs `openapi-typescript` on `../src/GestorIA.Api/openapi/v1.json` and writes `src/data/api-types.ts`. `dotnet build` writes that document from the API's code (`Microsoft.Extensions.ApiDescription.Server`, OpenAPI 3.1), and both files are committed, so the web job needs no .NET. CI fails if either is stale, and fails a pull request whose document breaks the one on `main` (oasdiff). After an API change: `dotnet build GestorIA.slnx`, then `pnpm api:types`, and commit both. Nothing in the web app invents an API type.

## Tests

- `shared/lib` formatting across the four locales, including negatives, zero, values beyond float precision and bad input.
- The theme and language toggles, the shell (navigation to every route, skip link, disclaimer in every language), and each feature's page through its `index.ts`.
- The root layout: `<html lang>` and `data-theme` come from the cookies, so the first paint is right (`tests/root-layout.test.tsx`).
- The virtualised list with ten thousand synthetic rows.
- The data client against stubbed `fetch` responses: JSON, 204, problem+json, a non-problem error and a network failure.
- The overview through its `index.ts`, with `fetch` stubbed by the API's own answers in `features/dashboard/tests/fixtures/` (`tests/GestorIA.Api.Tests/WebFixtures.cs` fails when they drift from the API; rerun it with `GESTORIA_WRITE_WEB_FIXTURES=1` to rewrite them). Typing G14's figures and loading G14's input file both post exactly G14's input file; nothing lands in storage, cookies or the address; errors show next to their field.
- Each test renders with a fresh QueryClient (`tests/render.tsx`), so no cached answer leaks from one test to the next.
- The structural checks in `tests/`.
