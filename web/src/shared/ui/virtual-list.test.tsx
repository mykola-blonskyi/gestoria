import { act, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { VirtualList } from "@/shared/ui/virtual-list";

const VIEWPORT_HEIGHT = 400;
const ROW_HEIGHT = 40;

type Row = { id: string; label: string };
const rows: Row[] = Array.from({ length: 10_000 }, (_, index) => ({ id: `row-${index}`, label: `Row ${index}` }));

// jsdom does no layout: give the scroll container the viewport's size, and every row its estimated height (uniform here,
// so this measures the same as the old fixed-height row and every existing assertion below still holds), so the
// virtualizer can measure both. TanStack Virtual's default measureElement reads offsetHeight, not getBoundingClientRect,
// so both must agree per-element or every row measures as the container's height instead of its own.
// trace-view.test.tsx varies a row's height instead, to prove real measurement is used.
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
    const height = this.hasAttribute("data-index") ? ROW_HEIGHT : VIEWPORT_HEIGHT;
    return DOMRect.fromRect({ width: 600, height });
  });
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockImplementation(function (this: HTMLElement) {
    return this.hasAttribute("data-index") ? ROW_HEIGHT : VIEWPORT_HEIGHT;
  });
  vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockReturnValue(600);
});

afterEach(() => {
  vi.restoreAllMocks();
});

function renderRows() {
  return render(
    <VirtualList
      items={rows}
      label="Synthetic rows"
      rowHeight={ROW_HEIGHT}
      getKey={(row) => row.id}
      renderRow={(row) => row.label}
    />,
  );
}

describe("VirtualList", () => {
  it("renders only the rows in view out of ten thousand", () => {
    renderRows();

    const items = within(screen.getByRole("list")).getAllByRole("listitem");
    expect(items.length).toBeGreaterThan(0);
    expect(items.length).toBeLessThan(30);
    expect(screen.getByText("Row 0")).toBeInTheDocument();
    expect(screen.queryByText("Row 9999")).not.toBeInTheDocument();
  });

  it("tells assistive technology the full size and each row's position", () => {
    renderRows();

    const first = within(screen.getByRole("list")).getAllByRole("listitem")[0];
    expect(first).toHaveAttribute("aria-setsize", "10000");
    expect(first).toHaveAttribute("aria-posinset", "1");
  });

  it("renders the rows at the end after scrolling there", () => {
    renderRows();
    const viewport = screen.getByRole("region", { name: "Synthetic rows" });

    act(() => {
      viewport.scrollTop = rows.length * ROW_HEIGHT - VIEWPORT_HEIGHT;
      viewport.dispatchEvent(new Event("scroll"));
    });

    expect(screen.getByText("Row 9999")).toBeInTheDocument();
    expect(screen.queryByText("Row 0")).not.toBeInTheDocument();
  });

  it("is a focusable, named region so the keyboard can scroll it", () => {
    renderRows();

    expect(screen.getByRole("region", { name: "Synthetic rows" })).toHaveAttribute("tabindex", "0");
  });
});
