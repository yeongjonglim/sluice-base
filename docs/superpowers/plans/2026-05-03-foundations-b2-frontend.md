# Foundations B2 — Frontend shell, auth bootstrap, codegen

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `docs/superpowers/specs/2026-05-03-foundations-design.md` — sub-slice B2 (§9.2).

**Goal:** Stand up the frontend so an unauthenticated user is auto-redirected to Keycloak, can log in as `alice`/`dev`, and lands on a Mantine `AppShell` with their name visible — running entirely under `aspire run`.

**Architecture:** React SPA on Vite, with Mantine 9 for the UI shell, TanStack Router (file-based) for routing with a protected-route boundary, TanStack Query for server-state, and a thin `fetch` wrapper that talks to the BFF backend through the Vite dev proxy. TypeScript types for the backend's DTOs are generated from the committed `openapi.json` via `openapi-typescript`. Auth is bootstrapped at the router root: `GET /api/me` runs once on app load — 200 hydrates user state, 401 triggers `window.location.assign('/login')`. No "Sign In" button.

**Tech Stack:** React 19, Mantine 9 (`@mantine/core`, `@mantine/hooks`, `@mantine/notifications`), `@tabler/icons-react`, TanStack Router (file-based via `@tanstack/router-plugin`), TanStack Query 5, `openapi-typescript`, Vitest + React Testing Library, npm.

---

## Pre-flight: state of the frontend after B1

The user committed some scaffolding intended for B2 already during B1 work. The working state today:

- `src/frontend/vite.config.ts` already imports `@tanstack/devtools-vite`, `@tanstack/router-plugin/vite`, sets up the proxy for `/api`, `/openapi`, `/signin-oidc`, `/signout-callback-oidc`, and reads `process.env["services__api__http__0"]` (with fallback to `http://localhost:5001`).
- `src/frontend/eslint.config.js` already imports `@tanstack/eslint-config`, `eslint-plugin-react-you-might-not-need-an-effect`, `eslint-plugin-react-hooks`, `eslint-plugin-react-refresh`.
- **But `package.json` declares NONE of those imports.** As of this plan, `npm install` would not satisfy the existing config files — running `vite` or `eslint` would fail. B2 fixes this by adding all the deps that the config files (and the plan) require.
- `src/frontend/src/` still has the original Vite hero/counter scaffold (`App.tsx`, `App.css`, `assets/hero.png`, etc.) and the bare `main.tsx` that mounts `<App />`.
- `src/SluiceBase.Api/openapi.json` exists at repo root from B1 and is the codegen input.

Also worth noting: the existing `vite.config.ts` proxy targets the **HTTP** endpoint of the api (`services__api__http__0`). For BFF cookie auth this is okay on `localhost` (browsers treat localhost as a secure context for Secure-flagged cookies), so we leave that as-is. B2 only needs to add `/login` and `/logout` to the proxy (currently absent — required for the BFF login button-less navigation).

---

## File structure

**Files created:**

| Path | Responsibility |
|---|---|
| `src/frontend/src/api/client.ts` | `fetch` wrapper: cookies, JSON, antiforgery, throws `ApiError` on non-2xx. |
| `src/frontend/src/api/schema.ts` | **GENERATED** from `../SluiceBase.Api/openapi.json` via `openapi-typescript`. Do not hand-edit. |
| `src/frontend/src/api/hooks.ts` | TanStack Query hooks: `useMe()`, `useAuthedHealth()`. |
| `src/frontend/src/api/__tests__/client.test.ts` | Vitest unit test for the fetch wrapper. |
| `src/frontend/src/auth/AuthProvider.tsx` | React context exposing the current user from the `useMe()` query. |
| `src/frontend/src/theme/theme.ts` | Mantine theme: primary color, fontFamily, default color scheme. |
| `src/frontend/src/lib/notifications.ts` | Thin wrapper over `@mantine/notifications` for consistent error toasts. |
| `src/frontend/src/routes/__root.tsx` | Root layout. Auth bootstrap: calls `useMe`, redirects to `/login` on 401. |
| `src/frontend/src/routes/_authed.tsx` | Authed layout. Mantine `AppShell` with header (logo, app name, user menu, logout) and navbar (Home, Health). |
| `src/frontend/src/routes/_authed/index.tsx` | Home placeholder ("Welcome, {name}"). |
| `src/frontend/src/routes/_authed/health.tsx` | Calls `useAuthedHealth`, renders the result. |
| `src/frontend/vitest.config.ts` | Vitest config: `jsdom`, jest-dom matchers. |
| `src/frontend/src/test-setup.ts` | Setup file: imports `@testing-library/jest-dom`. |

**Files modified:**

