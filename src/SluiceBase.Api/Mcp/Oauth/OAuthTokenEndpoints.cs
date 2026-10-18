using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/oauth/token", async (
            HttpContext ctx,
            IMcpClientResolver resolver,
            IMcpTokenService tokens,
            CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var grant = form["grant_type"].ToString();
            var clientId = form["client_id"].ToString();

            var secret = grant switch
            {
                "authorization_code" => form["code"].ToString(),
                "refresh_token" => form["refresh_token"].ToString(),
                _ => null,
            };

            // Checked before resolving so an anonymous caller without a live code or refresh token
            // can never make us fetch a CIMD document.
            if (secret is null || !await tokens.HasLiveGrantAsync(grant, clientId, secret, ct))
            {
                return InvalidGrant();
            }

            switch (await resolver.ResolveAsync(clientId, ct))
            {
                case ClientResolution.NotFound:
                    return Results.Json(new { error = "invalid_client" }, statusCode: 401);
                case ClientResolution.TemporarilyUnavailable:
                    return Results.Json(new { error = "temporarily_unavailable" }, statusCode: 503);
            }

            var issued = grant == "authorization_code"
                ? await tokens.RedeemAuthCodeAsync(
                    clientId, secret, form["redirect_uri"].ToString(), form["code_verifier"].ToString(), ct)
                : await tokens.RefreshAsync(clientId, secret, ct);

            if (issued is null)
            {
                return InvalidGrant();
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

    private static IResult InvalidGrant() => Results.Json(new { error = "invalid_grant" }, statusCode: 400);
}
