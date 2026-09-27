import { existsSync, readdirSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

import { NAV_ITEMS } from "@/shared/constants/routes";

const src = path.resolve(import.meta.dirname, "../src");
const directories = (dir: string) =>
  readdirSync(path.join(src, dir), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();

const FEATURES = ["auth", "backup", "dashboard", "payments", "periods", "settings", "transactions"];

describe("web/src structure (#65)", () => {
  it("has exactly the five layers", () => {
    expect(directories(".")).toEqual(["app", "data", "features", "i18n", "shared"]);
  });

  it("has exactly the seven features", () => {
    expect(directories("features")).toEqual(FEATURES);
  });

  it.each(FEATURES)("%s has components, hooks, tests and an index.ts public entry", (feature) => {
    expect(directories(`features/${feature}`)).toEqual(["components", "hooks", "tests"]);
    expect(existsSync(path.join(src, "features", feature, "index.ts"))).toBe(true);
  });

  it("gives shared its six parts", () => {
    expect(directories("shared")).toEqual(["constants", "lib", "shell", "theme", "types", "ui"]);
  });

  it.each(NAV_ITEMS)("routes $href to a page under app", ({ href }) => {
    expect(existsSync(path.join(src, "app", href, "page.tsx"))).toBe(true);
  });
});
