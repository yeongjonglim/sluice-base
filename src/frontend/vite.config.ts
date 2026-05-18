import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { devtools } from "@tanstack/devtools-vite";
import viteReact from "@vitejs/plugin-react";

import { tanstackRouter } from "@tanstack/router-plugin/vite";

const port = Number(process.env["PORT"] ?? 5173);

// When running via Aspire, VITE_BASE_URL is set to the frontend's own public URL
// (e.g. https://localhost:5173). The backend uses this as the base for absolute
// asset URLs so the browser fetches JS/CSS/HMR directly from Vite.
const viteBaseUrl = process.env["VITE_BASE_URL"] ?? `http://localhost:${port}`;

export default defineConfig(({ command }) => ({
  base: command === "serve" ? `${viteBaseUrl}/` : "/",
  plugins: [
    devtools(),
    tanstackRouter({
      target: "react",
      autoCodeSplitting: true,
      routeFileIgnorePattern: "__tests__",
    }),
    viteReact(),
  ],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  server: {
    port,
    // No proxy needed — the document is served from the backend port so all
    // route-relative fetches (/api, /login, /logout, etc.) resolve to the
    // backend natively. Vite's dev server includes permissive CORS headers
    // by default, so cross-origin module loading from the backend document
    // origin works without any additional configuration.
  },
}));
