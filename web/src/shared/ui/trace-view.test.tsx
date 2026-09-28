import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderInApp } from "@tests/render";

import { Trace, type TraceStep } from "@/shared/ui/trace-view";

const VIEWPORT_HEIGHT = 400;
const SHORT_ROW = 60;
const TALL_ROW = 800;

// jsdom does no layout: give the scroll container the viewport's size and every row a fixed estimate, so the virtualizer
// can measure both (mirrors virtual-list.test.tsx). TanStack Virtual's default measureElement reads offsetHeight, not
// getBoundingClientRect, so both must agree per-element. The last test below overrides a single row's measured height to
// a multi-line step's real size, proving the virtualizer uses that measurement rather than the 180px estimate.
function mockRowHeights(heightOf: (index: string | null) => number) {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
    const height = this.hasAttribute("data-index") ? heightOf(this.getAttribute("data-index")) : VIEWPORT_HEIGHT;
    return DOMRect.fromRect({ width: 600, height });
  });
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockImplementation(function (this: HTMLElement) {
    return this.hasAttribute("data-index") ? heightOf(this.getAttribute("data-index")) : VIEWPORT_HEIGHT;
  });
  vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockReturnValue(600);
}

beforeEach(() => {
  mockRowHeights(() => SHORT_ROW);
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

  // The bug this guards against: a fixed 180px row-height estimate with no real measurement made a tall, multi-line step
  // overlap the row below it. Row 0 here measures 800px, well past the 180px estimate; row 1 must start no earlier than
  // that, proving the virtualizer used row 0's actual rendered height rather than the estimate.
  it("positions the next row below a tall step's real measured height, not the fixed estimate", () => {
    mockRowHeights((index) => (index === "0" ? TALL_ROW : SHORT_ROW));
    const steps = Array.from({ length: 50 }, (_, index) => step(`row-${index}`));
    const { container } = renderTrace(steps);

    const startOf = (index: number) => {
      const row = container.querySelector(`[data-index="${index}"]`);
      const match = /translateY\((\d+(?:\.\d+)?)px\)/.exec((row as HTMLElement).style.transform);
      return match ? Number(match[1]) : NaN;
    };

    expect(startOf(1)).toBeGreaterThanOrEqual(TALL_ROW);
  });
});
