# Whitelabelling — Design

**Date:** 2026-05-11
**Status:** Approved

## 1. Purpose & scope

SluiceBase is open-source and intended to be deployed by operators under their own brand. This slice adds per-deployment whitelabelling: operators configure an app name, primary colour, logo, and favicon via environment variables without rebuilding the Docker image.

### In scope

- `Branding` config section in `appsettings.json` with env-var overrides.
- `GET /api/branding` — public endpoint returning `{ appName, primaryColor, hasLogo, hasFavicon }`.
- `GET /api/branding/logo` — public endpoint serving or proxying the configured logo.
- `GET /api/branding/favicon` — public endpoint serving or proxying the configured favicon.
- Frontend: fetches branding before mount, applies `primaryColor` to Mantine theme, `appName` to header and document title, logo/favicon to appropriate elements.

### Out of scope

- Arbitrary hex colour support (requires generating a full 10-shade Mantine palette).
- Multi-tenant branding (one brand per deployment only).
- Logo upload UI (operators provide a URL or mount a file).

### Success criteria

1. An operator sets `Branding__AppName=Acme`, `Branding__PrimaryColor=violet`, `Branding__LogoUrl=/config/logo.png`, `Branding__FaviconUrl=/config/favicon.png` via env vars.
2. The running app shows "Acme" in the header, violet as the primary colour, the logo image in the header, and the favicon in the browser tab — with no rebuild.
3. If no branding is configured, the app behaves identically to today (name "SluiceBase", teal, no logo/favicon).
4. If `GET /api/branding` fails at startup, the app still loads with defaults.
5. If a logo or favicon URL is unreachable or the file is missing, the app degrades gracefully (text title, default static favicon).

## 2. Backend

### 2.1 Config

New section added to `appsettings.json`:

```json
"Branding": {
  "AppName": "SluiceBase",
  "PrimaryColor": "teal",
  "LogoUrl": "",
  "FaviconUrl": ""
}
```

Operators override via standard .NET env var convention:

```
Branding__AppName=Acme
Branding__PrimaryColor=violet
Branding__LogoUrl=https://cdn.example.com/logo.png   # remote URLs only
Branding__FaviconUrl=https://cdn.example.com/fav.ico # remote URLs only
```

`LogoUrl` and `FaviconUrl` accept **remote URLs only** (`http://` or `https://`). Local asset files are not configured here — they are mounted at hardcoded paths (see §2.3). If a non-remote value is provided, the backend logs a warning and ignores it.

### 2.2 BrandingOptions

A record in `SluiceBase.Core` bound to the `Branding` section:

```csharp
public record BrandingOptions
{
    public string AppName { get; init; } = "SluiceBase";
    public string PrimaryColor { get; init; } = "teal";
    public string LogoUrl { get; init; } = "";
    public string FaviconUrl { get; init; } = "";
}
```

`PrimaryColor` is validated at startup against the set of Mantine built-in colour names. Invalid values log a warning and fall back to `"teal"` — no startup failure.

`LogoUrl`/`FaviconUrl` are validated to be remote URLs. Non-remote (or empty) values are treated as unconfigured; the backend then checks for a locally mounted file instead (see §2.3).

Valid Mantine colour names: `dark`, `gray`, `red`, `pink`, `grape`, `violet`, `indigo`, `blue`, `cyan`, `green`, `lime`, `yellow`, `orange`, `teal`.

### 2.3 Endpoints

All three endpoints are public (no authentication required) so branding can be fetched before or during the login flow.

**`GET /api/branding`**

Returns:

```json
{
  "appName": "Acme",
  "primaryColor": "violet",
  "logoUrl": "https://cdn.example.com/logo.png",
  "faviconUrl": "/api/branding/favicon"
}
```

The backend resolves each asset URL using this priority:

1. If `LogoUrl`/`FaviconUrl` config is a valid remote URL → return it as-is (frontend loads directly).
2. Else if a locally mounted file exists at the hardcoded path → return the serving endpoint URL (`/api/branding/logo` or `/api/branding/favicon`).
3. Else → `null`.

No operator-supplied path string is ever passed to the filesystem.

**`GET /api/branding/logo`** and **`GET /api/branding/favicon`**

These endpoints only serve **locally mounted files** at hardcoded paths. Remote URLs are handled directly by the frontend and never proxied.

The backend tries the following extensions in order for `/branding/logo` and `/branding/favicon`:

`.png`, `.svg`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.ico`

It serves the first file found with the matching content type. If no file is found → 404.

Operators mount files via Docker volume:

```
docker run -v /host/logo.svg:/branding/logo.svg ...
```

No path is accepted from config or request parameters, so path traversal is impossible.

## 3. Frontend

### 3.1 Startup fetch

In `main.tsx`, before `createRoot`, fetch branding:

```ts
const res = await fetch('/api/branding').catch(() => null);
const branding = res?.ok ? await res.json() : null;
```

If the fetch fails or returns non-200, `branding` is `null` and all defaults apply.

### 3.2 Dynamic theme

The Mantine theme is built using the fetched `primaryColor`:

```ts
const appTheme = createTheme({
  ...baseThemeConfig,
  primaryColor: branding?.primaryColor ?? "teal",
});
```

The existing `theme.ts` is refactored to export a `createAppTheme(primaryColor: string)` factory rather than a static `theme` object.

### 3.3 BrandingContext

A `BrandingContext` (in `src/theme/BrandingContext.tsx`) provides `appName`, `logoUrl`, and `faviconUrl` to the component tree. It is populated from the pre-mount fetch result and never changes at runtime.

### 3.4 Header (\_authed.tsx)

The hardcoded `<Title order={4}>SluiceBase</Title>` is replaced:

- If `logoUrl` is non-null: render `<img src={logoUrl} alt={appName} style={{ maxHeight: 24 }} onError={...} />`. On image error, fall back to the text title.
- Otherwise: render `<Title order={4}>{appName}</Title>`.

No special-casing of remote vs local URLs is needed in the header — the backend has already resolved the correct URL.

### 3.5 Document title and favicon

Applied once in `main.tsx` before mount (not in a component, to avoid hydration timing issues):

```ts
document.title = branding?.appName ?? "SluiceBase";

if (branding?.faviconUrl) {
  const link = document.querySelector<HTMLLinkElement>("link[rel~='icon']")
    ?? Object.assign(document.createElement("link"), { rel: "icon" });
  link.href = branding.faviconUrl; // remote URL or /api/branding/favicon — resolved by backend
  document.head.appendChild(link);
}
```

## 4. Error handling

| Failure | Behaviour |
|---|---|
| `GET /api/branding` network error or non-200 | App mounts with defaults: `appName="SluiceBase"`, `primaryColor="teal"`, `logoUrl=null`, `faviconUrl=null` |
| `LogoUrl`/`FaviconUrl` is empty or not a remote URL | Backend logs a warning (if non-empty), checks for a local mounted file; returns `null` if neither is found |
| Remote logo/favicon URL unreachable | Backend passes URL through to frontend; `<img onError>` hides the broken image and falls back to text title; favicon stays as browser default |
| No local file at `/branding/logo.*` or `/branding/favicon.*` | `GET /api/branding/logo` or `/favicon` returns 404; same `<img onError>` / favicon fallback as above |
| `PrimaryColor` is an invalid Mantine colour name | Backend logs a warning, returns `"teal"` in the API response; operator is informed via logs |

## 5. OpenAPI / codegen

The `GET /api/branding` response shape is added to the OpenAPI schema so the frontend gets full type safety via the existing `openapi-typescript` codegen pipeline. The logo and favicon endpoints return binary streams and are not typed in the schema.
