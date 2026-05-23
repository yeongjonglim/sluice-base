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
  build: {
    rolldownOptions: {
      output: {
        manualChunks(id) {
          if (id.includes("node_modules")) {
            if (id.includes("react-dom") || id.includes("/react/"))
              return "vendor/react";
            if (id.includes("@mantine")) return "vendor/mantine";
            if (id.includes("@tanstack")) return "vendor/tanstack";
            if (id.includes("@codemirror")) return "vendor/codemirror";
            if (id.includes("@uiw")) return "vendor/codemirror-ui";
            if (id.includes("@tabler")) return "vendor/icons";
          }
        },
      },
    },
  },
  server: {
    port,
    allowedHosts: true,
  },
});
