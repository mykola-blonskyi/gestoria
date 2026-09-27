import path from "node:path";

import { ESLint } from "eslint";
import { describe, expect, it } from "vitest";

const root = path.resolve(import.meta.dirname, "..");
const eslint = new ESLint({ cwd: root });

// Lints one import as if it were written in `file`, and returns the layer messages it raises.
async function layerErrors(file: string, importPath: string): Promise<string[]> {
  const [result] = await eslint.lintText(`import * as target from "${importPath}";\nexport { target };\n`, {
    filePath: path.join(root, file),
  });
  return (result?.messages ?? [])
    .filter((message) => message.ruleId === "import/no-restricted-paths")
    .map((message) => message.message);
}

describe("layer rules", () => {
  it.each([
    ["data", "src/data/fixture.ts"],
    ["features", "src/features/payments/components/fixture.tsx"],
    ["shared", "src/shared/lib/fixture.ts"],
    ["i18n", "src/i18n/fixture.ts"],
  ])("rejects %s importing app", async (_, file) => {
    expect(await layerErrors(file, "@/app/layout")).toEqual([
      expect.stringContaining("Nothing imports app: it holds routes and layouts only."),
    ]);
  });

  it.each([
    ["features", "@/features/payments"],
    ["i18n", "@/i18n/request"],
  ])("rejects data importing %s", async (_, importPath) => {
    expect(await layerErrors("src/data/fixture.ts", importPath)).toEqual([
      expect.stringContaining("data imports only shared."),
    ]);
  });

  it.each([
    ["features", "@/features/payments"],
    ["data", "@/data/client"],
    ["i18n", "@/i18n/request"],
  ])("rejects shared importing %s", async (_, importPath) => {
    expect(await layerErrors("src/shared/lib/fixture.ts", importPath)).toEqual([
      expect.stringContaining("shared is the bottom layer"),
    ]);
  });

  it.each([
    ["features", "@/features/payments"],
    ["data", "@/data/client"],
  ])("rejects i18n importing %s", async (_, importPath) => {
    expect(await layerErrors("src/i18n/fixture.ts", importPath)).toEqual([
      expect.stringContaining("i18n imports only shared."),
    ]);
  });

  it.each([
    ["its public entry", "@/features/transactions"],
    ["an internal file", "@/features/transactions/components/transactions-page"],
    ["a relative path", "../../transactions"],
  ])("rejects a feature importing another feature through %s", async (_, importPath) => {
    expect(await layerErrors("src/features/payments/components/fixture.tsx", importPath)).toEqual([
      expect.stringContaining("Features do not import each other."),
    ]);
  });

  it.each([
    ["a component", "@/features/payments/components/payments-page"],
    ["a test", "@/features/payments/tests/payments-page.test"],
  ])("rejects app importing %s of a feature instead of its index.ts", async (_, importPath) => {
    expect(await layerErrors("src/app/payments/fixture.tsx", importPath)).toEqual([
      expect.stringContaining("Import a feature only through its index.ts."),
    ]);
  });

  it.each([
    ["app", "a feature's index.ts", "src/app/payments/fixture.tsx", "@/features/payments"],
    ["app", "data", "src/app/fixture.tsx", "@/data/query-provider"],
    ["app", "shared", "src/app/fixture.tsx", "@/shared/shell/app-shell"],
    ["a feature", "its own files", "src/features/payments/fixture.ts", "./components/payments-page"],
    ["a feature", "data", "src/features/payments/components/fixture.tsx", "@/data/client"],
    ["a feature", "shared", "src/features/payments/components/fixture.tsx", "@/shared/ui/empty-state"],
    ["data", "shared", "src/data/fixture.ts", "@/shared/constants/locales"],
    ["i18n", "shared", "src/i18n/fixture.ts", "@/shared/constants/locales"],
    ["shared", "shared", "src/shared/shell/fixture.tsx", "@/shared/ui/card"],
  ])("allows %s to import %s", async (_, __, file, importPath) => {
    expect(await layerErrors(file, importPath)).toEqual([]);
  });
});

describe("console", () => {
  it("rejects console calls in the app, so no financial value reaches the browser console (SPEC-013)", async () => {
    const [result] = await eslint.lintText('console.log("1234.56");\n', {
      filePath: path.join(root, "src/features/payments/components/fixture.tsx"),
    });
    expect(result?.messages.map((message) => message.ruleId)).toEqual(["no-console"]);
  });
});
