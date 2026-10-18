using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/oauth/token", async (
            HttpContext ctx,
            AppDbContext db,
            IMcpTokenService tokens,
            IOptions<McpOptions> options,
            CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var grant = form["grant_type"].ToString();
            var clientId = form["client_id"].ToString();

            var client = await db.McpOAuthClients.AsNoTracking()
                .SingleOrDefaultAsync(c => c.ClientId == clientId, ct);
            if (client is null || !McpRedirectUriPolicy.IsConforming(client, options.Value.AllowedRedirectUris))
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            }

            var issued = grant switch
            {
                "authorization_code" => await tokens.RedeemAuthCodeAsync(
                    clientId, form["code"].ToString(), form["redirect_uri"].ToString(),
                    form["code_verifier"].ToString(), ct),
                "refresh_token" => await tokens.RefreshAsync(
                    clientId, form["refresh_token"].ToString(), ct),
                _ => null,
            };

            if (issued is null)
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            return Results.Json(new Dictionary<string, object?>
            {
                ["access_token"] = issued.AccessToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = issued.ExpiresInSeconds,
                ["refresh_token"] = issued.RefreshToken,
            });
        }).AllowAnonymous();
    }
}
