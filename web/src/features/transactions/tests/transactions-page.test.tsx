import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { MESSAGES, renderInApp } from "@tests/render";

import { TransactionsPage } from "@/features/transactions";
import { LOCALES } from "@/shared/constants/locales";

describe("TransactionsPage", () => {
  it.each(LOCALES)("says in %s what the page will show and that there is nothing to show yet", (locale) => {
    const messages = MESSAGES[locale];
    renderInApp(<TransactionsPage />, { locale });

    expect(screen.getByRole("heading", { level: 1, name: messages.Transactions.title })).toBeInTheDocument();
    for (const point of Object.values(messages.Transactions.points)) {
      expect(screen.getByText(point)).toBeInTheDocument();
    }
    expect(screen.getByRole("status")).toHaveTextContent(messages.EmptyState.status);
    expect(screen.getByText(messages.Transactions.pending)).toBeInTheDocument();
  });

  it("shows no amount, because no figure exists until the API does", () => {
    renderInApp(<TransactionsPage />, { locale: "en" });

    expect(document.body.textContent).not.toMatch(/€|\d[.,]\d{2}\b/);
  });
});
