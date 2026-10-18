using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthRegistrationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/oauth/register", async (
            [FromBody] RegisterClientRequest request,
            AppDbContext db,
            TimeProvider timeProvider,
            IOptions<McpOptions> options,
            CancellationToken ct) =>
        {
            // Disallowed URIs are dropped rather than failing the registration (RFC 7591 §3.2.1 lets the
            // server replace requested metadata): VS Code registers its loopback callback alongside
            // vscode.dev relay URIs, and only the loopback one is usable here.
            var redirectUris = (request.RedirectUris ?? [])
                .Where(u => McpRedirectUriPolicy.IsAllowed(u, options.Value.AllowedRedirectUris))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (redirectUris.Count == 0)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_redirect_uri",
                    error_description = "Redirect URIs must be http loopback (localhost, 127.0.0.1 or [::1]) "
                        + "or explicitly allowed by the server operator.",
                });
            }

            var client = McpOAuthClient.Register(
                request.ClientName ?? "MCP Client",
                redirectUris,
                timeProvider.GetUtcNow());

            db.McpOAuthClients.Add(client);
            await db.SaveChangesAsync(ct);

            return Results.Json(new Dictionary<string, object?>
            {
                ["client_id"] = client.ClientId,
                ["client_name"] = client.ClientName,
                ["redirect_uris"] = client.RedirectUris,
            }, statusCode: 201);
        }).AllowAnonymous();
    }

    internal sealed record RegisterClientRequest(
        [property: JsonPropertyName("client_name")] string? ClientName,
        [property: JsonPropertyName("redirect_uris")] List<string>? RedirectUris);
}
