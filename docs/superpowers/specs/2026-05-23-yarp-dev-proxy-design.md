# YARP Dev Proxy

Replace the custom HTTP/WebSocket proxy in `BrandingHtmlMiddleware` with an `Aspire.Hosting.Yarp` gateway that sits in front of both the API and Vite dev server during local development.

## Goals

- HMR works natively through YARP (no custom WebSocket pump)
- Branding injection is testable in dev (page navigations go through the API)
- Single HTTPS entry point; neither API nor Vite is accessed directly
- Consistency with prod (BFF pattern — API serves static files with branding)

## Architecture

Browser → YARP gateway (https://localhost:5443) → API or Vite

### Route table

| Priority | Match | Cluster |
|----------|-------|---------|
| 0 | `/api/{**rest}` | API |
| 0 | `/login` | API |
| 0 | `/logout` | API |
| 0 | `/signin-oidc` | API |
| 0 | `/signout-callback-oidc` | API |
| 0 | `/openapi/{**rest}` | API |
| 100 | `/{**rest}` with `Accept` header containing `text/html` | API |
| max | `/{**rest}` | Vite |

The `Accept: text/html` match at priority 100 captures browser navigations (which send `Accept: text/html,...`) and routes them to the API so `BrandingHtmlMiddleware` can fetch `index.html` from Vite and inject `window.__BRANDING__`. Asset requests (JS, CSS, images) and WebSocket upgrades (HMR) do not carry this header and fall through to Vite directly.

### OIDC flow

YARP preserves the original `Host` header. The API's OIDC middleware sees `localhost:5443` when constructing callback URIs, so Keycloak redirects land through the gateway.

## Changes

### AppHost (`src/AppHost/Program.cs`)

- Add `Aspire.Hosting.Yarp` package reference
- Add `gateway` YARP resource with HTTPS on port 5443
- Configure route table: API cluster for backend routes + `Accept: text/html` SPA fallback, Vite cluster for everything else
- Keep `Frontend__BaseUrl` env var on the API (still needed for index.html fetch)

### Keycloak realm (`src/AppHost/seed/keycloak/realm.json`)

- Update `redirectUris` from `https://localhost:7024/signin-oidc` to `https://localhost:5443/signin-oidc`
- Update `post.logout.redirect.uris` from `https://localhost:7024/signout-callback-oidc` to `https://localhost:5443/signout-callback-oidc`

### BrandingHtmlMiddleware (`src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`)

Remove:
- `ProxyWebSocketToViteAsync` method
- `PumpWebSocketAsync` method
- `ProxyToViteAsync` method
- CONNECT handling branch
- `using System.Net.WebSockets`

Simplify the dev branch to:
- Fetch index.html from Vite via `HttpClient`
- Inject branding
- Return

The prod branch is unchanged: read `index.html` from disk, inject branding.

### API Program.cs (`src/SluiceBase.Api/Program.cs`)

- Remove `app.UseWebSockets()` (no longer needed)
- Keep the `"vite"` HttpClient registration (used for index.html fetch in dev)

## What this eliminates

- ~90 lines of custom WebSocket/HTTP proxy code
- `System.Net.WebSockets` dependency in the middleware
- All HMR-related workarounds
