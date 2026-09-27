import path from "node:path";

import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "src"),
      "@tests": path.resolve(import.meta.dirname, "tests"),
    },
  },
  test: {
    environment: "jsdom",
    // West of UTC, so a date parsed or formatted in local time shows the previous day and fails.
    env: { TZ: "Pacific/Honolulu" },
    setupFiles: ["./tests/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}", "tests/**/*.test.{ts,tsx}"],
    server: {
      deps: {
        inline: ["next-intl"],
      },
    },
  },
});