| Path | What changes |
|---|---|
| `src/frontend/package.json` | Add Mantine 9, TanStack Router/Query, `@tanstack/eslint-config`, `@tanstack/devtools-vite`, `eslint-plugin-react-you-might-not-need-an-effect`, `openapi-typescript`, Vitest, `@testing-library/*`, jsdom, Tabler icons. Add `gen:api`, `prebuild`, `test` scripts. |
| `src/frontend/vite.config.ts` | Add `/login` and `/logout` to the proxy paths. Set `secure: false` on the proxy (api uses dev HTTPS cert when applicable). |
| `src/frontend/src/main.tsx` | Replace bare `App` mount with `MantineProvider` + `Notifications` + `QueryClientProvider` + `RouterProvider`. |
| `src/frontend/README.md` | Run instructions, dev users, login flow description. |

**Files deleted:**

| Path | Reason |
|---|---|
| `src/frontend/src/App.tsx` | Bare Vite hero/counter scaffold; replaced by routes. |
| `src/frontend/src/App.css` | Same. |
| `src/frontend/src/index.css` | Mantine ships its own global styles. |
| `src/frontend/src/assets/hero.png` | Vite-template image. |
| `src/frontend/src/assets/react.svg` | Vite-template logo. |
| `src/frontend/src/assets/vite.svg` | Vite-template logo. |
| `src/frontend/public/icons.svg` | Vite-template. |

**Files generated and committed:**

- `src/frontend/src/routeTree.gen.ts` — produced by `@tanstack/router-plugin` at build/dev time. Should be added to `.gitignore` (per the existing eslint config that already ignores it) **OR** committed depending on team convention. **Decision: commit it.** Committing makes the route tree visible in code review and avoids surprise diffs from local dev runs. The eslint global-ignore in `eslint.config.js` already excludes it from linting.

---

## Task 0: Branch setup

**Files:** none.

- [ ] **Step 1: Verify clean working tree on `feat/init`**

Run from repo root:

```bash
git status --short
```

Expected: empty output (clean tree). If anything is dirty, stop and reconcile.

- [ ] **Step 2: Create the worktree on a fresh branch**

```bash
git worktree add -b feat/foundations-b2-frontend ../sluice-base.b2
```

Expected: `Preparing worktree (new branch 'feat/foundations-b2-frontend')`. From here on, all paths are inside `/Users/voltendron/Projects/sluice-base.b2`.

---

## Task 1: Add frontend dependencies

**Files:**
- Modify: `src/frontend/package.json`

- [ ] **Step 1: Replace `package.json` with the full B2 dependency set**

Replace the contents of `src/frontend/package.json` with:

```json
{
  "name": "frontend",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "gen:api": "openapi-typescript ../SluiceBase.Api/openapi.json -o src/api/schema.ts",
    "prebuild": "npm run gen:api",
    "build": "tsc -b && vite build",
    "lint": "eslint .",
    "preview": "vite preview",
    "test": "vitest run"
  },
  "dependencies": {
    "@mantine/core": "^9.0.0",
    "@mantine/hooks": "^9.0.0",
    "@mantine/notifications": "^9.0.0",
    "@tabler/icons-react": "^3.0.0",
    "@tanstack/react-query": "^5.0.0",
    "@tanstack/react-query-devtools": "^5.0.0",
    "@tanstack/react-router": "^1.0.0",
    "@tanstack/react-router-devtools": "^1.0.0",
    "react": "^19.2.5",
    "react-dom": "^19.2.5"
  },
  "devDependencies": {
    "@eslint/js": "^10.0.1",
    "@tanstack/devtools-vite": "^0.1.0",
    "@tanstack/eslint-config": "^0.0.30",
    "@tanstack/router-plugin": "^1.0.0",
    "@testing-library/jest-dom": "^6.0.0",
    "@testing-library/react": "^16.0.0",
    "@types/node": "^24.12.2",
    "@types/react": "^19.2.14",
    "@types/react-dom": "^19.2.3",
    "@vitejs/plugin-react": "^6.0.1",
    "eslint": "^10.2.1",
    "eslint-plugin-react-hooks": "^7.1.1",
    "eslint-plugin-react-refresh": "^0.5.2",
    "eslint-plugin-react-you-might-not-need-an-effect": "^1.0.0",
    "globals": "^17.5.0",
    "jsdom": "^26.0.0",
    "openapi-typescript": "^7.0.0",
    "typescript": "~6.0.2",
    "typescript-eslint": "^8.58.2",
    "vite": "^8.0.10",
    "vitest": "^3.0.0"
  }
}
```

Notes on choices:
- The version ranges use `^` so npm picks the latest compatible at install time. Some of the `^x.0.0` entries are loose bounds — the implementer should let npm pick the actual current minor/patch.
- `@tanstack/eslint-config`, `@tanstack/devtools-vite`, and `eslint-plugin-react-you-might-not-need-an-effect` are added because the **existing** `vite.config.ts` and `eslint.config.js` files (committed during B1) already import them; without them, dev/lint break.
- Playwright (`@playwright/test`) is **not** included here — it lands in B3.
- `prebuild` runs `gen:api` before `build` so CI catches schema drift.

