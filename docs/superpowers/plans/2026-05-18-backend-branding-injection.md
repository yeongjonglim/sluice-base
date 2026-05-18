# Backend Branding Injection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move branding injection (title, favicon, primary colour, logo) server-side so all four elements are correct on the first browser render with zero flicker, consistently across local dev (Vite HMR) and the deployed Docker image.

**Architecture:** A single `BrandingHtmlMiddleware` intercepts all non-API GET requests. In dev it fetches `index.html` from the Vite dev server, injects branding, and returns it; in prod it reads `wwwroot/index.html` from disk. Vite's `base` is set to its own public URL in dev mode so all asset URLs in the HTML are absolute, letting the browser load JS/CSS/HMR directly from Vite while the document is served from the backend. The frontend reads `window.__BRANDING__` synchronously instead of fetching `/api/branding`.

**Tech Stack:** .NET 10 / ASP.NET Core middleware, `IHttpClientFactory`, `System.Text.Json`, `System.Text.RegularExpressions`; React/TypeScript; Vite `defineConfig` function form; Aspire resource model (AppHost).

---

## File Map

| Action | Path |
|--------|------|
| Create | `src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs` |
| Create | `tests/IntegrationTests/BrandingHtmlTests.cs` |
| Modify | `src/SluiceBase.Api/Program.cs` |
| Modify | `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` |
| Modify | `src/AppHost/Program.cs` |
| Modify | `src/frontend/vite.config.ts` |
| Modify | `src/frontend/src/main.tsx` |
| Modify | `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs` |
| Modify | `README.md` |
| Delete | `src/SluiceBase.Api/Endpoints/BrandingEndpoints.cs` |
| Delete | `tests/IntegrationTests/BrandingEndpointTests.cs` |

---

### Task 1: Delete old branding endpoints and tests

**Files:**
- Delete: `src/SluiceBase.Api/Endpoints/BrandingEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`
- Delete: `tests/IntegrationTests/BrandingEndpointTests.cs`

- [ ] **Step 1: Delete `BrandingEndpoints.cs`**

```bash
rm src/SluiceBase.Api/Endpoints/BrandingEndpoints.cs
```

- [ ] **Step 2: Remove the `BrandingEndpoints.Map(app)` call from `EndpointMapper.cs`**

In `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`, remove line 18. The file should look like:

```csharp
namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this WebApplication app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        PermissionEndpoints.Map(app);
        DatabaseRoleEndpoints.Map(app);
        CatalogEndpoints.Map(app);
        ServerEndpoints.Map(app);
        CredentialEndpoints.Map(app);
        DatabaseEndpoints.Map(app);
        SchemaEndpoints.Map(app);
        QueryEndpoints.Map(app);
        UpdateEndpoints.Map(app);

        if (app.Environment.IsDevelopment())
        {
            DevelopmentEndpoints.Map(app);
        }

        return app;
    }
}
```

- [ ] **Step 3: Delete `BrandingEndpointTests.cs`**

```bash
rm tests/IntegrationTests/BrandingEndpointTests.cs
```

- [ ] **Step 4: Verify the project builds**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/EndpointMapper.cs \
        src/SluiceBase.Api/Endpoints/BrandingEndpoints.cs \
        tests/IntegrationTests/BrandingEndpointTests.cs
git commit -m "feat: remove /api/branding endpoints"
```

---

### Task 2: Write failing integration tests

**Files:**
- Create: `tests/IntegrationTests/BrandingHtmlTests.cs`
- Modify: `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs`

- [ ] **Step 1: Add wait for `web` resource in `SluiceBaseStackFactory.cs`**

The new middleware fetches `index.html` from the Vite dev server (`web` resource). Add a wait for it after the existing `keycloak` and `api` waits.

In `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs`, change the `InitializeAsync` method:

```csharp
public async ValueTask InitializeAsync()
{
    var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
    [
        "DcpPublisher:RandomizePorts=false" // To get fixed port for login redirect
    ]);
    appHost.MakeTransient();
    appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
    {
        clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
    });
    App = await appHost.BuildAsync();
    await App.StartAsync();

    await App.ResourceNotifications.WaitForResourceHealthyAsync("keycloak");
    await App.ResourceNotifications.WaitForResourceHealthyAsync("api");
    await App.ResourceNotifications.WaitForResourceHealthyAsync("web");
}
```

- [ ] **Step 2: Create `BrandingHtmlTests.cs`**

Create `tests/IntegrationTests/BrandingHtmlTests.cs`:

```csharp
using System.Net;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

