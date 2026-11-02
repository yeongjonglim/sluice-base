namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/oauth/token", async (HttpContext ctx, IMcpTokenService tokens, CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            if (!TokenGrant.TryFromForm(form, out var grant))
            {
                return InvalidGrant();
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
    }

    private static IResult InvalidGrant() => Results.Json(new { error = "invalid_grant" }, statusCode: 400);
}