- [ ] **Step 2: Install**

Run from `src/frontend`:

```bash
cd src/frontend
npm install
```

Expected: `added N packages` with no peer-dependency errors. Some warnings are acceptable; errors are not. If npm complains about `@tanstack/eslint-config`'s peer deps (it sometimes pins older eslint), report DONE_WITH_CONCERNS — do NOT use `--force` or `--legacy-peer-deps` without surfacing the warning.

- [ ] **Step 3: Confirm package-lock.json was generated**

Run from `src/frontend`:

```bash
ls -la package-lock.json
```

Expected: file exists.

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/package.json src/frontend/package-lock.json
git commit -m "chore(frontend): add Mantine, TanStack, Vitest deps for B2"
```

---

## Task 2: Generate API schema types

**Files:**
- Create: `src/frontend/src/api/schema.ts` (generated)

- [ ] **Step 1: Run codegen**

Run from `src/frontend`:

```bash
npm run gen:api
```

Expected: produces `src/frontend/src/api/schema.ts`. The file content depends on what's in `../SluiceBase.Api/openapi.json` — for B1's endpoints, expect interfaces for each path (`/api/health`, `/api/health/authed`, `/api/me`, `/api/antiforgery-token`) plus a top-level `paths` and `components` shape.

- [ ] **Step 2: Verify the file was created and is non-empty**

```bash
head -20 src/frontend/src/api/schema.ts
```

Expected: typed export(s) like `export interface paths { ... }`.

- [ ] **Step 3: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/src/api/schema.ts
git commit -m "feat(frontend): generate OpenAPI types from backend schema"
```

---

## Task 3: Vite proxy — add `/login` and `/logout`

**Files:**
- Modify: `src/frontend/vite.config.ts`

- [ ] **Step 1: Replace `vite.config.ts`**

Replace the contents of `src/frontend/vite.config.ts` with:

```ts
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
```

Diff vs current: added `/login` and `/logout` proxy entries (these are real backend endpoints in the BFF flow — without proxying, Vite's catch-all serves `index.html` instead, which breaks the login button-less navigation), and added `secure: false` on every proxy entry so Vite doesn't reject the api's dev cert if it ever runs on HTTPS.

- [ ] **Step 2: Verify Vite config typechecks**

Run from `src/frontend`:

```bash
npx tsc --noEmit -p tsconfig.node.json
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/vite.config.ts
git commit -m "feat(frontend): proxy /login and /logout for BFF auth flow"
```

---

## Task 4: Mantine theme

**Files:**
- Create: `src/frontend/src/theme/theme.ts`

- [ ] **Step 1: Write the theme**

Create `src/frontend/src/theme/theme.ts`:

```ts
import { createTheme } from "@mantine/core";

export const theme = createTheme({
  primaryColor: "teal",
  primaryShade: { light: 7, dark: 5 },
  fontFamily:
    'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
  defaultRadius: "md",
});
```

Notes:
- Teal as primary is just a starting point — easy to change later. We picked it deliberately so the app doesn't look generic-Mantine-blue.
- `primaryShade` differs in light vs dark mode for readable contrast.
- System font stack avoids any web font fetch.

- [ ] **Step 2: Commit (deferred — combined with Task 5)**

The commit for this task happens at the end of Task 5.

---

## Task 5: API client (`fetch` wrapper)

**Files:**
- Create: `src/frontend/src/api/client.ts`

- [ ] **Step 1: Write the client**

Create `src/frontend/src/api/client.ts`:

```ts
const ANTIFORGERY_HEADER = "X-XSRF-TOKEN";
const ANTIFORGERY_COOKIE = "XSRF-TOKEN";
const MUTATING_METHODS = new Set(["POST", "PUT", "PATCH", "DELETE"]);

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly body: unknown,
  ) {
    super(`API request failed: ${status}`);
    this.name = "ApiError";
  }
}

function readCookie(name: string): string | undefined {
  const match = document.cookie
    .split("; ")
    .find((row) => row.startsWith(`${name}=`));
  return match?.split("=")[1];
}

export interface ApiRequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
}

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<T> {
  const method = (options.method ?? "GET").toUpperCase();
  const headers = new Headers({
    Accept: "application/json",
  });

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (MUTATING_METHODS.has(method)) {
    const token = readCookie(ANTIFORGERY_COOKIE);
    if (token) {
      headers.set(ANTIFORGERY_HEADER, decodeURIComponent(token));
    }
  }

  const response = await fetch(path, {
    method,
    headers,
    credentials: "include",
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    signal: options.signal,
  });

  const contentType = response.headers.get("content-type") ?? "";
  const isJson = contentType.includes("application/json");
  const payload = isJson ? await response.json() : await response.text();

  if (!response.ok) {
    throw new ApiError(response.status, payload);
  }

  return payload as T;
}
```

Notes:
- `credentials: "include"` is mandatory for the BFF cookie to flow.
- The antiforgery cookie name (`XSRF-TOKEN`) comes from ASP.NET's default antiforgery cookie name; the header name (`X-XSRF-TOKEN`) matches what the backend's `AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN")` configures.
- `decodeURIComponent` on the token is necessary because cookies are URL-encoded.
- We don't auto-redirect on 401 here — that's done globally in the QueryClient setup (Task 6), so that mutations and queries follow the same rule without each call site repeating it.

- [ ] **Step 2: Commit (combined with Task 4 theme)**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/src/theme/theme.ts src/frontend/src/api/client.ts
git commit -m "feat(frontend): add Mantine theme and fetch-based API client"
```

---

## Task 6: TanStack Query hooks + QueryClient

**Files:**
- Create: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Write the hooks and exported QueryClient factory**

Create `src/frontend/src/api/hooks.ts`:

```ts
import { QueryCache, QueryClient, useQuery } from "@tanstack/react-query";
import { ApiError, apiRequest } from "./client";

export interface MeResponse {
  sub: string | null;
  email: string | null;
  name: string | null;
  preferredUsername: string | null;
  roles: Array<string>;
}

export interface AuthedHealthResponse {
  status: string;
  user: string | null;
}

export function createAppQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error) => {
        if (error instanceof ApiError && error.status === 401) {
          window.location.assign("/login");
        }
      },
    }),
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status === 401) {
            return false;
          }
          return failureCount < 2;
        },
        staleTime: 30_000,
      },
    },
  });
}

