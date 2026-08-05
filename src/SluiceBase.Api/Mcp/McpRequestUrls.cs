namespace SluiceBase.Api.Mcp;

internal static class McpRequestUrls
{
    // The MCP server's own public base URL, derived from the incoming request exactly as the
    // OAuth metadata endpoints advertise the server (issuer / resource / authorization_server).
    // Single source of truth so any link a tool hands back (e.g. an update-request page) uses the
    // same scheme + host the client reached the MCP server on — mirroring how the frontend builds
    // the MCP endpoint from window.location.origin.
    public static string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}
