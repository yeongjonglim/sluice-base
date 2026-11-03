using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;
using SluiceBase.Api.Auth;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class AuthEndpoints
{
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";
    public const string AntiforgeryRequestCookieName = "XSRF-TOKEN";

    // A path on this site. "//host" and "/\host" are relative URIs too, but browsers treat them as another host,
    // and they ignore tabs and newlines, so "/\t/host" is rejected as well.
    internal static bool IsLocalPath(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(c => c == '\\' || char.IsControl(c))
        && Uri.IsWellFormedUriString(url, UriKind.Relative);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/login",
                ChallengeHttpResult (string? returnUrl) =>
                {
                    var redirectUri = IsLocalPath(returnUrl) ? returnUrl! : "/";
                    return TypedResults.Challenge(
                        new AuthenticationProperties { RedirectUri = redirectUri },
                        authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme]);
                })
            .WithName("Login")
            .AllowAnonymous();

        app.MapGet("/logout",
                SignOutHttpResult () =>
                    TypedResults.SignOut(
                        new AuthenticationProperties { RedirectUri = "/" },
                        authenticationSchemes:
                        [
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            OpenIdConnectDefaults.AuthenticationScheme
                        ]))
            .WithName("Logout")
            .AllowAnonymous();

        app.MapGet("/api/me",
                async Task<Results<UnauthorizedHttpResult, Ok<MeResponse>>> (
                    ICurrentUserAccessor currentUser,
                    IAccessResolver resolver,
                    CancellationToken ct) =>
                {
                    var user = await currentUser.GetAsync(ct);
                    if (user is null)
                    {
                        return TypedResults.Unauthorized();
                    }

                    var effective = await resolver.EffectivePermissionsAsync(user.Id, ct);

                    return TypedResults.Ok(new MeResponse(
                        Id: user.Id,
                        Email: user.Email,
                        Name: user.Name,
                        Permissions: effective.ToArray()));
                })
            .WithName("Me")
            .RequireAuthorization();

        app.MapGet("/api/antiforgery-token",
                Ok<AntiforgeryTokenResponse> (HttpContext ctx, IAntiforgery antiforgery) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(ctx);
                    // Readable by the SPA, which echoes it in the header; another site can neither read it nor set
                    // the header, so a forged request fails validation.
                    ctx.Response.Cookies.Append(AntiforgeryRequestCookieName, tokens.RequestToken!, new CookieOptions
                    {
                        HttpOnly = false,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Path = "/",
                    });
                    return TypedResults.Ok(new AntiforgeryTokenResponse(tokens.HeaderName));
                })
            .WithName("AntiforgeryToken")
            .RequireAuthorization();
    }
}

internal sealed record MeResponse(
    UserId Id,
    string? Email,
    string? Name,
    string[] Permissions);

internal sealed record AntiforgeryTokenResponse(string? HeaderName);