export const meQueryOptions = {
  queryKey: ["me"] as const,
  queryFn: () => apiRequest<MeResponse>("/api/me"),
};

export function useMe() {
  return useQuery(meQueryOptions);
}

export function useAuthedHealth() {
  return useQuery({
    queryKey: ["health-authed"] as const,
    queryFn: () => apiRequest<AuthedHealthResponse>("/api/health/authed"),
  });
}
```

Notes:
- `createAppQueryClient` is a factory so tests and the app both get a fresh instance.
- The global `onError` is the central place that handles "session expired mid-app-use" — if any query (including `useMe`) returns 401, the SPA navigates to `/login`. This mirrors the initial-load-unauth path.
- `retry` skips retrying on 401 (no point — the user needs to re-authenticate).
- `meQueryOptions` is exported separately so the route-level `beforeLoad` can use `queryClient.ensureQueryData(meQueryOptions)` without duplicating the key.

- [ ] **Step 2: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/src/api/hooks.ts
git commit -m "feat(frontend): add TanStack Query hooks for /api/me and /api/health/authed"
```

---

## Task 7: AuthProvider context

**Files:**
- Create: `src/frontend/src/auth/AuthProvider.tsx`

- [ ] **Step 1: Write the provider**

Create `src/frontend/src/auth/AuthProvider.tsx`:

```tsx
import { createContext, type ReactNode, useContext } from "react";
import type { MeResponse } from "../api/hooks";

interface AuthContextValue {
  user: MeResponse;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({
  user,
  children,
}: {
  user: MeResponse;
  children: ReactNode;
}) {
  return <AuthContext.Provider value={{ user }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an authenticated route");
  }
  return ctx;
}
```

Notes:
- The provider is dumb — it takes a `user` prop and exposes it. The bootstrapping (calling `/api/me` and waiting for the response) happens in `__root.tsx` (Task 8); the provider is rendered inside `_authed.tsx` (Task 9) once we know we're authenticated.
- `useAuth` throws if used outside a provider — fail-fast for buggy callers.

- [ ] **Step 2: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/src/auth/AuthProvider.tsx
git commit -m "feat(frontend): add AuthProvider context and useAuth hook"
```

---

## Task 8: Root route — auth bootstrap

**Files:**
- Create: `src/frontend/src/routes/__root.tsx`

- [ ] **Step 1: Write the root route**

Create `src/frontend/src/routes/__root.tsx`:

```tsx
import {
  createRootRouteWithContext,
  Outlet,
  redirect,
} from "@tanstack/react-router";
import type { QueryClient } from "@tanstack/react-query";
import { ApiError } from "../api/client";
import { meQueryOptions } from "../api/hooks";

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
```

Notes:
- `createRootRouteWithContext<RouterContext>()` lets us pass the `QueryClient` down via router context, so `beforeLoad` doesn't need to import a singleton.
- `redirect({ href: "/login" })` — using `href:` (not `to:`) — issues a real browser navigation (server-handled), exactly what the BFF login flow needs.
- The pathname guard at the bottom is a belt-and-suspenders: if a user types `/login` directly into the URL bar, we forward them to the backend rather than letting the SPA's catch-all render (which would loop).

- [ ] **Step 2: Commit (deferred — combined with Task 9)**

---

## Task 9: Authed layout — Mantine AppShell

**Files:**
- Create: `src/frontend/src/routes/_authed.tsx`

- [ ] **Step 1: Write the authed layout**

Create `src/frontend/src/routes/_authed.tsx`:

```tsx
import {
  ActionIcon,
  AppShell,
  Burger,
  Group,
  Menu,
  NavLink,
  Text,
  Title,
  useMantineColorScheme,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconChevronDown,
  IconHeartRateMonitor,
  IconHome,
  IconLogout,
  IconMoon,
  IconSun,
} from "@tabler/icons-react";
import {
  createFileRoute,
  Link,
  Outlet,
  useLocation,
} from "@tanstack/react-router";
import { useMe } from "../api/hooks";
import { AuthProvider } from "../auth/AuthProvider";

