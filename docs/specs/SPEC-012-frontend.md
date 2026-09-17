# SPEC-012 — Web Application (React + TypeScript)

**Status:** Draft · **Phase:** 5 · **ADR:** 0007 · **Location:** `web/` (created in Phase 5)

## 1. Screens
1. **Onboarding wizard** — year, region, personal/family data (31-Dec snapshot), employment/autónomo (036 data), housing. Saves `TaxpayerProfile`.
2. **Documents** — drag-and-drop upload, per-file status (queued / extracting / needs review / confirmed), duplicate detection (SHA-256).
3. **Review queue** — one item at a time: extracted fields with confidence colouring and the source page crop (bbox), inline edit, confirm/reject; unclear transactions with the direct question and "upload invoice / mark personal / it's a transfer" actions.
4. **Ledger** — tables per type (nóminas, invoices issued/received, transactions) with links to documents; filters by quarter.
5. **Quarter** — 130 and 303 results with line numbers, trace accordion, due dates, "set aside" estimate.
6. **Annual** — result banner (a ingresar / a devolver), step-by-step trace, individual vs conjunta comparison, credits panel (Applied / Possible — with next action), warnings, casilla sheet with copy buttons and CSV/PDF export.
7. **Calendar** — upcoming deadlines (from API), ICS export.
8. **Settings** — language, data export/delete, LLM fallback toggle (default off) with a clear privacy note.

## 2. Technical
Vite + React 19 + TS strict; TanStack Query; generated client from OpenAPI (`openapi-typescript`); routing (TanStack Router or React Router); forms with react-hook-form + zod (schemas mirror API validation); i18n (i18next, ES/EN/RU); charts minimal (effective vs marginal rate); no client-side tax math — the SPA displays engine output only.

## 3. Non-functional
Lighthouse ≥ 90; keyboard-navigable review queue; amounts formatted per locale but stored as strings; never log document contents in the browser console.

## 4. Acceptance
Two external users complete onboarding → upload → review → annual result without help; task time and confusion points recorded in `reports/reviews/`.
