namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/oauth/token", async (HttpContext ctx, IMcpTokenService tokens, CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var grant = form["grant_type"].ToString();
            var clientId = form["client_id"].ToString();

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
