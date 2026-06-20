import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import viteReact from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [viteReact()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    globals: false,
    include: ["src/**/*.test.{ts,tsx}"],
    coverage: {
      provider: "v8",
      reporter: ["cobertura", "text"],
      reportsDirectory: "./coverage",
      exclude: [
        "src/api/schema.ts",
        "src/routeTree.gen.ts",
        "src/main.tsx",
      ],
      thresholds: {
        lines: 65,
        branches: 42,
        functions: 58,
        statements: 64,
      },
    },
  },
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
});
