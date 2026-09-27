import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";

const web = path.resolve(import.meta.dirname, "..");
const document = path.resolve(web, "../src/GestorIA.Api/openapi/v1.json");
const output = path.resolve(web, "src/data/api-types.ts");

if (!existsSync(document)) {
  console.error(
    [
      `api:types: no OpenAPI document at ${path.relative(web, document)}.`,
      "GestorIA.Api emits it at build time (dotnet build GestorIA.slnx), and it does not do so yet (#66).",
      "Nothing was generated: the web types come only from the API's own document.",
    ].join("\n"),
  );
  process.exit(1);
}

execFileSync(path.join(web, "node_modules/.bin/openapi-typescript"), [document, "--output", output], {
  stdio: "inherit",
});