public sealed class BrandingHtmlTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Root_ReturnsHtmlWithInjectedTitle()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>TestCompany</title>", html);
    }

    [Fact]
    public async Task Root_ReturnsHtmlWithBrandingScript()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("window.__BRANDING__", html);
        Assert.Contains("\"appName\":\"TestCompany\"", html);
        Assert.Contains("\"primaryColor\":\"indigo\"", html);
    }

    [Fact]
    public async Task Root_NoBrandingFiles_LogoAndFaviconAreNull()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"logoUrl\":null", html);
        Assert.Contains("\"faviconUrl\":null", html);
    }

    [Fact]
    public async Task DeepRoute_ReturnsSameInjectedHtml()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/some/deep/route", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("window.__BRANDING__", html);
        Assert.Contains("<title>TestCompany</title>", html);
    }

    [Fact]
    public async Task ApiRoute_NotInterceptedByMiddleware()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("window.__BRANDING__", content);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/BrandingHtmlTests.cs \
        tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs
git commit -m "feat: add BrandingHtmlTests integration tests"
```

---

### Task 3: Implement `BrandingHtmlMiddleware`

**Files:**
- Create: `src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`

- [ ] **Step 1: Create the middleware**

Create `src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Middleware;

internal sealed partial class BrandingHtmlMiddleware(
    RequestDelegate next,
    IOptions<BrandingOptions> options,
    IWebHostEnvironment env,
    IHttpClientFactory httpClientFactory,
    ILogger<BrandingHtmlMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) ||
            context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var html = await GetHtmlAsync(context.RequestAborted);
        var injected = InjectBranding(html);

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(injected, context.RequestAborted);
    }

    private async Task<string> GetHtmlAsync(CancellationToken ct)
    {
        if (env.IsDevelopment())
        {
            var client = httpClientFactory.CreateClient("vite");
            var response = await client.GetAsync("/", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }

        var indexPath = Path.Combine(env.WebRootPath, "index.html");
        return await File.ReadAllTextAsync(indexPath, ct);
    }

    private string InjectBranding(string html)
    {
        var branding = options.Value;
        var logoUrl = ResolveAssetUrl(branding.LogoUrl);
        var faviconUrl = ResolveAssetUrl(branding.FaviconUrl);
        var primaryColor = branding.GetValidatedPrimaryColor(logger);

        var brandingJson = JsonSerializer.Serialize(
            new { branding.AppName, primaryColor, logoUrl, faviconUrl },
            JsonOptions);

        html = TitleRegex().Replace(
            html,
            $"<title>{WebUtility.HtmlEncode(branding.AppName)}</title>");

        var faviconTag = faviconUrl is not null
            ? $"""<link rel="icon" href="{faviconUrl}" />"""
            : "";
        html = FaviconRegex().Replace(html, faviconTag);

        html = html.Replace(
            "</head>",
            $"<script>window.__BRANDING__ = {brandingJson};</script>\n</head>",
            StringComparison.Ordinal);

        return html;
    }

    // Any non-empty configured value is used as-is — relative path (/branding/logo.png)
    // or remote URL (https://cdn.example.com/logo.png). Empty means not configured.
    private static string? ResolveAssetUrl(string configuredUrl) =>
        string.IsNullOrEmpty(configuredUrl) ? null : configuredUrl;

    [GeneratedRegex(@"<title>[^<]*</title>")]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<link[^>]+rel=""icon""[^>]*/?>")]
    private static partial Regex FaviconRegex();
}
```

- [ ] **Step 2: Verify the project builds**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs
git commit -m "feat: add BrandingHtmlMiddleware"
```

---

### Task 4: Wire up middleware in `Program.cs`

**Files:**
- Modify: `src/SluiceBase.Api/Program.cs`

- [ ] **Step 1: Replace `Program.cs` content**

Replace `src/SluiceBase.Api/Program.cs` with:

```csharp
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Extensions;
using SluiceBase.Api.Middleware;
using SluiceBase.Api.Servers;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Branding;
using SluiceBase.Core.Targets;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("Metadata",
    configureDbContextOptions: opt => { opt.UseSnakeCaseNamingConvention(); });

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();

builder.AddSluiceBaseAuth();

builder.Services.Configure<BrandingOptions>(
    builder.Configuration.GetSection(BrandingOptions.SectionName));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "XSRF-TOKEN";
});

builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

builder.Services.AddOpenApi(x =>
{
    x.MapVogenTypesInOpenApiTransformers();
    x.AddStringEnumSchemaTransformer();
});

builder.Services.AddSingleton<ITargetEngine, PostgresTargetEngine>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IServerConnectionFactory, ServerConnectionFactory>();

// Register the "vite" HttpClient used by BrandingHtmlMiddleware in dev.
// In prod the client is registered but never used.
var viteClientBuilder = builder.Services.AddHttpClient("vite");
if (builder.Environment.IsDevelopment())
{
    var viteBaseUrl = builder.Configuration["Frontend__BaseUrl"] ?? "https://localhost:5173";
    viteClientBuilder
        .ConfigureHttpClient(c => c.BaseAddress = new Uri(viteBaseUrl))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Vite uses a dev certificate that the HttpClient won't trust by default.
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
}

var app = builder.Build();

if (builder.Configuration.GetValue("Migrations:AutoApply", true)
    && builder.Configuration.GetConnectionString("Metadata") is not null)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Serve wwwroot static files in all environments so operators can place
// branding files at wwwroot/branding/ and have them served at /branding/*.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapAllEndpoints();

// Terminal handler: inject branding into index.html for all non-API GET requests.
// In dev: fetches index.html from the Vite dev server and injects branding.
// In prod: reads wwwroot/index.html from disk and injects branding.
app.UseMiddleware<BrandingHtmlMiddleware>();

app.Run();
```

- [ ] **Step 2: Verify the project builds**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Program.cs
git commit -m "feat: wire up BrandingHtmlMiddleware in Program.cs"
```

---

### Task 5: Update AppHost to pass `VITE_BASE_URL` to Vite

**Files:**
- Modify: `src/AppHost/Program.cs`

The Vite dev server needs to know its own public URL to set `base` correctly. Aspire already sets `Frontend__BaseUrl` on the API — set the same value as `VITE_BASE_URL` on the frontend so `vite.config.ts` can read it.

- [ ] **Step 1: Add `VITE_BASE_URL` to the `web` resource**

In `src/AppHost/Program.cs`, after the `web` variable declaration (currently ending at `.WithHttpsDeveloperCertificate();`), add the new env var. The `web` declaration becomes:

```csharp
var web = builder.AddViteApp("web", "../frontend")
    .WithNpm(install: true)
    .WithReference(api)
    .WithEnvironment("VITE_API_URL",
        ReferenceExpression.Create($"{api.GetEndpoint("https")}"))
    .WithEndpoint("http", e => { e.Port = 5173; e.UriScheme = "https"; })
    .WithHttpsDeveloperCertificate();

web.WithEnvironment("VITE_BASE_URL",
    ReferenceExpression.Create($"{web.GetEndpoint("http")}"));
```

- [ ] **Step 2: Verify the AppHost builds**

```bash
dotnet build src/AppHost/AppHost.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/AppHost/Program.cs
git commit -m "feat: pass VITE_BASE_URL to Vite via Aspire"
```

---

### Task 6: Update `vite.config.ts`

**Files:**
- Modify: `src/frontend/vite.config.ts`

- [ ] **Step 1: Replace `vite.config.ts` content**

Replace `src/frontend/vite.config.ts` with:

```ts
import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { devtools } from "@tanstack/devtools-vite";
import viteReact from "@vitejs/plugin-react";

import { tanstackRouter } from "@tanstack/router-plugin/vite";

const apiUrl = process.env["services__api__http__0"] ?? "http://localhost:5001";
const port = Number(process.env["PORT"] ?? 5173);

// When running via Aspire, VITE_BASE_URL is set to the frontend's own public URL
// (e.g. https://localhost:5173). The backend uses this as the base for absolute
// asset URLs so the browser fetches JS/CSS/HMR directly from Vite.
const viteBaseUrl = process.env["VITE_BASE_URL"] ?? `http://localhost:${port}`;

