import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderInApp } from "@tests/render";

import { LanguageToggle } from "@/shared/shell/language-toggle";

// The App Router is not mounted in a unit test; refresh() is where Next re-renders
// the page on the server with the new cookie.
const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

afterEach(() => {
  refresh.mockClear();
  document.cookie = "NEXT_LOCALE=; max-age=0; path=/";
});

describe("LanguageToggle", () => {
  it("lists the four languages by their own names, with the current one selected", () => {
    renderInApp(<LanguageToggle />, { locale: "uk" });

    const select = screen.getByRole("combobox", { name: "Мова" });
    expect([...select.querySelectorAll("option")].map((option) => [option.value, option.textContent])).toEqual([
      ["uk", "Українська"],
      ["es", "Español"],
      ["en", "English"],
      ["ru", "Русский"],
    ]);
    expect(select).toHaveValue("uk");
  });

  it("stores the chosen language in the NEXT_LOCALE cookie and refreshes the page", async () => {
    renderInApp(<LanguageToggle />, { locale: "uk" });

    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Мова" }), "es");

    expect(document.cookie).toContain("NEXT_LOCALE=es");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("labels the control in the current language", () => {
    renderInApp(<LanguageToggle />, { locale: "es" });

    expect(screen.getByRole("combobox", { name: "Idioma" })).toHaveValue("es");
  });
});