export const Route = createFileRoute("/_authed")({
  component: AuthedLayout,
});

function AuthedLayout() {
  const me = useMe();
  const [opened, { toggle }] = useDisclosure();
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();
  const location = useLocation();

  if (!me.data) {
    // useMe is prefetched by __root's beforeLoad, so this should never render.
    return null;
  }

  const displayName =
    me.data.name ?? me.data.preferredUsername ?? me.data.email ?? "user";

  return (
    <AuthProvider user={me.data}>
      <AppShell
        header={{ height: 56 }}
        navbar={{
          width: 240,
          breakpoint: "sm",
          collapsed: { mobile: !opened },
        }}
        padding="md"
      >
        <AppShell.Header>
          <Group h="100%" px="md" justify="space-between">
            <Group gap="sm">
              <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
              <Title order={3}>SluiceBase</Title>
            </Group>
            <Group gap="xs">
              <ActionIcon
                variant="subtle"
                onClick={() => toggleColorScheme()}
                aria-label="Toggle color scheme"
              >
                {colorScheme === "dark" ? <IconSun size={18} /> : <IconMoon size={18} />}
              </ActionIcon>
              <Menu position="bottom-end" withinPortal>
                <Menu.Target>
                  <ActionIcon variant="subtle" aria-label="User menu">
                    <Group gap={4}>
                      <Text size="sm">{displayName}</Text>
                      <IconChevronDown size={14} />
                    </Group>
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item
                    component="a"
                    href="/logout"
                    leftSection={<IconLogout size={14} />}
                  >
                    Log out
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
            </Group>
          </Group>
        </AppShell.Header>

        <AppShell.Navbar p="sm">
          <NavLink
            label="Home"
            leftSection={<IconHome size={16} />}
            component={Link}
            to="/"
            active={location.pathname === "/"}
          />
          <NavLink
            label="Health"
            leftSection={<IconHeartRateMonitor size={16} />}
            component={Link}
            to="/health"
            active={location.pathname === "/health"}
          />
        </AppShell.Navbar>

        <AppShell.Main>
          <Outlet />
        </AppShell.Main>
      </AppShell>
    </AuthProvider>
  );
}
```

Notes:
- The Logout `Menu.Item` is rendered as `<a href="/logout">` (via `component="a"`), not a button-with-onclick. That's intentional — it has to be a real browser navigation through the BFF logout flow.
- `useMe` returns immediately because `__root.tsx` prefetched it via `ensureQueryData`. The `if (!me.data) return null` is defensive.
- Color scheme toggle uses Mantine's built-in scheme manager.

- [ ] **Step 2: Commit (combined with Task 8)**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/src/routes/__root.tsx src/frontend/src/routes/_authed.tsx
git commit -m "feat(frontend): add root auth bootstrap and Mantine AppShell layout"
```

---

## Task 10: Home placeholder route

**Files:**
- Create: `src/frontend/src/routes/_authed/index.tsx`

- [ ] **Step 1: Write the home page**

Create `src/frontend/src/routes/_authed/index.tsx`:

```tsx
import { Stack, Text, Title } from "@mantine/core";
import { createFileRoute } from "@tanstack/react-router";
import { useAuth } from "../../auth/AuthProvider";

export const Route = createFileRoute("/_authed/")({
  component: HomePage,
});

function HomePage() {
  const { user } = useAuth();
  const displayName =
    user.name ?? user.preferredUsername ?? user.email ?? "stranger";

  return (
    <Stack gap="xs">
      <Title order={2}>Welcome, {displayName}</Title>
      <Text c="dimmed">
        SluiceBase Foundations is up. Server registry, schema browser, query
        workspace, and approval workflow ship in later sub-projects.
      </Text>
    </Stack>
  );
}
```

- [ ] **Step 2: Commit (deferred — combined with Task 11)**

---

## Task 11: Health placeholder route

**Files:**
- Create: `src/frontend/src/routes/_authed/health.tsx`

- [ ] **Step 1: Write the health page**

Create `src/frontend/src/routes/_authed/health.tsx`:

