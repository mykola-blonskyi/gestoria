# ADR-0017: The web app is Next.js (App Router) with next-intl, in `web/`

**Status:** Accepted · **Date:** 2026-09-27 · **Supersedes:** the framework, routing and i18n parts of [ADR-0007](ADR-0007-frontend-react-typescript.md) (its OpenAPI-as-artifact, TanStack Query and no-tax-math-in-the-client decisions stand)

## Context
ADR-0007 chose a React 19 + TypeScript SPA built with Vite, with i18n in ES/EN/RU. SPEC-012 §2 added TanStack Router or React Router and i18next. `docs/decisions.md` deferred "CLI or SPA" to M3 (March 2027).

On 2026-09-27 the author decided the frontend now (#65, spec #67): a Next.js App Router application in `web/`, with next-intl, four languages and Ukrainian by default, five themes, and a layered structure whose rules are enforced by lint. ADR-0007 is Accepted and is not edited, so this record replaces its framework, routing and i18n clauses and keeps the rest.

This ADR is Accepted because the decision has been exercised: #65 builds the scaffold against it, and lint, type-check, tests and `next build` run in CI.

## Premises

| Premise | Stated or concluded | Source |
|---|---|---|
| There is a web app, built now with the Next.js App Router. The CLI stays | Stated | Author, 2026-09-27 (#65) |
| Languages are uk, es, en and ru; Ukrainian is the default; the locale comes from a cookie | Stated | Author, 2026-09-27 (#65) |
| The allowed libraries are shadcn/ui, Tailwind CSS v4, TanStack Query, TanStack Virtual and zustand | Stated | Author, 2026-09-27 (#65) |
| Five themes: light, dark and three more. The three are sepia, contrast and ocean | Stated (the count, #65); the three names come from the spec | #65, #67 |
| The layers `app`, `data`, `features`, `shared`, `i18n` and their import rules | Stated | Author, 2026-09-27 (#65) |
| The app runs on the author's machine next to the API, for one user (ADR-0010), so locale-prefixed URLs buy nothing | Concluded | This ADR |
| The theme is better stored in a cookie and rendered by the server than set by an inline script | Concluded | This ADR, see Alternatives |
| `import/no-restricted-paths` expresses every layer rule | Concluded, and checked: `web/tests/layers.test.ts` sees each rule reject a violation | #65 |

## Decision
- **Framework.** Next.js 16 App Router, React 19, TypeScript strict, pnpm, in `web/` (the root `src/` holds .NET). The routes are the App Router's; there is no separate router library.
- **Layers** (`web/src`). `app` holds routes and layouts and nothing imports it. `data` is the data access layer and imports only `shared`. `features/<name>` has `components/`, `hooks/`, `tests/` and an `index.ts`, and features do not import each other. `shared` is the bottom layer. `i18n` imports only `shared`. ESLint's `import/no-restricted-paths` enforces each rule; `web/tests/layers.test.ts` proves each one rejects a violation.
- **Languages.** next-intl without i18n routing. The locale comes from the `NEXT_LOCALE` cookie and falls back to `uk`; `<html lang>` follows it. Messages live in `web/src/i18n/messages/<locale>.json`, and a test keeps their keys identical. Spanish tax terms are not translated.
- **Themes.** light, dark, sepia, contrast and ocean. The `theme` cookie is read on the server and rendered as `data-theme` on `<html>`, so the first paint has the right colours without a script. Tokens are CSS variables per theme in `web/src/app/globals.css`, named as shadcn/ui names them and exposed through Tailwind v4's `@theme inline`. A test computes WCAG contrast for every text/surface pair: AA (4.5:1) in every theme, AAA (7:1) in contrast, 3:1 for focus rings and input borders.
- **Data.** A fetch client for `/api/v1` on `NEXT_PUBLIC_API_BASE_URL` (default `http://localhost:5080`) turns problem+json (RFC 9457) into one typed `ApiError`. TanStack Query holds server state and does not retry a 4xx. `pnpm api:types` generates types with `openapi-typescript` from `src/GestorIA.Api/openapi/v1.json`, the document the API will emit at build time (#66), and fails with a clear message while it is absent.
- **Kept from ADR-0007.** OpenAPI is a first-class artifact, TanStack Query holds server state, and the browser does no tax math: every figure comes from the engine through the API.

## Alternatives
- **Vite SPA with a router library (ADR-0007, SPEC-012 §2).** Rejected by the author.
- **next-intl with locale-prefixed routes (`/uk/...`).** Useful for public, indexed, shareable pages. This is a local single-user app, and the author asked for a cookie.
- **i18next (SPEC-012 §2).** next-intl is built for the App Router and server components, and its message types catch a missing key at compile time.
- **next-themes.** It keeps the theme in `localStorage` and sets the attribute with an inline script that runs before hydration. Every page here already renders on the server per request, because the locale comes from a cookie, so the server can write `data-theme` itself: no script, no flash by construction, and one fewer dependency.
- **eslint-plugin-boundaries.** More expressive, but a new dependency whose rule API was renamed in its last two majors. `import/no-restricted-paths` ships with `eslint-config-next` and maps one rule to one zone.
- **zustand now.** Nothing in the scaffold is client state shared across components: server state is TanStack Query's, and the theme and language are cookies. zustand stays allowed and is added with the first store that needs it.

## Consequences
- `web/` is a second toolchain: Node 24 and pnpm. CI gains a `web` job (install from the lockfile, lint, type-check, test, build) next to the .NET job.
- Every page renders on demand, because the locale and the theme come from cookies. There is no static export. For a local app this costs nothing measurable.
- With no cookie the theme is light. A first visit does not follow the system's dark preference, because the server cannot see it without client hints.
- The browser calls the API origin directly, so #66 must allow the web origin in CORS, or the web app must proxy `/api/v1`.
- `pnpm api:types` fails until #66 emits `src/GestorIA.Api/openapi/v1.json` (for example with `OpenApiDocumentsDirectory` and `OpenApiGenerateDocumentsOptions="--file-name v1"` on `Microsoft.Extensions.ApiDescription.Server`).
- SPEC-012 §1's screen list is replaced by the feature list (dashboard, payments, transactions, periods, settings, auth, backup); SPEC-012 maps one onto the other.
- SPEC-010 still says explanations are ES/EN/RU. The engine's messages need Ukrainian too once they reach the web app.
