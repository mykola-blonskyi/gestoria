import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { MESSAGES, renderInApp } from "@tests/render";

import { SettingsPage } from "@/features/settings";
import { LOCALES } from "@/shared/constants/locales";

vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

describe("SettingsPage", () => {
  it.each(LOCALES)("says in %s what the page will show and that there is nothing to show yet", (locale) => {
    const messages = MESSAGES[locale];
    renderInApp(<SettingsPage />, { locale });

    expect(screen.getByRole("heading", { level: 1, name: messages.Settings.title })).toBeInTheDocument();
    for (const point of Object.values(messages.Settings.points)) {
      expect(screen.getByText(point)).toBeInTheDocument();
    }
    expect(screen.getByRole("status")).toHaveTextContent(messages.EmptyState.status);
    expect(screen.getByText(messages.Settings.pending)).toBeInTheDocument();
  });

  it("shows no amount, because no figure exists until the API does", () => {
    renderInApp(<SettingsPage />, { locale: "en" });

    expect(document.body.textContent).not.toMatch(/€|\d[.,]\d{2}\b/);
  });

  it("lets the user change the theme and the language here as well as in the header", () => {
    renderInApp(<SettingsPage />, { locale: "en" });

    expect(screen.getByRole("combobox", { name: "Theme" })).toHaveValue("light");
    expect(screen.getByRole("combobox", { name: "Language" })).toHaveValue("en");
  });
});
