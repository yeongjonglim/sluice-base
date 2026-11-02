using Microsoft.AspNetCore.Http.HttpResults;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Mcp;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Endpoints;

// The signed-in user's own MCP sign-ins, so they can see which clients hold tokens and revoke any of them.
internal static class McpGrantEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me/mcp-grant").RequireAuthorization();
        me.MapGet("", ListMyGrants).WithName("ListMyMcpGrants");
        me.MapDelete("/{grantId}", RevokeMyGrant).WithName("RevokeMyMcpGrant");
    }

    private static async Task<Results<Ok<ListMcpGrantsResponse>, UnauthorizedHttpResult>> ListMyGrants(
        ICurrentUserAccessor currentUser, IMcpTokenService tokens, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var grants = await tokens.ListGrantsAsync(user.Id, ct);
        return TypedResults.Ok(new ListMcpGrantsResponse(
            [.. grants.Select(g => new McpGrantResponse(g.Id, g.ClientId, g.IsLoopback, g.SignedInAt, g.LastUsedAt, g.ExpiresAt))]));
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RevokeMyGrant(
        McpGrantId grantId, ICurrentUserAccessor currentUser, IMcpTokenService tokens, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return await tokens.RevokeGrantAsync(user.Id, grantId, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}

internal sealed record McpGrantResponse(
    McpGrantId Id,
    string ClientId,
    bool IsLoopback,
    DateTimeOffset SignedInAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset ExpiresAt);

internal sealed record ListMcpGrantsResponse(IReadOnlyList<McpGrantResponse> Grants);
