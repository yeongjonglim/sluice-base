# Backend Branding Injection

**Date:** 2026-05-18

## Problem

Branding values (app name, primary colour, logo, favicon) are currently fetched from `/api/branding` client-side in `main.tsx` after the browser loads the page. This causes visible flickering — the tab title, favicon, Mantine theme colour, and logo all render with their default values before the API response arrives and updates them.

## Goal

Inject branding into the HTML response server-side so all four elements are correct on the very first render, with zero flicker. Behaviour must be consistent between local development (Vite HMR) and the deployed Docker image.

---

## Architecture

A single `BrandingHtmlMiddleware` in ASP.NET Core handles `index.html` injection in both environments. The source of the HTML differs:

- **Dev:** middleware fetches `http://localhost:5173/` (Vite's dev server), injects branding into the response, returns it for all non-API routes
- **Prod:** middleware reads `wwwroot/index.html` from disk, injects branding, returns it for all non-API routes

`MapFallbackToFile("index.html")` is replaced by this middleware. Static assets (`/assets/*.js`, `/branding/logo.png`, etc.) continue to be served by `UseStaticFiles` in both environments — the middleware only intercepts HTML page requests.

Vite's `base` is set to `http://localhost:5173/` when `command === 'serve'`. This makes all asset URLs in Vite's HTML output absolute, so the browser fetches JS, CSS, and HMR directly from Vite at port 5173 even though the HTML was served from the backend at port 5001. HMR connects natively to 5173 — no WebSocket proxying required.

In local dev, users access the app at `http://localhost:5001` (the backend port). Vite runs on 5173 as a build and HMR server only.

---

## What Gets Injected

The middleware rewrites three things in `<head>` before responding:

1. **`<title>`** — the existing tag's content is replaced with the configured `AppName`
2. **`<link rel="icon">`** — replaced with the resolved favicon URL, or removed if none is configured
3. **Inline script** — inserted before `</head>`:
   ```html
   <script>window.__BRANDING__ = {"appName":"...","primaryColor":"...","logoUrl":"...","faviconUrl":"..."};</script>
   ```

The frontend reads `window.__BRANDING__` synchronously in `main.tsx` on startup. `createAppTheme` and `BrandingContext` are populated with the correct values before the first React render — no async gap, no flicker for any element.

---

## Local Asset Serving

Operators place logo and favicon files in `wwwroot/branding/`. `UseStaticFiles` serves them natively at `/branding/logo.{ext}` and `/branding/favicon.{ext}` — no custom endpoint required.

The middleware resolves asset URLs as follows:

- `BrandingOptions.LogoUrl` / `BrandingOptions.FaviconUrl` is non-empty → inject as-is (relative path such as `/branding/logo.png` or remote `https://` URL)
- Empty (default) → inject `null`

Operators place files in `wwwroot/branding/` and must explicitly set `LogoUrl`/`FaviconUrl` to the corresponding path (e.g. `Branding__LogoUrl=/branding/logo.png`). In the Docker deployment, operators volume-mount their files into `/app/wwwroot/branding/`.

---

## Changes

### Backend — new
- `BrandingHtmlMiddleware` — intercepts all non-API `GET` requests; resolves branding from config + file system; injects title, favicon link, and `window.__BRANDING__` script into the HTML; in dev fetches HTML from Vite, in prod reads from `wwwroot/index.html`

### Backend — changed
- `Program.cs`
  - Wire up `UseStaticFiles` in both dev and prod (so `wwwroot/branding/` is always served)
  - Replace the `!IsDevelopment()` guard block (which contained `UseDefaultFiles`, `UseStaticFiles`, `MapFallbackToFile`) with `BrandingHtmlMiddleware` registered unconditionally
- `BrandingOptions.cs` — remove asset URL resolution logic (moves into middleware)

### Backend — deleted
- `BrandingEndpoints.cs` — removed entirely (`/api/branding`, `/api/branding/logo`, `/api/branding/favicon`)
- `EndpointMapper.cs` — remove `BrandingEndpoints.Map(app)` call

### Frontend — changed
- `main.tsx` — read `window.__BRANDING__` synchronously instead of `fetch('/api/branding')`; downstream logic (set `document.title`, inject favicon `<link>`, call `createAppTheme`, populate `BrandingContext`) is unchanged

### Frontend — unchanged
- `BrandingContext.tsx`, `theme.ts`, `_authed.tsx` — no changes needed

### Vite — changed
- `vite.config.ts`
  - Set `base: 'http://localhost:5173/'` when `command === 'serve'`
  - Remove the `/api` proxy entry (API calls resolve to the backend natively since the document origin is now port 5001)

### Docker / ops
- Mount operator branding files into `/app/wwwroot/branding/` (was `/branding/`)

### README — changed
- Update "Using local branding files" section:
  - Mount path changes from `./branding:/branding:ro` → `./branding:/app/wwwroot/branding:ro`
  - Remove references to `/api/branding/logo` and `/api/branding/favicon`
  - Clarify files are served at `/branding/logo.{ext}` directly as static assets
  - `LogoUrl`/`FaviconUrl` env vars remain, documented as for remote `http(s)://` URLs only

### Integration tests — changed
- `BrandingEndpointTests.cs` — deleted (all four tests target endpoints being removed)
- `BrandingHtmlTests.cs` — new file, same `SluiceBaseStackFactory`, tests against the live Aspire stack:
  - `GET /` returns HTML with `<title>TestCompany</title>` (dev config value)
  - `GET /` response body contains `window.__BRANDING__` with `appName: "TestCompany"` and `primaryColor: "indigo"`
  - `window.__BRANDING__.logoUrl` and `faviconUrl` are `null` when no branding files are configured
  - `GET /some/deep/route` returns the same injected HTML (SPA fallback)
  - `GET /api/health` is not intercepted — returns the health response, not HTML

---

## What Does Not Change

- `BrandingOptions` validation (primary colour whitelist, section name)
- `BrandingContext`, `useBranding()` hook, `theme.ts` — no API surface changes
- All other API endpoints
- Aspire AppHost configuration
- Authentication / OIDC flow
