using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Auth;

namespace SluiceBase.Api.Mcp;

internal sealed class McpBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IMcpTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        var userId = await tokens.ValidateAccessTokenAsync(token, Context.RequestAborted);
        if (userId is null)
        {
            return AuthenticateResult.Fail("Invalid token");
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(AppClaims.InternalUserIdClaim, userId.Value.ToString()));
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate =
            $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
        return Task.CompletedTask;
    }
}
