using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
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
            CancellationToken ct) =>
        {
            if (request.RedirectUris is null or { Count: 0 }
                || request.RedirectUris.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out _)))
            {
                return Results.BadRequest(new { error = "invalid_redirect_uri" });
            }

            var client = McpOAuthClient.Register(
                request.ClientName ?? "MCP Client",
                request.RedirectUris,
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
