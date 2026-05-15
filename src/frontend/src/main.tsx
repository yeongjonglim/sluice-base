import "@mantine/core/styles.css";
import "@mantine/dates/styles.css";
import "@mantine/notifications/styles.css";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MantineProvider } from "@mantine/core";
import { ModalsProvider } from "@mantine/modals";
import { Notifications } from "@mantine/notifications";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { RouterProvider, createRouter } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import type { BrandingValue } from "@/theme/BrandingContext.tsx";
import { createAppQueryClient } from "@/api/hooks.ts";
import { createAppTheme } from "@/theme/theme.ts";
import { BrandingContext } from "@/theme/BrandingContext.tsx";
import { routeTree } from "@/routeTree.gen.ts";
// @ts-ignore — generated at build/dev time by @tanstack/router-plugin

const res = await fetch("/api/branding").catch(() => null);
const branding = res?.ok ? await res.json() : null;

const brandingValue: BrandingValue = {
  appName: branding?.appName ?? "SluiceBase",
  logoUrl: branding?.logoUrl ?? null,
  faviconUrl: branding?.faviconUrl ?? null,
};

document.title = brandingValue.appName;

if (brandingValue.faviconUrl) {
  const link =
    document.querySelector<HTMLLinkElement>("link[rel~='icon']") ??
    Object.assign(document.createElement("link"), { rel: "icon" });
  link.href = brandingValue.faviconUrl;
  document.head.appendChild(link);
}

const appTheme = createAppTheme(branding?.primaryColor ?? "teal");

const queryClient = createAppQueryClient();

const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: "intent",
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

const rootElement = document.getElementById("root")!;
createRoot(rootElement).render(
  <StrictMode>
    <BrandingContext value={brandingValue}>
      <MantineProvider theme={appTheme} defaultColorScheme="auto">
        <ModalsProvider>
          <Notifications />
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
            <ReactQueryDevtools initialIsOpen={false} />
            <TanStackRouterDevtools router={router} initialIsOpen={false} />
          </QueryClientProvider>
        </ModalsProvider>
      </MantineProvider>
    </BrandingContext>
  </StrictMode>,
);
