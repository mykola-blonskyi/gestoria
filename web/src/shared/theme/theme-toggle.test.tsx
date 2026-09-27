import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";

import { renderInApp } from "@tests/render";

import { ThemeToggle } from "@/shared/theme/theme-toggle";

afterEach(() => {
  delete document.documentElement.dataset.theme;
  document.cookie = "theme=; max-age=0; path=/";
});

describe("ThemeToggle", () => {
  it("offers the five themes by their translated names", () => {
    renderInApp(<ThemeToggle />, { locale: "uk" });

    const select = screen.getByRole("combobox", { name: "Тема" });
    expect([...select.querySelectorAll("option")].map((option) => option.textContent)).toEqual([
      "Світла",
      "Темна",
      "Сепія",
      "Висока контрастність",
      "Океан",
    ]);
    expect(select).toHaveValue("light");
  });

  it("applies the chosen theme to the page at once and remembers it in a cookie", async () => {
    renderInApp(<ThemeToggle />, { locale: "en", theme: "light" });

    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Theme" }), "sepia");

    expect(document.documentElement.dataset.theme).toBe("sepia");
    expect(document.cookie).toContain("theme=sepia");
    expect(screen.getByRole("combobox", { name: "Theme" })).toHaveValue("sepia");
  });

  it("is a native select reachable with the Tab key", async () => {
    renderInApp(<ThemeToggle />, { locale: "en" });

    await userEvent.tab();

    expect(screen.getByRole("combobox", { name: "Theme" })).toHaveFocus();
  });
});
