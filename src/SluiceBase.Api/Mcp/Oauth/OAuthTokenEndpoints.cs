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

            // Liveness is checked before resolving so an anonymous caller without a live code or
            // refresh token can never make us fetch a CIMD document.
            if (!TokenGrant.TryFromForm(form, out var grant) || await grant.FindLiveAsync(tokens, ct) is not { } live)
            {
                return InvalidGrant();
            }

            // Loopback sign-ins were never resolved at authorize, so they aren't here either.
            if (!live.IsLoopback)
            {
                switch (await resolver.ResolveAsync(grant.ClientId, ct))
                {
                    case ClientResolution.NotFound:
                        return Results.Json(new { error = "invalid_client" }, statusCode: 401);
                    case ClientResolution.TemporarilyUnavailable:
                        return Results.Json(new { error = "temporarily_unavailable" }, statusCode: 503);
                }
            }

            var issued = await grant.RedeemAsync(tokens, ct);
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

        // RFC 7009. Clients are public, so the client_id only has to match the token's; the response is 200 whether
        // or not the token was found, so a caller learns nothing about tokens it does not hold.
        app.MapPost("/mcp/oauth/revoke", async (HttpContext ctx, IMcpTokenService tokens, CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var token = form["token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                return Results.Json(new { error = "invalid_request" }, statusCode: 400);
            }

            await tokens.RevokeTokenAsync(form["client_id"].ToString(), token, ct);
            return Results.Ok();
        }).AllowAnonymous();
    }

    private static IResult InvalidGrant() => Results.Json(new { error = "invalid_grant" }, statusCode: 400);
}
