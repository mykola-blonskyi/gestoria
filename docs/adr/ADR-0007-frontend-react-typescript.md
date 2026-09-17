# ADR-0007: React + TypeScript SPA (not Blazor)

**Status:** Accepted · **Date:** 2026-09-17

## Context
Frontend choices: Blazor (stay all-C#), React/Vue/Svelte with TypeScript (author's primary language).

## Decision
React 19 + TypeScript + Vite, API client generated from OpenAPI, state via TanStack Query, UI kit kept minimal (headless components + CSS modules or Tailwind). Lives in `web/` (created in Phase 5).

## Alternatives
- Blazor WASM/Server: maximises C# practice but adds a second unfamiliar UI paradigm on top of two new languages.
- Vue/Svelte: fine, but React is the author's strongest ecosystem.

## Trade-offs
The SPA is where the author is fastest, which frees learning budget for the engine and the OCR service. The API contract stays language-neutral, so Blazor can be added later for a gestor back-office if desired.

## Consequences
- OpenAPI is a first-class artifact; breaking API changes fail the SPA type-check in CI.
- i18n from day one (ES/EN/RU).
- No tax math in the client; all numbers come from the engine.
