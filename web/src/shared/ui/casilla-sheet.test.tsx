import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import negativeQuarter from "@tests/fixtures/g12-quarter-negative.json";
import taxYears from "@tests/fixtures/tax-years.json";
import { renderInApp } from "@tests/render";

import { CasillaSheet } from "@/shared/ui/casilla-sheet";

const MODELO130_LINES = taxYears.find((year) => year.taxYear === 2025)!.modelo130Lines;

describe("CasillaSheet", () => {
  // #71 fix-forward: casilla 19 is the self-assessment's result, not always a payment. A negative one (g12-quarter-negative
  // .json, casilla 19 = -100.00) is never due; it carries to a later quarter instead (Modelo130Carry.NegativosPendientes).
  it("never calls a negative casilla 19 a payment or something due", () => {
    renderInApp(<CasillaSheet casillas={negativeQuarter.casillas} modelo130Lines={MODELO130_LINES} />, { locale: "en" });

    const dt = screen.getByText(/Casilla 19/);
    expect(dt.textContent ?? "").not.toMatch(/\bpay\b/i);
    expect(dt.textContent ?? "").not.toMatch(/\bdue\b/i);
    expect(dt.nextElementSibling).toHaveTextContent("-€100.00");
  });
});
