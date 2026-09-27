import { describe, expect, it } from "vitest";

import { resolveLocale } from "@/shared/constants/locales";

describe("resolveLocale", () => {
  it("uses Ukrainian when there is no cookie", () => {
    expect(resolveLocale(undefined)).toBe("uk");
  });

  it.each(["uk", "es", "en", "ru"] as const)("uses the %s cookie", (locale) => {
    expect(resolveLocale(locale)).toBe(locale);
  });

  it.each(["de", "EN", "en-US", "", "uk;"])("falls back to Ukrainian for the unsupported cookie %j", (value) => {
    expect(resolveLocale(value)).toBe("uk");
  });
});
