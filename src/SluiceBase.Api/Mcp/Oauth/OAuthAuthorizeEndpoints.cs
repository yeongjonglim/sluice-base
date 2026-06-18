using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthAuthorizeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/oauth/authorize", async (
            HttpContext ctx, AppDbContext db, IMcpTokenService tokens, CancellationToken ct) =>
        {
            var q = ctx.Request.Query;
            var clientId = q["client_id"].ToString();
            var redirectUri = q["redirect_uri"].ToString();
            var codeChallenge = q["code_challenge"].ToString();
            var state = q["state"].ToString();

            if (q["response_type"] != "code" || q["code_challenge_method"] != "S256"
                || string.IsNullOrEmpty(codeChallenge))
            {
                return Results.BadRequest("invalid_request");
            }

            var client = await db.McpOAuthClients.AsNoTracking()
                .SingleOrDefaultAsync(c => c.ClientId == clientId, ct);
            if (client is null || !client.RedirectUris.Contains(redirectUri))
            {
                return Results.BadRequest("invalid_client_or_redirect_uri");
            }

            if (!(ctx.User?.TryGetInternalUserId(out var userId) ?? false))
            {
                // Not logged in yet: bounce through the existing OIDC provider, then return here.
                var returnUrl = ctx.Request.Path + ctx.Request.QueryString;
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl },
                    [OpenIdConnectDefaults.AuthenticationScheme]);
            }

            var code = await tokens.IssueAuthCodeAsync(clientId, userId, redirectUri, codeChallenge, ct);
            var sep = redirectUri.Contains('?') ? '&' : '?';
            return Results.Redirect($"{redirectUri}{sep}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
        }).AllowAnonymous();
    }
}
