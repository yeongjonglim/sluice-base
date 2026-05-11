# SluiceBase Foundations — Design

**Date:** 2026-05-03
**Status:** Proposed
**Sub-project:** 1 of 6 (Foundations)
**Successor sub-projects:** 2. Permission model, 3. Server registry, 4. Schema browser, 5. Query workspace, 6. Approval workflow.

## 1. Purpose & scope

SluiceBase is a controlled gateway for users to query authorized databases through a UI, with logging, permissioning, and an approval workflow for mutating queries. It will be open-sourced.

**Foundations** establishes the substrate that every later slice will lean on: backend project layout, BFF authentication against Keycloak, EF Core wiring against the metadata DB, the `ITargetEngine` abstraction with a Postgres implementation, the React/Mantine/TanStack frontend shell, the OpenAPI → TypeScript codegen pipeline, and a working test harness.

Foundations is **not** a product feature on its own — there is nothing for an end user to *do* yet beyond log in. Its purpose is to make every later slice cheap to build.

### In scope

- Backend projects (`SluiceBase.Api`, `SluiceBase.Core`) and integration test project.
- BFF auth: cookie session backed by ASP.NET OpenIdConnect handler against the existing Keycloak realm `sluicebase`.
- EF Core + Npgsql against the Aspire-provisioned `metadata-pg`, with EF Migrations.
- `ITargetEngine` (in `Core`) with `PostgresTargetEngine` implementation (in `Api`).
- Frontend: Mantine 9, TanStack Router (file-based), TanStack Query, OpenAPI → `openapi-typescript` codegen, Vite dev proxy.
- Test harness: xUnit + `Aspire.Hosting.Testing`, Vitest, Playwright (one happy-path E2E).
- Aspire AppHost edits to wire it all together.

### Out of scope (deferred to later sub-projects)

- Permission/role model and enforcement (Sub-project 2).
- Server registry, connection-string encryption, server CRUD UI (Sub-project 3).
- Schema introspection, table/column browser (Sub-project 4).
- Query execution, results rendering, query log (Sub-project 5).
- Approval workflow for mutating queries (Sub-project 6).
- `DevSeedHook` activation (deferred — file stays on disk but is not registered with the AppHost).
- CI configuration (separate later slice).
- Production deployment story (separate later slice).

### Success criteria

A reviewer cloning the repo and running `aspire run` from the root must observe:

1. All six Aspire resources reach Healthy: `metadata-pg`, `target-blue-pg`, `target-green-pg`, `keycloak`, `api`, `web`.
2. Browsing to `http://localhost:5173`:
   - An unauthenticated user is automatically redirected to Keycloak (no "Sign in" button shown).
   - After authenticating as `alice` / `dev`, the user lands on a Mantine `AppShell` with their name visible.
   - The shell includes a working logout link that ends both the SluiceBase session and the Keycloak session.
3. `curl -i https://localhost:7024/api/me` returns **401** (not 302) with no cookie.
4. `dotnet test` passes the integration test suite against an Aspire-booted Postgres.
5. `npm run test` and `npm run test:e2e` pass.
6. README documents how to run, the dev user credentials, and the test commands.

