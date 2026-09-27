import { describe, expect, it } from "vitest";

import { MESSAGES } from "@tests/render";

import { LOCALES } from "@/shared/constants/locales";

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
