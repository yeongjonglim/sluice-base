import { Outlet, createRootRouteWithContext, redirect } from "@tanstack/react-router";
import { ApiError } from "../api/client";
import { meQueryOptions } from "../api/hooks";
import type { QueryClient } from "@tanstack/react-query";

export interface RouterContext {
  queryClient: QueryClient;
}

export const Route = createRootRouteWithContext<RouterContext>()({
  beforeLoad: async ({ context, location }) => {
    try {
      await context.queryClient.ensureQueryData(meQueryOptions);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        throw redirect({ href: "/login" });
      }
      throw error;
    }
    if (location.pathname === "/login" || location.pathname === "/logout") {
      // These are server-rendered navigations; the SPA should not handle them.
      window.location.assign(location.pathname);
    }
  },
  component: () => <Outlet />,
});