## 2. Architectural decisions (locked in via brainstorm)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Decompose v1 into 6 sub-projects, ship Foundations first | Single-spec scope for v1 was too broad; smaller specs review better. |
| 2 | Backend layout: `SluiceBase.Api` + `SluiceBase.Core`, no separate `Infrastructure` or `Contracts` projects | Two-project split captures the "domain vs HTTP" boundary that matters; further splits don't earn their cost yet. OpenAPI codegen replaces a `Contracts` project. |
| 3 | Auth pattern: BFF (cookie session, server-side OIDC) — not SPA-with-tokens | More secure token storage; the SPA never sees an access token. Adds backend complexity (cookies, anti-forgery, dev proxy) but stays well within ASP.NET's built-in capabilities. |
| 4 | Use built-in `Microsoft.AspNetCore.Authentication.OpenIdConnect`, **not Duende.BFF** | Duende.BFF is commercial-licensed for non-trivial use; SluiceBase will be open-source and must avoid that constraint. |
| 5 | Backend returns 401 for `/api/*` paths on unauth (not the default 302 to login) | A 302 to Keycloak's HTML login is a footgun for `fetch` (returns HTML at status 200, indistinguishable from success). 401 keeps the API contract uniform — initial-load-unauth and session-expired-mid-use go through one error path. |
| 6 | Frontend has no "Sign In" button; unauth users are auto-redirected to `/login` | Standard SPA-on-BFF pattern; reduces UI surface for an unauthenticated state that doesn't really exist in this product. |
| 7 | Data stack: EF Core + Npgsql + EF Migrations for the metadata DB | CRUD-heavy small schema; productivity wins over Dapper. Tooling (`dotnet ef`) is friction-free. |
| 8 | API contracts: `Microsoft.AspNetCore.OpenApi` + `openapi-typescript` codegen on the frontend | Generated TypeScript types stay in lockstep with the backend at zero runtime cost. `openapi.json` is checked into the repo so frontend dev doesn't need the backend running. |
| 9 | API style: Minimal APIs over Controllers | Idiomatic for .NET 10, less boilerplate, cleaner OpenAPI generation. |
| 10 | Frontend router: TanStack Router file-based | Discoverable layout for OSS contributors; the Vite plugin makes the route-tree codegen invisible. |
| 11 | Mantine 9.x as the UI library; Tabler icons | User preference; v9 is the current major. No Tailwind. |
| 12 | Test stack: xUnit + `Aspire.Hosting.Testing` + Vitest + Playwright (chromium) | Reuses AppHost as the source of truth for tests (no Testcontainers config duplication). Playwright covers the OIDC round-trip end-to-end. |
| 13 | Migrations apply automatically on backend startup in dev (`Migrations:AutoApply: true`); production deployers run them manually | Frictionless dev loop; production keeps the safer manual pattern. |
| 14 | Foundations split into three serial PRs (B1, B2, B3) | Each is independently shippable; the highest-risk work (BFF + Keycloak) lands first and de-risks the rest. |

## 3. Repository layout

```
sluice-base/
├── SluiceBase.slnx
├── docs/
│   └── superpowers/specs/
│       └── 2026-05-03-foundations-design.md   # this document
├── src/
│   ├── AppHost/                                # existing — minor edits in B1
│   ├── ServiceDefaults/                        # existing — unchanged
│   ├── SluiceBase.Api/                         # NEW
│   │   ├── SluiceBase.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Properties/launchSettings.json      # pins https://localhost:7024
│   │   ├── openapi.json                        # generated, committed
│   │   ├── Auth/
│   │   │   ├── AuthSetup.cs                    # AddAuthentication/Cookie/OIDC config + event overrides
│   │   │   └── AntiforgerySetup.cs
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs                 # only DataProtectionKeys for Foundations
│   │   │   └── Migrations/
│   │   ├── Endpoints/
│   │   │   ├── EndpointMapper.cs               # app.MapAllEndpoints() aggregator
│   │   │   ├── AuthEndpoints.cs                # /login, /logout, /api/me, /api/antiforgery-token
│   │   │   └── HealthEndpoints.cs              # /api/health, /api/health/authed
│   │   └── Targets/
│   │       └── PostgresTargetEngine.cs         # ITargetEngine impl
│   ├── SluiceBase.Core/                        # NEW
│   │   ├── SluiceBase.Core.csproj              # no infra deps
│   │   └── Targets/
│   │       └── ITargetEngine.cs
│   └── frontend/
│       ├── package.json                        # deps additions in B2
│       ├── vite.config.ts                      # proxy additions in B2
│       ├── playwright.config.ts                # NEW (B3)
│       ├── vitest.config.ts                    # NEW (B2)
│       ├── e2e/login.spec.ts                   # NEW (B3)
│       └── src/
│           ├── main.tsx                        # mounts MantineProvider/RouterProvider/QueryClientProvider/Notifications
│           ├── routes/                         # file-based, route-tree codegen target
│           │   ├── __root.tsx                  # auth bootstrap; calls /api/me; on 401 → window.location.assign('/login')
│           │   ├── _authed.tsx                 # Mantine AppShell layout
│           │   ├── _authed/index.tsx           # placeholder home
│           │   └── _authed/health.tsx          # placeholder authed-health page
│           ├── api/
│           │   ├── schema.ts                   # GENERATED (do not edit)
│           │   ├── client.ts                   # fetch wrapper: credentials:'include' + antiforgery
│           │   ├── hooks.ts                    # TanStack Query hooks (useMe, useAuthedHealth)
│           │   └── __tests__/client.test.ts    # Vitest
│           ├── auth/AuthProvider.tsx
│           ├── theme/theme.ts                  # Mantine theme + color scheme manager
│           └── lib/notifications.ts
└── tests/
    └── SluiceBase.Api.IntegrationTests/        # NEW (B1, upgraded in B3)
        └── ... see §7
```

