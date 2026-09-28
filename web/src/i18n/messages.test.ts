import { createTranslator } from "next-intl";
import { describe, expect, it } from "vitest";

import { MESSAGES } from "@tests/render";

import { LOCALES, type Locale } from "@/shared/constants/locales";

type Tree = { [key: string]: string | Tree };

function leaves(tree: Tree, prefix = ""): Map<string, string> {
  const result = new Map<string, string>();
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === "string") result.set(path, value);
    else for (const [leaf, text] of leaves(value, path)) result.set(leaf, text);
  }
  return result;
}

const reference = leaves(MESSAGES.uk);

describe("message files", () => {
  it.each(LOCALES)("%s has exactly the keys of uk, the default locale", (locale) => {
    expect([...leaves(MESSAGES[locale]).keys()].sort()).toEqual([...reference.keys()].sort());
  });

  it.each(LOCALES)("%s has no empty message", (locale) => {
    const empty = [...leaves(MESSAGES[locale])].filter(([, text]) => text.trim() === "").map(([key]) => key);
    expect(empty).toEqual([]);
  });
});

describe("a count agrees with its number", () => {
  const lead = (locale: Locale, count: number) =>
    createTranslator({ locale, messages: MESSAGES[locale], namespace: "Trace" })("lead", { count }).split(" ").slice(0, 2).join(" ");

  it.each([
    ["uk", 1, "1 крок"],
    ["uk", 3, "3 кроки"],
    ["uk", 11, "11 кроків"],
    ["uk", 21, "21 крок"],
    ["uk", 84, "84 кроки"],
    ["ru", 1, "1 шаг"],
    ["ru", 5, "5 шагов"],
    ["ru", 22, "22 шага"],
    ["ru", 84, "84 шага"],
    ["en", 1, "1 step"],
    ["en", 84, "84 steps"],
    ["es", 1, "1 paso"],
    ["es", 84, "84 pasos"],
  ] as const)("%s: %i reads %s", (locale, count, text) => {
    expect(lead(locale, count)).toBe(text);
  });
});
