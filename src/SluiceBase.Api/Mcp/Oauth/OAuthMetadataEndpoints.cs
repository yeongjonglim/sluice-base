using SluiceBase.Api.Extensions;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthMetadataEndpoints
{
    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] GrantTypes = [GrantType.AuthorizationCode.Value, GrantType.RefreshToken.Value];
    private static readonly string[] CodeChallengeMethods = ["S256"];
    private static readonly string[] TokenEndpointAuthMethods = ["none"];

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
        {
            var baseUrl = ctx.Request.BaseUrl();
            return Results.Ok(new Dictionary<string, object?>
            {
                ["issuer"] = baseUrl,
                ["authorization_endpoint"] = $"{baseUrl}/mcp/oauth/authorize",
                ["token_endpoint"] = $"{baseUrl}/mcp/oauth/token",
                ["registration_endpoint"] = $"{baseUrl}/mcp/oauth/register",
                ["response_types_supported"] = ResponseTypes,
                ["grant_types_supported"] = GrantTypes,
                ["code_challenge_methods_supported"] = CodeChallengeMethods,
                ["token_endpoint_auth_methods_supported"] = TokenEndpointAuthMethods,
            });
        }).AllowAnonymous();
    }
}