`Directory.Build.props` already enforces `TreatWarningsAsErrors=true`, nullable, and analyzers for everything under `src/`. New projects inherit this automatically.

## 4. Backend internals

### 4.1 `SluiceBase.Core`

Infrastructure-free. References only the BCL and `Microsoft.Extensions.Logging.Abstractions` if needed. Nothing else.

```csharp
namespace SluiceBase.Core.Targets;

public interface ITargetEngine
{
    string Kind { get; }                                      // "postgres", "mysql", ...
    Task<ConnectivityResult> TestConnectionAsync(
        string connectionString,
        CancellationToken ct);
}

public sealed record ConnectivityResult(bool Ok, string? Error);
```

That is the *entire* public surface for Foundations. Schema introspection, query execution, and parameter handling will be added by later sub-projects, when their shapes are pinned down by real consumers.

### 4.2 `SluiceBase.Api`

#### Composition root (`Program.cs`)

Order of registrations:

1. `AddServiceDefaults()` (Aspire — OTel, health checks, service discovery).
2. `AddDbContext<AppDbContext>` against connection string `Metadata` (Aspire injects).
3. `AddDataProtection().PersistKeysToDbContext<AppDbContext>()` so cookie keys survive restarts.
4. `AddAuthentication` (cookie default scheme + OIDC challenge scheme), with the event overrides described in §5.
5. `AddAuthorization()`.
6. `AddAntiforgery()` with header name `X-XSRF-TOKEN`.
7. `AddOpenApi()`.
8. Singleton: `ITargetEngine → PostgresTargetEngine`.
9. After `app = builder.Build()`: optional auto-migrate (see §4.4); `UseAuthentication`; `UseAuthorization`; `UseAntiforgery`; `MapOpenApi`; `MapAllEndpoints()`; `MapDefaultEndpoints()` (Aspire health).

#### Postgres target engine

```csharp
internal sealed class PostgresTargetEngine : ITargetEngine
{
    public string Kind => "postgres";

    public async Task<ConnectivityResult> TestConnectionAsync(
        string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            var result = await cmd.ExecuteScalarAsync(ct);
            return new ConnectivityResult(result is 1, null);
        }
        catch (Exception ex)
        {
            return new ConnectivityResult(false, ex.Message);
        }
    }
}
```

When a second engine arrives, this becomes a keyed registration / factory keyed on `Kind`. Not built yet (YAGNI).

#### EF Core

`AppDbContext` for Foundations holds **only** the data-protection keys table (`DataProtectionKey` entity provided by `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`). All product tables (servers, permissions, query log, approval requests) are introduced by later sub-projects.

Migrations live at `src/SluiceBase.Api/Data/Migrations/`. The initial migration `0001_Init` creates only `DataProtectionKeys`.

#### Endpoints

```
src/SluiceBase.Api/Endpoints/
├── EndpointMapper.cs           # public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app)
├── AuthEndpoints.cs
└── HealthEndpoints.cs
```

