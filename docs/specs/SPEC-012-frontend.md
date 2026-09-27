# SPEC-012 — Web Application (Next.js + TypeScript)

**Status:** Draft · **Phase:** 5 · **ADRs:** 0007, 0017 · **Location:** `web/` (scaffold in #65)

## 1. Screens

> **Replaced by the feature list (2026-09-27, #65, #67).** The author's features are the web app's screens: dashboard, payments, transactions, periods, settings, auth and backup. The list below keeps its content, mapped onto them:
>
> | Screen below | Feature |
> |---|---|
> | 1 Onboarding wizard | settings (the taxpayer profile) |
> | 2 Documents | none: document upload stays out with OCR (SPEC-005, v1.x) |
> | 3 Review queue | transactions (movements that need a decision) |
> | 4 Ledger | transactions |
> | 5 Quarter | periods (results and trace); payments (due dates and amounts) |
> | 6 Annual | periods |
> | 7 Calendar | payments |
> | 8 Settings | settings; data export and restore in backup |
> | (new) | dashboard: the set-aside estimate, what to hold back from each payment and what falls due next |
> | (new) | auth: the single-user local API key (SPEC-009 §3) |
>
> "Payments" means what the taxpayer pays (Modelo 130, 303 and 349, TGSS cuotas, the Renta true-up). Incoming money is in transactions.

1. **Onboarding wizard** — year, region, personal/family data (31-Dec snapshot), employment/autónomo (036 data), housing. Saves `TaxpayerProfile`.
2. **Documents** — drag-and-drop upload, per-file status (queued / extracting / needs review / confirmed), duplicate detection (SHA-256).
3. **Review queue** — one item at a time: extracted fields with confidence colouring and the source page crop (bbox), inline edit, confirm/reject; unclear transactions with the direct question and "upload invoice / mark personal / it's a transfer" actions.
4. **Ledger** — tables per type (nóminas, invoices issued/received, transactions) with links to documents; filters by quarter.
5. **Quarter** — 130 and 303 results with line numbers, trace accordion, due dates, "set aside" estimate.
6. **Annual** — result banner (a ingresar / a devolver), step-by-step trace, individual vs conjunta comparison, credits panel (Applied / Possible — with next action), warnings, casilla sheet with copy buttons and CSV/PDF export.
7. **Calendar** — upcoming deadlines (from API), ICS export.
8. **Settings** — language, data export/delete, LLM fallback toggle (default off) with a clear privacy note.

## 2. Technical
Decided in ADR-0017, which supersedes ADR-0007's framework, routing and i18n. `web/README.md` is the working guide.

- **Framework.** Next.js 16 App Router, React 19, TypeScript strict, pnpm. Routing is the App Router's.
- **Layers** in `web/src`, enforced by ESLint (`import/no-restricted-paths`) and proven by `web/tests/layers.test.ts`:
  - `app`: routes and layouts only. Nothing imports it.
  - `data`: the fetch client for `/api/v1`, types generated from OpenAPI, TanStack Query options and mutations per resource. Imports only `shared`.
  - `features/<name>`: `components/`, `hooks/` (data queries assembled for the feature), `tests/`, and an `index.ts` that is the only way in. Features do not import each other.
  - `shared`: `lib` (`cn`, money and date formatting), `ui` (shadcn/ui, alias `@/shared/ui`), `types`, `constants`, `shell` (navigation, header, disclaimer, language toggle), `theme` (provider and toggle). Imports nothing above it.
  - `i18n`: next-intl configuration. Imports only `shared`.
- **Languages.** next-intl without locale routes: uk (default), es, en, ru, chosen by the `NEXT_LOCALE` cookie; `<html lang>` follows it. Tax terms stay in Spanish. A test keeps the four message files' keys identical.
- **Themes.** light, dark, sepia, contrast, ocean. The `theme` cookie is rendered by the server as `data-theme` on `<html>` (no flash). Tokens are CSS variables in `app/globals.css` with shadcn/ui names, exposed through Tailwind v4 `@theme inline`. A test holds text to WCAG AA in every theme and AAA in contrast.
- **Server state** is TanStack Query's. zustand only for shared client state that is neither server state nor a cookie preference; none exists yet. TanStack Virtual renders long lists (transactions, traces).
- **API contract** (SPEC-009). Money arrives as strings with two decimals and is formatted from the string, never through a float. Dates are ISO-8601. problem+json errors become one typed `ApiError`; a 4xx is not retried. `pnpm api:types` generates the types with `openapi-typescript` from `src/GestorIA.Api/openapi/v1.json`, which `dotnet build` writes (#66); both are committed and CI fails when either is stale.
- **Forms.** Plain React state, no forms library (#66). react-hook-form and zod are not on the author's list of allowed libraries (#65), and the first form, the overview's set-aside input, is one screen whose values mirror the console's input file. Each field is keyed by the JSON path the API reports errors at, so a client-side check and an API refusal land on the same field. Revisit when a form needs field arrays, async validation or schema sharing that plain state makes painful.
- **No client-side tax math.** The web app displays engine output only.

## 3. Non-functional
Lighthouse ≥ 90; keyboard-navigable review queue; amounts formatted per locale but stored as strings; never log document contents in the browser console.

## 4. Acceptance
Two external users complete onboarding → upload → review → annual result without help; task time and confusion points recorded in `reports/reviews/`.
