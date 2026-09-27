import { screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { MESSAGES, renderInApp } from "@tests/render";

import { LOCALES } from "@/shared/constants/locales";
import { AppShell } from "@/shared/shell/app-shell";

vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh: vi.fn() }), usePathname: () => "/payments" }));

describe("AppShell", () => {
  it("links to every feature route and marks the current one", () => {
    renderInApp(<AppShell>content</AppShell>, { locale: "en" });

    const nav = screen.getByRole("navigation", { name: "Main navigation" });
    const links = within(nav)
      .getAllByRole("link")
      .map((link) => [link.textContent, link.getAttribute("href")]);
    expect(links).toEqual([
      ["Overview", "/"],
      ["Payments", "/payments"],
      ["Transactions", "/transactions"],
      ["Periods", "/periods"],
      ["Settings", "/settings"],
      ["Backup", "/backup"],
      ["Access", "/auth"],
    ]);
    expect(within(nav).getByRole("link", { name: "Payments" })).toHaveAttribute("aria-current", "page");
  });

  it("starts with a skip link to the main content", () => {
    renderInApp(<AppShell>content</AppShell>, { locale: "en" });

    const skip = screen.getAllByRole("link")[0];
    expect(skip).toHaveTextContent("Skip to main content");
    expect(skip).toHaveAttribute("href", "#main");
    expect(screen.getByRole("main")).toHaveAttribute("id", "main");
  });

  it("puts the theme and language toggles in the header", () => {
    renderInApp(<AppShell>content</AppShell>, { locale: "en" });

    const header = screen.getByRole("banner");
    expect(within(header).getByRole("combobox", { name: "Theme" })).toBeInTheDocument();
    expect(within(header).getByRole("combobox", { name: "Language" })).toBeInTheDocument();
  });

  it.each(LOCALES)("shows the not-tax-advice disclaimer in %s", (locale) => {
    renderInApp(<AppShell>content</AppShell>, { locale });

    expect(screen.getByRole("note")).toHaveTextContent(MESSAGES[locale].Shell.disclaimer);
  });
});
