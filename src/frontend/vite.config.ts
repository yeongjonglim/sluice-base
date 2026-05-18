import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { devtools } from "@tanstack/devtools-vite";
import viteReact from "@vitejs/plugin-react";

import { tanstackRouter } from "@tanstack/router-plugin/vite";

const port = Number(process.env["PORT"] ?? 5173);

export default defineConfig({
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
    // The backend proxies all unmatched requests to Vite (including /@vite/*, /src/*, etc.)
    // so base = "/" works correctly. HMR is configured to connect directly to Vite's own
    // port rather than through the backend, so the WebSocket doesn't need proxying.
    hmr: {
      protocol: "ws",
      clientPort: port,
    },
  },
});
