import { createFileRoute, redirect } from "@tanstack/react-router";

// Superseded by the unified Access surface; kept as a redirect so bookmarks survive.
export const Route = createFileRoute("/_authed/permission")({
  beforeLoad: () => {
    throw redirect({ to: "/access", search: { tab: "principal" } });
  },
});
