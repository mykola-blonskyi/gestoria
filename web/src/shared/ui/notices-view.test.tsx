import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { renderInApp } from "@tests/render";

import { Notices } from "@/shared/ui/notices-view";

describe("Notices", () => {
  it("renders nothing for an empty list, instead of a heading over nothing", () => {
    const { container } = renderInApp(<Notices notices={[]} />, { locale: "en" });

    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
  });

  it("renders the heading and every notice when there is at least one", () => {
    renderInApp(<Notices notices={[{ code: "SET_ASIDE_ESTIMATE", severity: "Info", text: "A note." }]} />, { locale: "en" });

    expect(screen.getByRole("heading")).toBeInTheDocument();
    expect(screen.getByText("A note.")).toBeInTheDocument();
  });
});
