using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Branding;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthAuthorizeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/oauth/authorize", async (
            HttpContext ctx,
            AppDbContext db,
            IAntiforgery antiforgery,
            IOptions<McpOptions> mcpOptions,
            IOptions<BrandingOptions> branding,
            CancellationToken ct) =>
        {
            var q = ctx.Request.Query;
            var request = AuthorizeRequest.From(key => q[key]);
            var (client, error) = await ValidateAsync(request, db, mcpOptions.Value, ct);
            if (error is not null)
            {
                return error;
            }

            if (!(ctx.User?.TryGetInternalUserId(out _) ?? false))
            {
                // Not logged in yet: bounce through the existing OIDC provider, then return here.
                return ChallengeBackTo(request);
            }

            // Never issue a code straight from a GET: registration is open, so the user must see
            // where the code is going and approve it (the confused-deputy guidance in the MCP spec).
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            return McpConsentPage.Render(
                ctx, request, client!.ClientName, ctx.User.GetEmail() ?? ctx.User.GetName(), tokens, branding.Value);
        }).AllowAnonymous();

        app.MapPost("/mcp/oauth/authorize", async (
            HttpContext ctx,
            AppDbContext db,
            IAntiforgery antiforgery,
            IMcpTokenService tokens,
            IOptions<McpOptions> mcpOptions,
            CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var request = AuthorizeRequest.From(key => form[key]);
            var (_, error) = await ValidateAsync(request, db, mcpOptions.Value, ct);
            if (error is not null)
            {
                return error;
            }

            if (!(ctx.User?.TryGetInternalUserId(out var userId) ?? false))
            {
                return ChallengeBackTo(request);
            }

            if (!await antiforgery.IsRequestValidAsync(ctx))
            {
                return Results.BadRequest("invalid_csrf_token");
            }

            if (form["decision"] != "allow")
            {
                return RedirectToClient(request, ("error", "access_denied"), ("state", request.State));
            }

            var code = await tokens.IssueAuthCodeAsync(request.ClientId, userId, request.RedirectUri, request.CodeChallenge, ct);
            return RedirectToClient(request, ("code", code), ("state", request.State));
        }).AllowAnonymous();
    }

    private static async Task<(McpOAuthClient? Client, IResult? Error)> ValidateAsync(
        AuthorizeRequest request, AppDbContext db, McpOptions options, CancellationToken ct)
    {
        if (request.ResponseType != "code" || request.CodeChallengeMethod != "S256"
            || string.IsNullOrEmpty(request.CodeChallenge))
        {
            return (null, Results.BadRequest("invalid_request"));
        }

        var client = await db.McpOAuthClients.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ClientId == request.ClientId, ct);
        // The policy is re-checked here, not just at registration, so a URI that is no longer
        // allowed (or a client registered before the policy existed) can't receive a code.
        if (client is null
            || !McpRedirectUriPolicy.IsAllowed(request.RedirectUri, options.AllowedRedirectUris)
            || !client.RedirectUris.Any(r => McpRedirectUriPolicy.Matches(r, request.RedirectUri)))
        {
            return (null, Results.BadRequest("invalid_client_or_redirect_uri"));
        }

        return (client, null);
    }

    private static IResult ChallengeBackTo(AuthorizeRequest request) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/mcp/oauth/authorize" + QueryString.Create(
                request.Fields().Select(f => new KeyValuePair<string, string?>(f.Key, f.Value))) },
            [OpenIdConnectDefaults.AuthenticationScheme]);

    private static IResult RedirectToClient(AuthorizeRequest request, params (string Key, string Value)[] parameters)
    {
        var sep = request.RedirectUri.Contains('?') ? '&' : '?';
        var query = string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
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