Each endpoint module exposes a `Map(IEndpointRouteBuilder)` method. `EndpointMapper` calls them all. No reflection-based discovery.

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/login` | GET | Anon | `Challenge(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" })` |
| `/logout` | GET | Anon | Sign out cookie + OIDC schemes; redirects to Keycloak end-session, then to `Frontend__BaseUrl` |
| `/signin-oidc` | (handled by OIDC middleware) | — | OIDC callback path |
| `/signout-callback-oidc` | (handled by OIDC middleware) | — | Post-logout callback |
| `/api/me` | GET | Required | Returns `{ sub, email, name, preferredUsername, roles[] }` from the cookie principal |
| `/api/antiforgery-token` | GET | Required | Issues the antiforgery cookie pair, returns `{ headerName }` |
| `/api/health` | GET | Anon | `{ status: "ok" }` |
| `/api/health/authed` | GET | Required | `{ status: "ok", user: <preferredUsername> }` |
| `/openapi/v1.json` | GET | Anon | OpenAPI document |

#### OpenAPI generation

- Runtime: `app.MapOpenApi()` exposes the live document.
- Build-time export: a `dotnet build` target uses `Microsoft.Extensions.ApiDescription.Server` to write the document to `src/SluiceBase.Api/openapi.json`. CI (when added) verifies the committed file matches the build output.

### 4.3 Configuration

| Source | Contents |
|---|---|
| `appsettings.json` | Structural defaults; no secrets. |
| `appsettings.Development.json` | `Migrations:AutoApply: true`. Verbose logging tweaks. |
| Environment variables (Aspire-injected) | `ConnectionStrings:Metadata`, `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret`, `Frontend__BaseUrl`. Take precedence over appsettings. |
| `dotnet user-secrets` | Reserved for developer-local overrides. |

### 4.4 Migrations

- `appsettings.json` (prod default): explicit `Migrations:AutoApply: false`.
- `appsettings.Development.json` (dev override): `Migrations:AutoApply: true`. When true, startup runs `app.Services.GetRequiredService<AppDbContext>().Database.MigrateAsync()`.
- Production: deployers run `dotnet ef database update` manually. The README will recommend a future Aspire `MigrationService` resource for production once the schema gets non-trivial — out of scope for Foundations.

## 5. Authentication (BFF)

### 5.1 Login flow

1. User loads the app at `http://localhost:5173/`. SPA mounts.
2. `__root.tsx` `beforeLoad` issues `GET /api/me` via the API client.
3. Vite dev proxy forwards to `https://localhost:7024/api/me`.
4. Backend cookie middleware finds no cookie; **the `OnRedirectToLogin` event override returns 401** (because the path starts with `/api`).
5. Frontend's auth bootstrap sees 401; calls `window.location.assign('/login')`.
6. Browser navigates to `/login` (proxied to backend).
7. Backend's `/login` endpoint calls `Challenge(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" })` → 302 to Keycloak's `/auth` with PKCE.
8. User authenticates at Keycloak.
9. Keycloak 302s to `https://localhost:7024/signin-oidc` with the auth code.
10. ASP.NET OIDC handler exchanges the code for tokens, builds the cookie principal with claims `sub`, `email`, `name`, `preferred_username`, and `realm_access.roles` mapped to `ClaimTypes.Role`. Tokens are stored in the auth properties (server-side only, never sent to the browser).
11. 302 to `/` → frontend reloads → `GET /api/me` returns 200 → SPA renders Mantine shell.

### 5.2 Logout flow

1. User clicks "Log out" — an `<a href="/logout">` in the Mantine user menu (anchor, not fetch).
2. Browser navigates to backend `/logout`.
3. Backend signs out cookie + OIDC schemes.
4. OIDC handler 302s to Keycloak's end-session endpoint with a `post_logout_redirect_uri` of `https://localhost:7024/signout-callback-oidc`.
5. Keycloak ends the realm session and 302s back.
6. Backend's signout-callback issues a final redirect to `Frontend__BaseUrl` (`http://localhost:5173`).
7. SPA mounts → `GET /api/me` → 401 → redirect to `/login` → loops back into the login flow.

### 5.3 Cookie configuration

| Setting | Value |
|---|---|
| Cookie name | `sb.auth` |
| HttpOnly | `true` |
| Secure | `true` |
| SameSite | `Lax` (must allow the post-Keycloak top-level redirect to retain the cookie) |
| Sliding expiration | `8h` |
| Absolute expiration | `12h` |
| DataProtection key store | EF (`PersistKeysToDbContext<AppDbContext>`) |

### 5.4 401-on-API override (the central BFF fix)

```csharp
options.Events.OnRedirectToLogin = ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
    ctx.Response.Redirect(ctx.RedirectUri);
    return Task.CompletedTask;
};
options.Events.OnRedirectToAccessDenied = ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
    ctx.Response.Redirect(ctx.RedirectUri);
    return Task.CompletedTask;
};
```

Page-style paths (`/login`, `/signin-oidc`) keep their normal redirect behavior — they're real navigations, not fetches.

### 5.5 Anti-forgery

- `services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");`
- Endpoint convention: every non-`GET`/`HEAD` Minimal API endpoint chains `.RequireAntiforgery()`. Foundations doesn't have such endpoints yet; the convention is documented in the spec for later slices.
- `GET /api/antiforgery-token` calls `IAntiforgery.GetAndStoreTokens(ctx)` to set a readable cookie + returns the header name. The SPA's `apiClient` reads that cookie on first authed load and adds the corresponding header to mutations.

### 5.6 Keycloak realm wiring

Edit `src/AppHost/seed/keycloak/realm.json`:

```diff
- "redirectUris": [
-   "http://localhost:5173/signin-oidc",
-   "https://localhost:7024/signin-oidc"
- ],
+ "redirectUris": [
+   "https://localhost:7024/signin-oidc"
+ ],
+ "postLogoutRedirectUris": "https://localhost:7024/signout-callback-oidc",
```

The `localhost:5173` redirect URI is wrong for BFF — the SPA never sees the OIDC callback. Only the backend's URL matters.

### 5.7 Vite dev proxy

`vite.config.ts` proxies `/api`, `/login`, `/logout`, `/signin-oidc`, `/signout-callback-oidc` to the backend. Backend URL is read from `import.meta.env.VITE_API_URL`. Aspire's `WithReference(api)` injects connection-info env vars without the `VITE_` prefix, so Vite cannot expose them to client config — the AppHost explicitly sets `VITE_API_URL` on the `web` resource (see §8). Cookies flow because the browser sees a single origin (`localhost:5173`).

## 6. Frontend

### 6.1 Dependencies (added in B2)

**Runtime:** `@mantine/core@^9`, `@mantine/hooks@^9`, `@mantine/notifications@^9`, `@tabler/icons-react`, `@tanstack/react-router`, `@tanstack/react-query`.

**Dev:** `@tanstack/router-plugin` (Vite plugin), `@tanstack/router-devtools`, `@tanstack/react-query-devtools`, `openapi-typescript`, `vitest`, `@testing-library/react`, `@testing-library/jest-dom`, `jsdom`, `prettier` (config exists; package missing), `@playwright/test`.

### 6.2 App entry (`main.tsx`)

Mounts in order: `MantineProvider` (with `theme` from `theme/theme.ts`, color-scheme from Mantine's manager), `Notifications`, `QueryClientProvider`, `RouterProvider`. Reads color-scheme preference from `localStorage` (default `auto`).

### 6.3 Routes (file-based)

| File | Purpose |
|---|---|
| `routes/__root.tsx` | Auth bootstrap. `beforeLoad` calls `useMe` (or directly the API client). On 401 throws `redirect({ href: '/login' })` (real browser navigation). On success, stashes user into router context and renders `<Outlet />`. |
| `routes/_authed.tsx` | Mantine `AppShell` layout: header (logo, app name, user menu with logout), navbar (placeholder links: Home, Health), main area `<Outlet />`. |
| `routes/_authed/index.tsx` | "Welcome, {name}" placeholder. |
| `routes/_authed/health.tsx` | Calls `useAuthedHealth` and renders the result; demonstrates an authed API call. |

### 6.4 API client (`api/client.ts`)

Single `fetch` wrapper:

- `credentials: 'include'`, `Accept: 'application/json'`.
- For non-`GET`/`HEAD`: reads the antiforgery cookie (set by `GET /api/antiforgery-token` on first authed load) and adds `X-XSRF-TOKEN`.
- 2xx → parsed JSON.
- non-2xx → throws `ApiError` (typed: `{ status, body }`).

A global TanStack Query `QueryCache.onError` handler detects `error.status === 401` and triggers `window.location.assign('/login')`. This handles session-expired-mid-app-use uniformly with initial-load-unauth.

### 6.5 Codegen

- `openapi-codegen.config.ts` (or just CLI args) — input: `../SluiceBase.Api/openapi.json`, output: `src/api/schema.ts`.
- `package.json` script `gen:api`: `openapi-typescript ../SluiceBase.Api/openapi.json -o src/api/schema.ts`.
- `prebuild` script runs `gen:api` so CI catches drift.
- Frontend dev workflow: backend developer changes an endpoint → `dotnet build` regenerates `openapi.json` → frontend runs `npm run gen:api` → typechecker catches consumers needing updates. The committed `openapi.json` means a frontend-only contributor doesn't need the .NET toolchain to run the SPA against a stable schema.

### 6.6 Theme

`theme/theme.ts` exports a Mantine `createTheme({...})` with:

- Primary color: a deep teal (e.g., Mantine's built-in `teal`, shade 7 as the primary). Easily changed; this is just a starting point so the app doesn't look defaultly-Mantine-blue.
- Default fontFamily: Mantine's default + a sensible system stack fallback.
- Color scheme manager handles light/dark/auto; default is `auto`.
- Tabler icons used for all icon needs.

No Tailwind. CSS modules for any component-specific styling beyond Mantine's style props.

## 7. Test strategy

### 7.1 `tests/SluiceBase.Api.IntegrationTests/` (B1 simple → B3 upgraded)

**B1 form:** `WebApplicationFactory<Program>` boot, in-process, no real Postgres. Covers everything that doesn't require a real database.

**B3 upgrade:** Migrate to `Aspire.Hosting.Testing`. An `AppHostFixture : IAsyncLifetime` builds a `DistributedApplicationTestingBuilder` from `Projects.AppHost` so tests reuse the same Aspire wiring as `aspire run`. `app.CreateHttpClient("api")` provides a client. Test classes share the fixture via `IClassFixture<AppHostFixture>` (booting the full Aspire app per test is too slow).

**Test inventory:**

| Test | Lands in | Asserts |
|---|---|---|
| `Health_Anonymous_ReturnsOk` | B1 | `GET /api/health` returns 200 anon |
| `Health_Authed_Anonymous_Returns401` | B1 | `GET /api/health/authed` returns 401 (not 302) without cookie |
| `Me_Anonymous_Returns401` | B1 | `GET /api/me` returns 401 |
| `Login_Redirects_ToKeycloak` | B1 | `GET /login` returns 302 with `Location` containing the Keycloak authority |
| `OpenApi_Document_IsServed` | B1 | `GET /openapi/v1.json` returns 200 with parseable OpenAPI |
| `TargetEngine_Postgres_TestConnection_Succeeds` | B3 | `ITargetEngine.TestConnectionAsync` against `target-blue-pg`'s Aspire-provided connection string returns `Ok == true` |
| `TargetEngine_Postgres_TestConnection_Fails_OnBadConnString` | B3 | Same with a deliberately broken string returns `Ok == false` and a non-null `Error` |

The full OIDC round-trip (browser-mediated) is left to Playwright. Recreating the Keycloak login UI in xUnit would be fragile and not worth the maintenance.

### 7.2 Frontend Vitest (B2)

`vitest.config.ts` with `jsdom` and `@testing-library/jest-dom` matchers.

One spec: `src/api/__tests__/client.test.ts`:

- The fetch wrapper sets `credentials: 'include'`.
- 2xx → parsed JSON.
- 4xx/5xx → `ApiError` with status + body.
- Mutations (POST/PUT/PATCH/DELETE) read the antiforgery cookie and send `X-XSRF-TOKEN`.

`fetch` is mocked directly; no MSW needed for this surface.

### 7.3 Playwright E2E (B3)

`playwright.config.ts`:

- One project: `chromium`.
- `webServer: undefined` — E2E expects `aspire run` already active. Auto-starting Aspire from Playwright is fragile (port collisions, slow boot, surprising side effects).

`e2e/login.spec.ts` (happy path):

1. `await page.goto('http://localhost:5173')`.
2. Expect URL to land on Keycloak (`/realms/sluicebase/protocol/openid-connect/auth`).
3. Fill `alice` / `dev`; submit.
4. Expect to land back on `http://localhost:5173/`.
5. Expect Mantine shell to render with Alice's name visible.
6. Open user menu → click "Log out".
7. Expect logged-out state (a subsequent `goto('/')` should land back on the Keycloak login URL).

### 7.4 Out of test scope for Foundations

- Component-level Mantine rendering tests. Will land naturally as features land in later sub-projects.
- Permission/role gating tests. No permission model exists yet.
- Vitest tests of TanStack Router `beforeLoad` redirect logic. Covered end-to-end by Playwright; mocking the router context just to assert "redirects on 401" tests implementation details.

### 7.5 CI

Foundations does not ship CI configuration. All test scripts (`dotnet test`, `npm run test`, `npm run test:e2e`) are documented in the README. A future "wire CI" sub-slice will pick them up after Sub-project 1 (Permissions) lands and there's stable behavior to lock down.

## 8. Aspire AppHost edits

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var metadata = builder.AddPostgres("metadata-pg")
    .WithDataVolume()
    .AddDatabase("metadata");

builder.AddPostgres("target-blue-pg")
    .WithBindMount("seed/blue", "/docker-entrypoint-initdb.d")
    .WithDataVolume()
    .AddDatabase("blue-appdb", "appdb");

builder.AddPostgres("target-green-pg")
    .WithBindMount("seed/green", "/docker-entrypoint-initdb.d")
    .WithDataVolume()
    .AddDatabase("green-appdb", "appdb");

var keycloak = builder.AddKeycloak("keycloak")
    .WithRealmImport("seed/keycloak");

var api = builder.AddProject<Projects.SluiceBase_Api>("api")
    .WithReference(metadata, "Metadata").WaitFor(metadata)
    .WaitFor(keycloak)
    .WithEnvironment("Oidc__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("https")}/realms/sluicebase"))
    .WithEnvironment("Oidc__ClientId", "sluicebase-app")
    .WithEnvironment("Oidc__ClientSecret", "dev-secret");

var web = builder.AddViteApp("web", "../frontend")
    .WithNpm(install: true)
    .WithReference(api)
    .WithEnvironment("VITE_API_URL",
        ReferenceExpression.Create($"{api.GetEndpoint("https")}"))
    .WithEndpoint("http", e => { e.Port = 5173; });

api.WithEnvironment("Frontend__BaseUrl",
    ReferenceExpression.Create($"{web.GetEndpoint("http")}"));

builder.Build().Run();
```

**Diff vs current `AppHost.cs`:**

- Add `.WaitFor(keycloak)` on `api` so the api doesn't start before Keycloak's discovery doc is reachable.
- Add `.WithEnvironment("VITE_API_URL", ...)` on `web` so the Vite dev proxy knows where to forward backend-bound paths.
- Fix `AppHost.csproj` reference from `..\SluiceBase.Api\SluiceBase.Api.csproj` (currently broken) to the new project path.

`DevSeedHook.cs` and the `seed/blue` / `seed/green` bind-mount scripts stay on disk **but `DevSeedHook` is not registered with the AppHost**. Sub-project 5 (Query workspace) will revisit it.

## 9. Sub-slice breakdown

Three serial PRs. Each is independently shippable: the repo builds, tests pass, and `aspire run` works at the end of every slice.

### 9.1 B1 — Backend, BFF auth, EF, target-engine seam

**Branch:** `feat/foundations-b1-backend`

**Deliverables:**

- New projects: `SluiceBase.Api`, `SluiceBase.Core`, `SluiceBase.Api.IntegrationTests`. Added to `SluiceBase.slnx`. `AppHost.csproj` reference fixed.
- `Core/Targets/ITargetEngine.cs` + `ConnectivityResult`.
- `Api/Program.cs` wires ServiceDefaults → EF (`AppDbContext` with `DataProtectionKeys`) → DataProtection-to-DB → Cookie + OIDC against Keycloak with `OnRedirectToLogin`/`OnRedirectToAccessDenied` overrides → Authorization → Antiforgery → OpenAPI → endpoints.
- `Api/Endpoints/AuthEndpoints.cs`: `/login`, `/logout`, `/api/me`, `/api/antiforgery-token`.
- `Api/Endpoints/HealthEndpoints.cs`: `/api/health`, `/api/health/authed`.
- `Api/Targets/PostgresTargetEngine.cs` registered as singleton `ITargetEngine`.
- `seed/keycloak/realm.json` redirect URIs corrected for BFF.
- `Properties/launchSettings.json` pins `https://localhost:7024;http://localhost:7023`.
- `appsettings.json`: `Migrations:AutoApply: false` (explicit prod default).
- `appsettings.Development.json`: `Migrations:AutoApply: true`.
- Initial EF migration `0001_Init` committed.
- `AppHost.cs` adds `.WaitFor(keycloak)` on the api and `.WithEnvironment("VITE_API_URL", ...)` on the web resource.
- `openapi.json` checked in at `src/SluiceBase.Api/openapi.json`; build target regenerates it.
- xUnit tests: the five non-Postgres tests from §7.1.

**Acceptance:**

- `dotnet build` clean (warnings-as-errors).
- `aspire run` starts; api resource Healthy.
- `curl -i https://localhost:7024/api/me` → 401 (not 302).
- `curl -i https://localhost:7024/login` → 302 with `Location` containing Keycloak authority.
- `dotnet test` passes.
- Frontend untouched; Mantine/auth wiring lands in B2.

### 9.2 B2 — Frontend shell, auth bootstrap, codegen

**Branch:** `feat/foundations-b2-frontend`

**Deliverables:**

- `frontend/package.json` deps added (Mantine 9, TanStack Router, TanStack Query, openapi-typescript, Vitest, Playwright, Tabler icons, etc.).
- `vite.config.ts` proxies `/api`, `/login`, `/logout`, `/signin-oidc`, `/signout-callback-oidc` to the backend HTTPS endpoint (read from `VITE_API_URL`).
- File-based router scaffolded: `__root.tsx` (auth bootstrap), `_authed.tsx` (Mantine `AppShell`), `_authed/index.tsx`, `_authed/health.tsx`.
- `src/api/client.ts` (`fetch` wrapper).
- `src/api/schema.ts` generated; `prebuild` runs `gen:api`.
- `src/api/hooks.ts` with `useMe`, `useAuthedHealth`.
- `src/auth/AuthProvider.tsx`.
- `src/theme/theme.ts`.
- App.tsx scaffold replaced; old hero/counter content removed.
- Vitest config + `client.test.ts`.
- README updated: how to run, dev users, login flow.

**Acceptance:**

- `npm run build` clean (TS strict, ESLint clean).
- `aspire run` → unauth user redirected to Keycloak → log in as alice/dev → land on Mantine shell with name visible.
- Logout link returns user to logged-out state.
- `npm run test` passes.
- B1's xUnit tests still pass.

### 9.3 B3 — Aspire test infra + Playwright E2E

**Branch:** `feat/foundations-b3-tests`

**Deliverables:**

- `tests/SluiceBase.Api.IntegrationTests/` migrated from `WebApplicationFactory` to `Aspire.Hosting.Testing` `AppHostFixture`.
- `TargetEngine_Postgres_*` tests added.
- `frontend/playwright.config.ts` + `e2e/login.spec.ts`.
- `npm run test:e2e` script.
- README documents: run `aspire run` first, then `npm run test:e2e`.
- README extended: test pyramid explanation, when to write what.

**Acceptance:**

- `dotnet test` passes against Aspire-booted Postgres.
- `npm run test:e2e` (with `aspire run` active) passes login happy path.
- All B1 + B2 acceptance criteria still hold.

## 10. Risks & open questions

- **Keycloak HTTPS cert in dev.** Keycloak's HTTPS endpoint via Aspire uses a dev cert. The backend's OIDC discovery call must trust it. If issues arise, document setting `BackchannelHttpHandler` to a permissive validator in `Development` only. Tracked as a likely B1 wrinkle.
- **`SameSite=Lax` on the auth cookie + Keycloak top-level redirect.** Lax is required for the post-Keycloak redirect to keep the cookie. Verify in B1 — if browsers strip it, we'll need to re-evaluate (but Lax is the documented standard for this pattern).
- **`openapi.json` drift.** Without CI, the committed file can drift from the actual API. Mitigation: `prebuild` regenerates it locally on the frontend side; backend `dotnet build` regenerates it on the backend side. CI verification is a follow-up task.
- **Anti-forgery in B1 vs B2.** B1 has no mutating endpoints, so anti-forgery is technically unexercised until a later slice adds one. The wiring still goes in during B1 (cheap); the frontend integration lives in B2.
- **Deferred: Aspire `MigrationService` resource.** Sticking with startup auto-apply for Foundations. When the metadata schema gains real product tables (Sub-project 2), revisit whether to extract migrations to a one-shot Aspire resource for parity with prod.
- **Deferred: CI configuration.** Out of Foundations scope; pick up after Sub-project 1.

## 11. References

- Aspire AppHost (current): `src/AppHost/AppHost.cs`
- Keycloak realm: `src/AppHost/seed/keycloak/realm.json`
- Build conventions: `src/Directory.Build.props`
- Existing seed scripts (kept, not yet wired): `src/AppHost/DevSeedHook.cs`, `src/AppHost/seed/blue/01-init.sql`, `src/AppHost/seed/green/01-init.sql`