export default defineConfig(({ command }) => ({
  base: command === "serve" ? `${viteBaseUrl}/` : "/",
  plugins: [
    devtools(),
    tanstackRouter({
      target: "react",
      autoCodeSplitting: true,
      routeFileIgnorePattern: "__tests__",
    }),
    viteReact(),
  ],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  server: {
    port,
    // No proxy needed — the document is served from the backend port so all
    // route-relative fetches (/api, /login, /logout, etc.) resolve to the
    // backend natively. Vite's dev server includes permissive CORS headers
    // by default, so cross-origin module loading from the backend document
    // origin works without any additional configuration.
  },
}));
```

- [ ] **Step 2: Commit**

```bash
git add src/frontend/vite.config.ts
git commit -m "feat: set Vite base URL for dev mode, remove all proxies"
```

---

### Task 7: Update `main.tsx` to read `window.__BRANDING__`

**Files:**
- Modify: `src/frontend/src/main.tsx`

- [ ] **Step 1: Replace `main.tsx` with the following complete file**

```tsx
import "@mantine/core/styles.css";
import "@mantine/dates/styles.css";
import "@mantine/notifications/styles.css";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MantineProvider } from "@mantine/core";
import { ModalsProvider } from "@mantine/modals";
import { Notifications } from "@mantine/notifications";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { RouterProvider, createRouter } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import type { BrandingValue } from "@/theme/BrandingContext.tsx";
import { createAppQueryClient } from "@/api/hooks.ts";
import { createAppTheme } from "@/theme/theme.ts";
import { BrandingContext } from "@/theme/BrandingContext.tsx";
import { routeTree } from "@/routeTree.gen.ts";
// @ts-ignore — generated at build/dev time by @tanstack/router-plugin

declare global {
  interface Window {
    __BRANDING__?: {
      appName: string;
      primaryColor: string;
      logoUrl: string | null;
      faviconUrl: string | null;
    };
  }
}

const branding = window.__BRANDING__;

const brandingValue: BrandingValue = {
  appName: branding?.appName ?? "SluiceBase",
  logoUrl: branding?.logoUrl ?? null,
  faviconUrl: branding?.faviconUrl ?? null,
};

const appTheme = createAppTheme(branding?.primaryColor ?? "teal");

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
    <BrandingContext value={brandingValue}>
      <MantineProvider theme={appTheme} defaultColorScheme="auto">
        <ModalsProvider>
          <Notifications />
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
            <ReactQueryDevtools initialIsOpen={false} />
            <TanStackRouterDevtools router={router} initialIsOpen={false} />
          </QueryClientProvider>
        </ModalsProvider>
      </MantineProvider>
    </BrandingContext>
  </StrictMode>,
);
```

- [ ] **Step 2: Verify the TypeScript compiles**

```bash
cd src/frontend && npm run typecheck
```

Expected: No type errors.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/main.tsx
git commit -m "feat: read branding from window.__BRANDING__ synchronously"
```

---

### Task 8: Update README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the "Using local branding files" section**

Replace lines 99–115 of `README.md` with:

```markdown
### Using local branding files

Mount a directory into `/app/wwwroot/branding/` inside the container and set `LogoUrl`/`FaviconUrl` to the corresponding relative paths. The files are served natively as static assets.

```yaml
# docker-compose.yml
services:
  app:
    image: ghcr.io/yeongjonglim/sluice-base:latest
    volumes:
      - ./branding:/app/wwwroot/branding:ro
    environment:
      - Branding__LogoUrl=/branding/logo.png
      - Branding__FaviconUrl=/branding/favicon.ico
```

Place `logo.png` and `favicon.ico` in a local `./branding/` directory. Both `LogoUrl` and `FaviconUrl` also accept remote `http(s)://` URLs if the assets are hosted externally — in that case no volume mount is needed.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: update branding file mount path to wwwroot/branding"
```

---

### Task 9: Run integration tests and verify

- [ ] **Step 1: Run the integration test suite**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj --logger "console;verbosity=normal"
```

Expected: All tests pass, including the 5 new `BrandingHtmlTests` and all pre-existing tests.

- [ ] **Step 2: If `BrandingHtmlTests` fail with a connection error to Vite**

The `web` resource may not expose a health endpoint Aspire can poll. If `WaitForResourceHealthyAsync("web")` throws, change it in `SluiceBaseStackFactory.cs` to:

```csharp
await App.ResourceNotifications.WaitForResourceAsync(
    "web", KnownResourceStates.Running, TestContext.Current.CancellationToken);
```

Re-run tests after the change.

- [ ] **Step 3: Verify the `window.__BRANDING__` JSON matches the assertions exactly**

If the JSON serialisation uses a different casing than the test expects, check the `JsonSerializerOptions` in `BrandingHtmlMiddleware`. The options use `JsonNamingPolicy.CamelCase` so property names must be `appName`, `primaryColor`, `logoUrl`, `faviconUrl`. The anonymous type passed to `JsonSerializer.Serialize` uses explicit lowercase names, so this should be fine.
