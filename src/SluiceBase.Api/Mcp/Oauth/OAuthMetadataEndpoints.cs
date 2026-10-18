using Microsoft.Extensions.Options;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthMetadataEndpoints
{
    private static readonly string[] BearerMethods = ["header"];
    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] GrantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] CodeChallengeMethods = ["S256"];
    private static readonly string[] TokenEndpointAuthMethods = ["none"];

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx, IOptions<McpOptions> mcpOptions) =>
        {
            var baseUrl = McpIssuer.BaseUrl(ctx, mcpOptions.Value);
            return Results.Ok(new Dictionary<string, object?>
            {
                ["resource"] = baseUrl,
                ["authorization_servers"] = new[] { baseUrl },
                ["bearer_methods_supported"] = BearerMethods,
            });
        }).AllowAnonymous();

        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx, IOptions<McpOptions> mcpOptions) =>
        {
            var baseUrl = McpIssuer.BaseUrl(ctx, mcpOptions.Value);
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
                ["authorization_response_iss_parameter_supported"] = true,
                ["client_id_metadata_document_supported"] = true,
            });
        }).AllowAnonymous();
    }
}
