using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
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
        // Pass through any request with a matched route (API endpoints, OIDC callbacks, health, etc.).
        // Routing runs before this middleware, so GetEndpoint() is non-null for any registered route.
        if (context.GetEndpoint() is not null)
        {
            await next(context);
            return;
        }

        var ct = context.RequestAborted;

        if (env.IsDevelopment())
        {
            // In dev, proxy ALL unmatched requests to Vite regardless of method so that
            // dev-tool endpoints (e.g. POST /__tsd/console-pipe) work alongside SSE and assets.
            await ProxyToViteAsync(context, ct);
            return;
        }

        // In prod, only serve the SPA fallback for GET requests.
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        var indexPath = Path.Combine(env.WebRootPath, "index.html");
        var html = await File.ReadAllTextAsync(indexPath, ct);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(InjectBranding(html), ct);
    }

    // In dev, proxy every unmatched request to Vite.
    // HTML responses get branding injected; everything else (JS, CSS, SSE, …) is streamed
    // through without buffering so long-lived connections like SSE work correctly.
    // HMR WebSocket bypasses this proxy — Vite's hmr.clientPort config makes the browser
    // connect directly to Vite's port for WebSocket upgrades.
    private async Task ProxyToViteAsync(HttpContext context, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("vite");
        var path = context.Request.Path + context.Request.QueryString;

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);

        // Forward request body for methods that carry one (e.g. POST).
        if (context.Request.ContentLength is > 0)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is { Length: > 0 } reqContentType)
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", reqContentType);
            }
        }

        // ResponseHeadersRead streams the body instead of buffering it to completion first.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        context.Response.StatusCode = (int)response.StatusCode;

        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var html = await response.Content.ReadAsStringAsync(ct);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(InjectBranding(html), ct);
            return;
        }

        context.Response.ContentType = contentType;
        // Disable ASP.NET Core's response buffering so SSE and other streaming responses
        // are forwarded to the browser incrementally rather than held in memory.
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await body.CopyToAsync(context.Response.Body, ct);
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

        // Only replace the favicon link when an explicit URL is configured.
        // Leaving it untouched preserves whatever the HTML has (e.g. /vite.svg),
        // avoiding browser fallback requests to /favicon.ico that produce 404s.
        if (faviconUrl is not null)
        {
            html = FaviconRegex().Replace(html, $"""<link rel="icon" href="{faviconUrl}" />""");
        }

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

    [GeneratedRegex("<title>[^<]*</title>")]
    private static partial Regex TitleRegex();

    [GeneratedRegex("""<link[^>]+rel="icon"[^>]*/?>""")]
    private static partial Regex FaviconRegex();
}