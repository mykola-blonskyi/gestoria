import { describe, expect, it } from "vitest";

import { resolveTheme } from "@/shared/theme/themes";

describe("resolveTheme", () => {
  it("uses light when there is no cookie", () => {
    expect(resolveTheme(undefined)).toBe("light");
  });

  it.each(["light", "dark", "sepia", "contrast", "ocean"] as const)("uses the %s cookie", (theme) => {
    expect(resolveTheme(theme)).toBe(theme);
  });

  it.each(["solarized", "Dark", "", "dark "])("falls back to light for the unknown cookie %j", (value) => {
    expect(resolveTheme(value)).toBe("light");
  });
});
