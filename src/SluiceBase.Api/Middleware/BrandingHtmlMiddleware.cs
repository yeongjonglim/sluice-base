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