```tsx
import { Alert, Code, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { IconAlertCircle } from "@tabler/icons-react";
import { createFileRoute } from "@tanstack/react-router";
import { useAuthedHealth } from "../../api/hooks";

export const Route = createFileRoute("/_authed/health")({
  component: HealthPage,
});

function HealthPage() {
  const health = useAuthedHealth();

  return (
    <Stack gap="md">
      <Title order={2}>Authenticated health</Title>
      <Text c="dimmed">
        Calls <Code>GET /api/health/authed</Code> via the BFF. A 200 with
        your username confirms cookie auth is wired end-to-end.
      </Text>

      {health.isPending && (
        <Group>
          <Loader size="sm" />
          <Text>Checking…</Text>
        </Group>
      )}

      {health.isError && (
        <Alert icon={<IconAlertCircle />} color="red" title="Error">
          {health.error.message}
        </Alert>
      )}

      {health.data && (
        <Alert color="teal" title="OK">
          status=<Code>{health.data.status}</Code> · user=<Code>{health.data.user ?? "(none)"}</Code>
        </Alert>
      )}
    </Stack>
  );
}
```

- [ ] **Step 2: Commit (combined with Task 10)**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/src/routes/_authed/index.tsx src/frontend/src/routes/_authed/health.tsx
git commit -m "feat(frontend): add home and authed-health placeholder routes"
```

---

## Task 12: Replace `main.tsx` with provider stack

**Files:**
- Modify: `src/frontend/src/main.tsx`

- [ ] **Step 1: Replace `main.tsx`**

Replace the contents of `src/frontend/src/main.tsx` with:

```tsx
import "@mantine/core/styles.css";
import "@mantine/notifications/styles.css";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MantineProvider } from "@mantine/core";
import { Notifications } from "@mantine/notifications";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { createRouter, RouterProvider } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import { createAppQueryClient } from "./api/hooks";
import { theme } from "./theme/theme";
// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore — generated at build/dev time by @tanstack/router-plugin
import { routeTree } from "./routeTree.gen";

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
    <MantineProvider theme={theme} defaultColorScheme="auto">
      <Notifications />
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
        <ReactQueryDevtools initialIsOpen={false} />
        <TanStackRouterDevtools router={router} initialIsOpen={false} />
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
);
```

Notes:
- Mantine's CSS imports must come before any Mantine component imports — so they go at the top.
- The `@ts-ignore` on `routeTree.gen.ts` is because the file is generated by the Vite plugin at dev/build time. After the first `npm run dev` or `npm run build`, the file exists on disk. (We commit it so cold checkouts have it; see "Files generated and committed" above.)
- The `declare module` block enables TanStack Router's typed-context machinery globally.
- `defaultColorScheme="auto"` defers to the user's OS preference.

- [ ] **Step 2: Commit (deferred — combined with Task 13)**

---

## Task 13: Remove old Vite scaffold

**Files deleted:**
- `src/frontend/src/App.tsx`
- `src/frontend/src/App.css`
- `src/frontend/src/index.css`
- `src/frontend/src/assets/hero.png`
- `src/frontend/src/assets/react.svg`
- `src/frontend/src/assets/vite.svg`
- `src/frontend/public/icons.svg`

- [ ] **Step 1: Delete the files**

Run from `src/frontend`:

```bash
rm src/App.tsx src/App.css src/index.css
rm src/assets/hero.png src/assets/react.svg src/assets/vite.svg
rm public/icons.svg
rmdir src/assets 2>/dev/null || true
```

The `rmdir` is best-effort — if `src/assets` ends up empty after the removals, drop it; if anything else is in there leave it.

- [ ] **Step 2: Commit (combined with Task 12)**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add -u src/frontend/src src/frontend/public
git add src/frontend/src/main.tsx
git commit -m "feat(frontend): mount Mantine + TanStack providers; remove Vite scaffold"
```

---

## Task 14: Vitest config and setup

**Files:**
- Create: `src/frontend/vitest.config.ts`
- Create: `src/frontend/src/test-setup.ts`

- [ ] **Step 1: Write the vitest config**

Create `src/frontend/vitest.config.ts`:

```ts
import { defineConfig } from "vitest/config";
import viteReact from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [viteReact()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    globals: false,
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
```

- [ ] **Step 2: Write the test setup**

Create `src/frontend/src/test-setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";
```

That's it. The import alone registers the matchers globally for vitest.

- [ ] **Step 3: Commit (deferred — combined with Task 15)**

---

## Task 15: Vitest test for the API client

**Files:**
- Create: `src/frontend/src/api/__tests__/client.test.ts`

- [ ] **Step 1: Write the test**

