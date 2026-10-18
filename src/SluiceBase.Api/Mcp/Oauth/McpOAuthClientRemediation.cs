using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;

namespace SluiceBase.Api.Mcp.Oauth;

/// <summary>
/// Brings stored MCP OAuth clients in line with <see cref="McpRedirectUriPolicy"/>. Clients
/// registered before the policy (or before an operator narrowed the allowlist) may carry redirect
/// URIs that could have delivered codes to a third party, so their disallowed URIs are removed
/// and every token and pending code they hold is revoked. Client rows are kept for investigation.
/// </summary>
internal static class McpOAuthClientRemediation
{
    public static async Task RunAsync(AppDbContext db, McpOptions options, ILogger logger, CancellationToken ct = default)
    {
        var clients = await db.McpOAuthClients.ToListAsync(ct);
        foreach (var client in clients.Where(c => c.RedirectUris.Count > 0
                     && !McpRedirectUriPolicy.IsConforming(c, options.AllowedRedirectUris)))
        {
            var removed = client.RedirectUris
                .Where(u => !McpRedirectUriPolicy.IsAllowed(u, options.AllowedRedirectUris))
                .ToList();
            client.RestrictRedirectUris(u => McpRedirectUriPolicy.IsAllowed(u, options.AllowedRedirectUris));

            var revokedTokens = await db.McpTokens
                .Where(t => t.ClientId == client.ClientId && !t.Revoked)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revoked, true), ct);
            await db.McpAuthCodes
                .Where(c => c.ClientId == client.ClientId && !c.Consumed)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Consumed, true), ct);

            logger.WarningRemediatedMcpClient(client.ClientId, client.ClientName, string.Join(", ", removed), revokedTokens);
        }

        await db.SaveChangesAsync(ct);
    }
}

internal static partial class McpRemediationLoggerMessage
{
    [LoggerMessage(
        LogLevel.Warning,
        Message = "MCP OAuth client {ClientId} ('{ClientName}') had disallowed redirect URIs [{RemovedRedirectUris}]; "
            + "removed them and revoked {RevokedTokenCount} token(s). Review mcp_token for this client_id to see which users were affected.")]
    public static partial void WarningRemediatedMcpClient(
        this ILogger logger, string clientId, string clientName, string removedRedirectUris, int revokedTokenCount);
}
