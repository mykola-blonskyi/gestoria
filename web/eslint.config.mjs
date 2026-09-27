import { readdirSync } from "node:fs";
import path from "node:path";

import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import { defineConfig, globalIgnores } from "eslint/config";

const root = import.meta.dirname;

const features = readdirSync(path.join(root, "src/features"), { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .map((entry) => entry.name);

// The layer rules of web/README.md. tests/layers.test.ts proves each zone rejects a violation.
const layerZones = [
  {
    target: ["./src/data", "./src/features", "./src/shared", "./src/i18n"],
    from: "./src/app",
    message: "Nothing imports app: it holds routes and layouts only.",
  },
  {
    target: "./src/data",
    from: ["./src/features", "./src/i18n"],
    message: "data imports only shared.",
  },
  {
    target: "./src/shared",
    from: ["./src/features", "./src/data", "./src/i18n"],
    message: "shared is the bottom layer and imports nothing from app, features, data or i18n.",
  },
  {
    target: "./src/i18n",
    from: ["./src/features", "./src/data"],
    message: "i18n imports only shared.",
  },
  ...features.map((feature) => ({
    target: `./src/features/${feature}`,
    from: "./src/features",
    except: [`./${feature}`],
    message: "Features do not import each other. Move what they share to shared or data.",
  })),
  {
    target: "./src/app",
    from: ["./src/features/*/*/**", "./src/features/*/!(index).{ts,tsx}"],
    message: "Import a feature only through its index.ts.",
  },
];

export default defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    files: ["src/**/*.{ts,tsx}"],
    rules: {
      "import/no-restricted-paths": ["error", { basePath: root, zones: layerZones }],
      // SPEC-013: nothing about the user's finances may reach the browser console.
      "no-console": "error",
    },
  },
  globalIgnores([".next/**", "out/**", "build/**", "coverage/**", "next-env.d.ts", "src/data/api-types.ts"]),
]);
