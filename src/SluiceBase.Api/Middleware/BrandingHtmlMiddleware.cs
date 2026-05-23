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
        // Pass through any request with a matched route (API endpoints, OIDC callbacks, health, etc.).
        // Routing runs before this middleware, so GetEndpoint() is non-null for any registered route.
        if (context.GetEndpoint() is not null)
        {
            await next(context);
            return;
        }

        // Only serve the SPA fallback for GET requests.
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        var ct = context.RequestAborted;
        string html;

        if (env.IsDevelopment())
        {
            var client = httpClientFactory.CreateClient("vite");
            using var response = await client.GetAsync("/index.html", ct);
            html = await response.Content.ReadAsStringAsync(ct);
        }
        else
        {
            var indexPath = Path.Combine(env.WebRootPath, "index.html");
            html = await File.ReadAllTextAsync(indexPath, ct);
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(InjectBranding(html), ct);
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
