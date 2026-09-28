import { describe, expect, it } from "vitest";

import type { components } from "@/data/api-types";
import { TRACE_SECTIONS, type TraceSection } from "@/shared/ui/trace-view";

type ApiTraceSection = components["schemas"]["TraceSection"];

// Compile-time proof, not a runtime one: TypesMatch is `true` only when the two unions have exactly the same members in
// each direction, so this only type-checks (and so only survives `pnpm typecheck`, part of the same gate as every other
// test here) when TraceSection and the API's generated TraceSection list the same sections. A section added to or removed
// from GestorIA.Engine's TraceSection enum, once `pnpm api:types` regenerates api-types.ts, fails here rather than
// silently drifting from the shared Trace component's local literal tuple.
type TypesMatch<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;
const sectionsMatchTheApi: TypesMatch<TraceSection, ApiTraceSection> = true;
void sectionsMatchTheApi;

describe("TraceSection", () => {
  it("is kept in trace-view.tsx as the shared component's source of truth", () => {
    expect(TRACE_SECTIONS.length).toBeGreaterThan(0);
    expect(new Set(TRACE_SECTIONS).size).toBe(TRACE_SECTIONS.length);
  });
});
