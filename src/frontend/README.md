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
g'"
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

## End-to-end testing

Playwright E2E lives in `e2e/` and assumes `aspire run` is already active. From this directory:

```bash
npm run test:e2e        # headless run
npm run test:e2e:ui     # interactive Playwright UI
```

See [docs/TESTING.md](../../docs/TESTING.md) for the full test pyramid and conventions.
