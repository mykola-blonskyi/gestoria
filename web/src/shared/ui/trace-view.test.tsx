import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderInApp } from "@tests/render";

import { Trace, type TraceStep } from "@/shared/ui/trace-view";

const VIEWPORT_HEIGHT = 400;

// jsdom does no layout: give every element the viewport's size so the virtualizer can measure (mirrors virtual-list.test.tsx).
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(DOMRect.fromRect({ width: 600, height: VIEWPORT_HEIGHT }));
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockReturnValue(VIEWPORT_HEIGHT);
  vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockReturnValue(600);
});

afterEach(() => {
  vi.restoreAllMocks();
});

function step(id: string, section: TraceStep["section"] = "Modelo130"): TraceStep {
  return {
    id,
    section,
    title: `Step ${id}`,
    inputs: [],
    formula: "1 = 1",
    output: { kind: "count", value: "1" },
    reference: "fixture",
  };
}

function renderTrace(steps: TraceStep[]) {
  return renderInApp(<Trace steps={steps} locale="en" />, { locale: "en" });
}

describe("Trace virtualization", () => {
  it("renders a short section as a plain, fully-present list", () => {
    const steps = Array.from({ length: 5 }, (_, index) => step(`short-${index}`));
    renderTrace(steps);

    expect(screen.queryByRole("region")).not.toBeInTheDocument();
    for (const s of steps) expect(screen.getByText(s.id)).toBeInTheDocument();
  });

  it("switches a long section (more than 12 steps) to a virtualized region", () => {
    const steps = Array.from({ length: 50 }, (_, index) => step(`long-${index}`));
    renderTrace(steps);

    expect(screen.getByRole("region")).toBeInTheDocument();
    // The virtualizer renders only what fits the viewport: the first steps are present, the far tail is not.
    expect(screen.getByText("long-0")).toBeInTheDocument();
    expect(screen.queryByText("long-49")).not.toBeInTheDocument();
  });
});
