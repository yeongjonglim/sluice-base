namespace SluiceBase.Api.Mcp;

internal static class McpIssuer
{
    public static string BaseUrl(HttpContext ctx, McpOptions options) =>
        options.GetValidatedPublicBaseUrl() ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}
