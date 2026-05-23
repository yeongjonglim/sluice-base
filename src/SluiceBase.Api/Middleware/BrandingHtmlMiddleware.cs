using System.Net;
using System.Net.WebSockets;
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
            if (context.WebSockets.IsWebSocketRequest)
            {
                await ProxyWebSocketToViteAsync(context, ct);
                return;
            }

            // CONNECT is an HTTP proxy-tunnelling method that HttpClient cannot forward.
            if (HttpMethods.IsConnect(context.Request.Method))
            {
                await next(context);
                return;
            }
            // Proxy all other unmatched requests to Vite so that dev-tool endpoints
            // (e.g. POST /__tsd/console-pipe, SSE, static assets) work correctly.
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

    private async Task ProxyWebSocketToViteAsync(HttpContext context, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("vite");
        var viteBase = client.BaseAddress!;
        var wsUri = new UriBuilder(viteBase)
        {
            Scheme = "ws",
            Path = context.Request.Path,
            Query = context.Request.QueryString.ToString()
        }.Uri;

        using var remote = new ClientWebSocket();
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
        {
            remote.Options.AddSubProtocol(protocol);
        }
        await remote.ConnectAsync(wsUri, ct);
        using var local = await context.WebSockets.AcceptWebSocketAsync(remote.SubProtocol);

        var up = PumpWebSocketAsync(local, remote, ct);
        var down = PumpWebSocketAsync(remote, local, ct);
        await Task.WhenAny(up, down);
    }

    private static async Task PumpWebSocketAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var result = await from.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await to.CloseAsync(
                    result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                    result.CloseStatusDescription,
                    ct);
                return;
            }

            await to.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                ct);
        }
    }

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