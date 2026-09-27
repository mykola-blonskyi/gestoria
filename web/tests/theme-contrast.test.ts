import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

import { THEMES, type Theme } from "@/shared/theme/themes";

const css = readFileSync(path.resolve(import.meta.dirname, "../src/app/globals.css"), "utf8");

function tokensOf(theme: string): Map<string, string> {
  const block = new RegExp(`\\[data-theme="${theme}"\\]\\s*\\{([^}]*)\\}`).exec(css)?.[1];
  if (block === undefined) throw new Error(`globals.css has no [data-theme="${theme}"] block`);
  return new Map([...block.matchAll(/--([\w-]+):\s*([^;]+);/g)].map((match) => [match[1]!, match[2]!.trim()]));
}

// WCAG 2.2 relative luminance and contrast ratio, for opaque #rrggbb colours.
function luminance(hex: string): number {
  const match = /^#([0-9a-f]{6})$/i.exec(hex);
  if (match === null) throw new Error(`${hex} is not an opaque #rrggbb colour`);
  const [r, g, b] = [0, 2, 4].map((offset) => {
    const channel = parseInt(match[1]!.slice(offset, offset + 2), 16) / 255;
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!;
}

function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light! + 0.05) / (dark! + 0.05);
}

// [text, surface] pairs the components actually render.
const TEXT_PAIRS = [
  ["foreground", "background"],
  ["card-foreground", "card"],
  ["popover-foreground", "popover"],
  ["primary-foreground", "primary"],
  ["secondary-foreground", "secondary"],
  ["muted-foreground", "muted"],
  ["muted-foreground", "background"],
  ["muted-foreground", "card"],
  ["accent-foreground", "accent"],
  ["primary", "background"],
  ["destructive", "background"],
  ["destructive", "card"],
] as const;

// Focus rings and form-control borders are non-text UI (WCAG 1.4.11): 3:1.
const UI_PAIRS = [
  ["ring", "background"],
  ["ring", "card"],
  ["input", "background"],
  ["input", "card"],
] as const;

const TOKENS = [...new Set([...TEXT_PAIRS, ...UI_PAIRS].flat()), "border"];

const textMinimum = (theme: Theme) => (theme === "contrast" ? 7 : 4.5);

describe("theme tokens", () => {
  it("define a block for every theme and no other", () => {
    const blocks = [...css.matchAll(/\[data-theme="(\w+)"\]\s*\{/g)].map((match) => match[1]);
    expect(blocks.sort()).toEqual([...THEMES].sort());
  });

  it.each(THEMES)("%s defines every token", (theme) => {
    const tokens = tokensOf(theme);
    expect(TOKENS.filter((token) => !tokens.has(token))).toEqual([]);
  });

  describe.each(THEMES)("%s", (theme) => {
    const tokens = tokensOf(theme);
    const ratio = (a: string, b: string) => contrast(tokens.get(a)!, tokens.get(b)!);

    it.each(TEXT_PAIRS)(
      `%s on %s reaches ${textMinimum(theme)}:1 (WCAG ${theme === "contrast" ? "AAA" : "AA"} body text)`,
      (text, surface) => {
        expect(ratio(text, surface)).toBeGreaterThanOrEqual(textMinimum(theme));
      },
    );

    it.each(UI_PAIRS)("%s on %s reaches 3:1", (ui, surface) => {
      expect(ratio(ui, surface)).toBeGreaterThanOrEqual(3);
    });
  });
});

// The pairs above test the tokens at full strength. A translucent ring or outline
// (ring-ring/50) would draw a weaker colour than the one tested.
describe("focus indicators", () => {
  it("use the ring colour at full strength everywhere in src", () => {
    const files = readdirSync(path.resolve(import.meta.dirname, "../src"), { recursive: true, encoding: "utf8" })
      .filter((file) => /\.(tsx?|css)$/.test(file))
      .filter((file) => /(ring|outline)-ring\/\d+/.test(readFileSync(path.resolve(import.meta.dirname, "../src", file), "utf8")));
    expect(files).toEqual([]);
  });
});

describe("contrast", () => {
  it("matches the WCAG reference values", () => {
    expect(contrast("#000000", "#ffffff")).toBeCloseTo(21, 5);
    expect(contrast("#777777", "#ffffff")).toBeCloseTo(4.48, 2);
  });
});