Create `src/frontend/src/api/__tests__/client.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiRequest } from "../client";

describe("apiRequest", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    document.cookie = "";
  });

  afterEach(() => {
    fetchMock.mockReset();
    vi.unstubAllGlobals();
  });

  it("includes credentials and parses JSON on 2xx", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    const result = await apiRequest<{ ok: boolean }>("/api/things");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.credentials).toBe("include");
    expect(init.method).toBe("GET");
    expect(result).toEqual({ ok: true });
  });

  it("throws ApiError on non-2xx with parsed body", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ message: "no good" }), {
        status: 401,
        headers: { "content-type": "application/json" },
      }),
    );

    await expect(apiRequest("/api/me")).rejects.toMatchObject({
      name: "ApiError",
      status: 401,
      body: { message: "no good" },
    });
    await expect(apiRequest("/api/me")).rejects.toBeInstanceOf(ApiError);
  });

  it("sends X-XSRF-TOKEN on mutations when the antiforgery cookie is present", async () => {
    document.cookie = "XSRF-TOKEN=abc%20def";
    fetchMock.mockResolvedValue(
      new Response(null, { status: 204 }),
    );

    await apiRequest("/api/things", { method: "POST", body: { x: 1 } });

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("X-XSRF-TOKEN")).toBe("abc def");
    expect(headers.get("Content-Type")).toBe("application/json");
    expect(init.body).toBe(JSON.stringify({ x: 1 }));
  });

  it("does not send X-XSRF-TOKEN on GET", async () => {
    document.cookie = "XSRF-TOKEN=abc";
    fetchMock.mockResolvedValue(
      new Response("{}", {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    await apiRequest("/api/things");

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("X-XSRF-TOKEN")).toBeNull();
  });
});
```

- [ ] **Step 2: Run the test**

Run from `src/frontend`:

```bash
npm run test
```

Expected: `Test Files  1 passed (1)` with 4 individual tests passing.

- [ ] **Step 3: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/vitest.config.ts src/frontend/src/test-setup.ts src/frontend/src/api/__tests__/client.test.ts
git commit -m "test(frontend): vitest setup + API client tests"
```

---

## Task 16: Build, lint, typecheck

**Files:** none directly — verifying everything compiles together.

- [ ] **Step 1: Run typecheck and build**

From `src/frontend`:

```bash
npm run build
```

Expected: TypeScript reports no errors; Vite produces `dist/`.

If `routeTree.gen.ts` is missing from disk at this point, the TanStack Router plugin should have generated it during the build (the plugin runs at Vite startup). If not, run `npm run dev` once briefly to generate it, then `Ctrl+C` and rebuild.

- [ ] **Step 2: Lint**

```bash
npm run lint
```

Expected: zero errors, zero warnings. ESLint already globally-ignores `routeTree.gen.ts`.

- [ ] **Step 3: Commit `routeTree.gen.ts` if not yet committed**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git status --short src/frontend/src/routeTree.gen.ts
```

If the file shows as untracked or modified:

```bash
git add src/frontend/src/routeTree.gen.ts
git commit -m "feat(frontend): commit generated TanStack Router tree"
```

If it's already tracked (was committed during a prior task), skip.

---

## Task 17: README updates

**Files:**
- Modify: `src/frontend/README.md`

- [ ] **Step 1: Replace `src/frontend/README.md`**

Replace the contents of `src/frontend/README.md` with:

```markdown
# SluiceBase frontend

React + TypeScript + Vite SPA for SluiceBase. Mantine 9 UI, TanStack Router (file-based) + TanStack Query, OpenAPI-driven type generation.

## Running

The frontend runs as part of the Aspire AppHost. From the repo root:

```bash
aspire run
```

The dashboard opens automatically. The web app is at <http://localhost:5173>.

Sign in with one of the dev users seeded in Keycloak:

| Username | Password |
|---|---|
| `alice` | `dev` |
| `bob` | `dev` |

## Auth flow

The app uses the Backend-For-Frontend (BFF) pattern:

1. SPA loads → root route runs `GET /api/me`.
2. Unauthenticated → returns 401 → SPA navigates to `/login`.
3. Backend `/login` 302s to Keycloak.
4. After Keycloak login, backend sets a session cookie and 302s back to `/`.
5. SPA reloads → `/api/me` returns 200 → Mantine shell renders.

Logging out follows the inverse path through `/logout`.

The SPA never sees an OIDC token — only a server-side session cookie.

## API contract

The backend exposes OpenAPI at `/openapi/v1.json` and writes the document to `src/SluiceBase.Api/openapi.json` on every backend build. This file is checked into the repo. The frontend's typed schema is generated from it:

```bash
npm run gen:api
```

This is also run automatically as `prebuild`. After backend changes, run `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj` (refreshes `openapi.json`), then `npm run gen:api` here.

## Scripts

| Script | Purpose |
|---|---|
| `npm run dev` | Vite dev server (used implicitly by `aspire run`). |
| `npm run build` | Type-check + Vite production build. Runs `gen:api` first. |
| `npm run gen:api` | Regenerate `src/api/schema.ts` from the backend OpenAPI document. |
| `npm run test` | Vitest unit tests. |
| `npm run lint` | ESLint. |

## Layout

- `src/routes/` — file-based TanStack Router routes (`__root.tsx` is the auth bootstrap; `_authed.tsx` is the AppShell layout for authenticated pages).
- `src/api/` — fetch client, generated schema types, query hooks.
- `src/auth/` — `AuthProvider` context exposing the current user.
- `src/theme/` — Mantine theme.
- `src/lib/` — small utilities.
```

