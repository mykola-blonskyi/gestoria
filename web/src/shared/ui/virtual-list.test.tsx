import { act, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { VirtualList } from "@/shared/ui/virtual-list";

const VIEWPORT_HEIGHT = 400;
const ROW_HEIGHT = 40;

type Row = { id: string; label: string };
const rows: Row[] = Array.from({ length: 10_000 }, (_, index) => ({ id: `row-${index}`, label: `Row ${index}` }));

// jsdom does no layout: give every element the viewport's size so the virtualizer can measure.
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(
    DOMRect.fromRect({ width: 600, height: VIEWPORT_HEIGHT }),
  );
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockReturnValue(VIEWPORT_HEIGHT);
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
