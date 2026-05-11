import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { devtools } from "@tanstack/devtools-vite";
import viteReact from "@vitejs/plugin-react";

import { tanstackRouter } from "@tanstack/router-plugin/vite";

const apiUrl = process.env["services__api__http__0"] ?? "http://localhost:5001";

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
    port: Number(process.env.PORT ?? 5173),
    proxy: {
      "/api": { target: apiUrl, changeOrigin: false, secure: false },
      "/openapi": { target: apiUrl, changeOrigin: false, secure: false },
      "/login": { target: apiUrl, changeOrigin: false, secure: false },
      "/logout": { target: apiUrl, changeOrigin: false, secure: false },
      "/signin-oidc": { target: apiUrl, changeOrigin: false, secure: false },
      "/signout-callback-oidc": { target: apiUrl, changeOrigin: false, secure: false },
    },
  },
});
