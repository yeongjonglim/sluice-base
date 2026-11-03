namespace SluiceBase.Api.Middleware;

// Defaults for every response. A page that needs a stricter policy (the MCP consent page sets its own CSP) keeps it,
// since a header already set when the response starts is left alone.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // No script-src: index.html carries inline branding, so this policy is about framing (the approve and execute
    // buttons must not be clickjackable) and the narrow injection vectors, not a full script policy.
    private const string ContentSecurityPolicy = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            return Task.CompletedTask;
        });

        return next(context);
    }
}
