# Design: Docker CI, wwwroot SPA serving, README

**Date:** 2026-05-11

## Overview

Add a multi-arch Docker image build + push workflow on `main`, wire the API to serve the React frontend through `wwwroot`, and add a project README with hosting instructions.

## Dockerfile (multi-stage)

Three stages, placed at the repo root.

### Stage 1 — `frontend-build` (`node:24-alpine`)

- `WORKDIR /app/src/frontend`
- Copy `package.json` + `package-lock.json` → `npm ci`
- Copy `src/SluiceBase.Api/openapi.json` to `../SluiceBase.Api/openapi.json` (required by the `prebuild` `gen:api` script)
- Copy remaining frontend sources → `npm run build`
- Output: `/app/src/frontend/dist/`

### Stage 2 — `api-build` (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`)

- `WORKDIR /src`
- Copy solution and all `*.csproj` files → `dotnet restore`
- Copy remaining source → `dotnet publish src/SluiceBase.Api/SluiceBase.Api.csproj -c Release -o /publish`

### Stage 3 — `final` (`mcr.microsoft.com/dotnet/aspnet:10.0-alpine`)

- `WORKDIR /app`
- `COPY --from=api-build /publish .`
- `COPY --from=frontend-build /app/src/frontend/dist ./wwwroot`
- `EXPOSE 8080`
- `ENTRYPOINT ["dotnet", "SluiceBase.Api.dll"]`

## API changes (`src/SluiceBase.Api/Program.cs`)

Add static file serving before the auth middleware, and a SPA fallback after all endpoint mappings:

```
app.UseDefaultFiles();    // redirects / → /index.html
app.UseStaticFiles();     // serves wwwroot assets without auth

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapAllEndpoints();
app.MapFallbackToFile("index.html");  // SPA client-side routing fallback

app.Run();
```

Static assets (`.js`, `.css`, etc.) bypass the auth pipeline. The `MapFallbackToFile` fallback is an endpoint that goes through auth middleware — but since it has no `RequireAuthorization()`, unauthenticated users receive `index.html` and the React app handles the redirect to `/login`.

## GitHub Actions workflow (`.github/workflows/docker-publish.yml`)

**Triggers:**
- `push` to branch `main` → builds and pushes with tag `latest`
- `push` of tags matching `v*.*.*` → builds and pushes with tags `v1.2.3` (exact) + `latest`

No `v1.2` or `v1` major/minor-only tags.

**Steps:**
1. Checkout
2. `docker/setup-qemu-action` (for cross-arch)
3. `docker/setup-buildx-action`
4. `docker/login-action` → `ghcr.io` using `secrets.GITHUB_TOKEN`
5. `docker/metadata-action` with custom tag rules (exact semver + latest)
6. `docker/build-push-action` → platforms `linux/amd64,linux/arm64`, push enabled

**Package:** `ghcr.io/yeongjonglim/sluice-base`

No extra secrets required — `GITHUB_TOKEN` is sufficient for GHCR.

## README (`README.md`)

Sections:
1. Project intro (what SluiceBase is, key features)
2. Architecture (text overview: React SPA → .NET API → Postgres + OIDC provider)
3. Docker quick-start (`docker run` example with required env vars)
4. Environment variable reference (connection strings, OIDC config, branding, etc.)
5. Database migrations (how to run `dotnet ef database update` or enable auto-apply)
6. OIDC provider setup (what redirect URIs to register, what scopes are needed)
7. Development setup (Aspire AppHost + Keycloak)
