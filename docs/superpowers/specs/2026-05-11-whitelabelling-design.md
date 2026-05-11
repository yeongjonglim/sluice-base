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
Branding__LogoUrl=/config/logo.png
Branding__FaviconUrl=/config/favicon.png
```

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

Valid Mantine colour names: `dark`, `gray`, `red`, `pink`, `grape`, `violet`, `indigo`, `blue`, `cyan`, `green`, `lime`, `yellow`, `orange`, `teal`.

### 2.3 Endpoints

All three endpoints are public (no authentication required) so branding can be fetched before or during the login flow.

**`GET /api/branding`**

Returns:

```json
{
  "appName": "Acme",
  "primaryColor": "violet",
  "hasLogo": true,
  "hasFavicon": true
}
```

`hasLogo` is `true` when `LogoUrl` is non-empty. `hasFavicon` is `true` when `FaviconUrl` is non-empty. The raw URLs are not exposed.

**`GET /api/branding/logo`** and **`GET /api/branding/favicon`**

Both use the same serving logic:

1. If the configured URL is empty → 404.
2. If the URL starts with `http://` or `https://` → proxy via `HttpClient`, stream the response body back with the upstream `Content-Type`.
3. Otherwise → treat as a local file path, read and stream the file. Content type is inferred from the file extension, defaulting to `image/png`. Missing file → 404.

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

A `BrandingContext` (in `src/theme/BrandingContext.tsx`) provides `appName`, `hasLogo`, and `hasFavicon` to the component tree. It is populated from the pre-mount fetch result and never changes at runtime.

### 3.4 Header (\_authed.tsx)

The hardcoded `<Title order={4}>SluiceBase</Title>` is replaced:

- If `hasLogo`: render `<img src="/api/branding/logo" alt={appName} style={{ maxHeight: 24 }} onError={...} />`. On image error, fall back to the text title.
- Otherwise: render `<Title order={4}>{appName}</Title>`.

### 3.5 Document title and favicon

Applied once in `main.tsx` before mount (not in a component, to avoid hydration timing issues):

```ts
document.title = branding?.appName ?? "SluiceBase";

if (branding?.hasFavicon) {
  const link = document.querySelector<HTMLLinkElement>("link[rel~='icon']")
    ?? Object.assign(document.createElement("link"), { rel: "icon" });
  link.href = "/api/branding/favicon";
  document.head.appendChild(link);
}
```

## 4. Error handling

| Failure | Behaviour |
|---|---|
| `GET /api/branding` network error or non-200 | App mounts with defaults: `appName="SluiceBase"`, `primaryColor="teal"`, no logo, no favicon |
| Logo/favicon URL is empty | Backend returns 404; frontend never requests the endpoint (`hasLogo`/`hasFavicon` is false) |
| Logo/favicon file not found or remote URL unreachable | Backend returns 404; `<img onError>` hides the broken image and renders text title instead; favicon stays as default static file |
| `PrimaryColor` is an invalid Mantine colour name | Backend logs a warning, returns `"teal"` in the API response; operator is informed via logs |

## 5. OpenAPI / codegen

The `GET /api/branding` response shape is added to the OpenAPI schema so the frontend gets full type safety via the existing `openapi-typescript` codegen pipeline. The logo and favicon endpoints return binary streams and are not typed in the schema.
