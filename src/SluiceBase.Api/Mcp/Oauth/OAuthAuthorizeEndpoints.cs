using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Mcp.Clients;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthAuthorizeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/oauth/authorize", async (
            HttpContext ctx,
            IMcpClientResolver resolver,
            IAntiforgery antiforgery,
            IMcpTokenService tokens,
            IOptions<McpOptions> mcpOptions,
            IOptions<BrandingOptions> branding,
            CancellationToken ct) =>
        {
            var q = ctx.Request.Query;
            var request = AuthorizeRequest.From(key => q[key]);
            if (!HasValidShape(request))
            {
                return Results.BadRequest("invalid_request");
            }

            // Sign-in comes before resolving so an anonymous caller can't make us fetch arbitrary CIMD URLs.
            if (!(ctx.User?.TryGetInternalUserId(out var userId) ?? false))
            {
                // Not logged in yet: bounce through the existing OIDC provider, then return here.
                return ChallengeBackTo(request);
            }

            var (client, error) = await ResolveAsync(request, resolver, ct);
            if (error is not null)
            {
                return error;
            }

            if (RequiresConsent(client!, request.RedirectUri))
            {
                // A web or private-scheme callback can deliver the code off this machine, so the user
                // must see where it is going and approve it (the confused-deputy guidance in the MCP spec).
                var antiforgeryTokens = antiforgery.GetAndStoreTokens(ctx);
                return McpConsentPage.Render(
                    ctx, request, client!, ctx.User.GetEmail() ?? ctx.User.GetName(), antiforgeryTokens, branding.Value);
            }

            var code = await tokens.IssueAuthCodeAsync(request.ClientId, userId, request.RedirectUri, request.CodeChallenge, ct);
            return RedirectToClient(ctx, mcpOptions.Value, request, ("code", code), ("state", request.State));
        }).AllowAnonymous();

        app.MapPost("/mcp/oauth/authorize", async (
            HttpContext ctx,
            IMcpClientResolver resolver,
            IAntiforgery antiforgery,
            IMcpTokenService tokens,
            IOptions<McpOptions> mcpOptions,
            CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var request = AuthorizeRequest.From(key => form[key]);
            if (!HasValidShape(request))
            {
                return Results.BadRequest("invalid_request");
            }

            if (!(ctx.User?.TryGetInternalUserId(out var userId) ?? false))
            {
                return ChallengeBackTo(request);
            }

            // Before resolving, so a forged cross-site POST can't make us fetch arbitrary CIMD URLs.
            if (!await antiforgery.IsRequestValidAsync(ctx))
            {
                return Results.BadRequest("invalid_csrf_token");
            }

            var (_, error) = await ResolveAsync(request, resolver, ct);
            if (error is not null)
            {
                return error;
            }

            if (form["decision"] != "allow")
            {
                return RedirectToClient(ctx, mcpOptions.Value, request, ("error", "access_denied"), ("state", request.State));
            }

            var code = await tokens.IssueAuthCodeAsync(request.ClientId, userId, request.RedirectUri, request.CodeChallenge, ct);
            return RedirectToClient(ctx, mcpOptions.Value, request, ("code", code), ("state", request.State));
        }).AllowAnonymous();
    }

    // A loopback callback can only reach the user's own machine and a configured client is
    // operator-vetted, so only a CIMD client with a web or private-scheme callback needs consent.
    public static bool RequiresConsent(ResolvedClient client, string redirectUri) =>
        client.Kind == ClientKind.Cimd && !McpRedirectUriPolicy.IsLoopback(redirectUri);

    private static bool HasValidShape(AuthorizeRequest request) =>
        request.ResponseType == "code" && request.CodeChallengeMethod == "S256"
        && !string.IsNullOrEmpty(request.CodeChallenge);

    private static async Task<(ResolvedClient? Client, IResult? Error)> ResolveAsync(
        AuthorizeRequest request, IMcpClientResolver resolver, CancellationToken ct)
    {
        switch (await resolver.ResolveAsync(request.ClientId, ct))
        {
            case ClientResolution.Found { Client: var client }
                when client.RedirectUris.Any(r => McpRedirectUriPolicy.Matches(r, request.RedirectUri)):
                return (client, null);
            case ClientResolution.TemporarilyUnavailable:
                return (null, Results.BadRequest("temporarily_unavailable"));
            default:
                return (null, Results.BadRequest("invalid_client_or_redirect_uri"));
        }
    }

    private static IResult ChallengeBackTo(AuthorizeRequest request) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/mcp/oauth/authorize" + QueryString.Create(
                request.Fields().Select(f => new KeyValuePair<string, string?>(f.Key, f.Value))) },
            [OpenIdConnectDefaults.AuthenticationScheme]);

    private static IResult RedirectToClient(
        HttpContext ctx, McpOptions options, AuthorizeRequest request, params (string Key, string Value)[] parameters)
    {
        var sep = request.RedirectUri.Contains('?') ? '&' : '?';
        var query = string.Join('&', parameters
            .Append((Key: "iss", Value: McpIssuer.BaseUrl(ctx, options)))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return Results.Redirect($"{request.RedirectUri}{sep}{query}");
    }
}

internal sealed record AuthorizeRequest(
    string ClientId,
    string RedirectUri,
    string ResponseType,
    string CodeChallenge,
    string CodeChallengeMethod,
    string State)
{
    public static AuthorizeRequest From(Func<string, StringValues> get) => new(
        get("client_id").ToString(),
        get("redirect_uri").ToString(),
        get("response_type").ToString(),
        get("code_challenge").ToString(),
        get("code_challenge_method").ToString(),
        get("state").ToString());

    public IEnumerable<KeyValuePair<string, string>> Fields() =>
    [
        new("client_id", ClientId),
        new("redirect_uri", RedirectUri),
        new("response_type", ResponseType),
        new("code_challenge", CodeChallenge),
        new("code_challenge_method", CodeChallengeMethod),
        new("state", State),
    ];
}