- [ ] **Step 2: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b2
git add src/frontend/README.md
git commit -m "docs(frontend): document auth flow, scripts, and layout"
```

---

## Task 18: Final acceptance — smoke + test suite

**Files:** none.

- [ ] **Step 1: Confirm full repo build**

From the repo root:

```bash
dotnet build SluiceBase.slnx
```

Expected: `Build succeeded.` with zero warnings (B1 still passes).

```bash
cd src/frontend && npm run build && cd ../..
```

Expected: TS clean, Vite build succeeds.

- [ ] **Step 2: Run all tests**

```bash
dotnet test SluiceBase.slnx
cd src/frontend && npm run test && cd ../..
```

Expected: B1's 5 xUnit tests + B2's 4 Vitest tests all pass.

- [ ] **Step 3: Smoke test the full BFF login flow**

Start Aspire:

```bash
aspire run
```

Wait for all six resources Healthy. Then in a browser, visit <http://localhost:5173>:

1. Page should load briefly, then redirect to Keycloak's login page.
2. Sign in as `alice` / `dev`.
3. Should land back on <http://localhost:5173/> with the Mantine `AppShell` rendered, "Welcome, Alice Dev" (or similar) visible.
4. Click "Health" in the navbar — should show `status=ok user=alice`.
5. Open the user menu in the header → click "Log out". Should land back at the Keycloak login screen (or a logged-out variant), proving the logout chain completed.

If any step fails, common culprits:

- **Cookie not flowing through the proxy:** check Vite proxy entries include `/login`, `/logout`, `/signin-oidc`, `/signout-callback-oidc` and `/api`.
- **`Set-Cookie` rejected as Secure on HTTP:** browsers (Chrome 89+, Firefox) treat `localhost` as a secure context and accept Secure cookies; if you've changed hosts, this exception no longer applies — set up Vite HTTPS via `@vitejs/plugin-basic-ssl` or relax the cookie SecurePolicy in dev.
- **`/login` proxy 404:** Vite serves index.html for unmatched routes. Confirm the proxy entry exists and that `aspire run` injected `services__api__http__0`.
- **404 on `/api/me`:** the backend route name; if changed, update `meQueryOptions.queryKey` and `apiRequest` path.

Stop Aspire with `Ctrl+C`.

- [ ] **Step 4: No commit needed for smoke test alone**

If any fixes were required during smoke, commit each as its own commit with a focused message.

---

## Acceptance criteria recap (from spec §9.2)

- [x] `npm run build` clean (TS strict, ESLint clean) — Tasks 16.
- [x] `aspire run` → unauth user redirected to Keycloak → log in as alice/dev → land on Mantine shell with name visible — Task 18 step 3.
- [x] Logout link returns user to logged-out state — Task 18 step 3.
- [x] `npm run test` passes — Task 18 step 2.
- [x] B1's xUnit tests still pass — Task 18 step 2.

---

## Self-review notes

- **Spec coverage:**
  - §6.1 dependencies — Task 1.
  - §6.2 main.tsx provider stack — Task 12.
  - §6.3 routes (`__root.tsx`, `_authed.tsx`, `_authed/index.tsx`, `_authed/health.tsx`) — Tasks 8, 9, 10, 11.
  - §6.4 API client (`fetch` wrapper, antiforgery, global 401 handler) — Tasks 5 + 6.
  - §6.5 codegen (`gen:api`, `prebuild`) — Tasks 1 + 2.
  - §6.6 theme — Task 4.
  - §9.2 deliverables (Vitest config + client.test.ts, README) — Tasks 14, 15, 17.
- **Placeholder scan:** No "TBD" / "TODO" / "implement later" left in the plan.
- **Type consistency:**
  - `MeResponse` and `AuthedHealthResponse` defined in `api/hooks.ts` (Task 6) and used in `_authed/index.tsx` (Task 10) and `_authed/health.tsx` (Task 11) via `useMe()` / `useAuthedHealth()`.
  - `RouterContext` defined in `__root.tsx` (Task 8) consumed by `createRouter({ context })` in `main.tsx` (Task 12) — both use the same `QueryClient` field.
  - `meQueryOptions.queryKey` (Task 6) is referenced by `__root.tsx`'s `ensureQueryData` (Task 8) — same key.
  - `ApiError` exported from `client.ts` (Task 5) imported by `hooks.ts` (Task 6) and `__root.tsx` (Task 8).
- **Known wrinkles documented in Task 18 step 3 troubleshooting** (cookie Secure on localhost, proxy paths, env var name).
