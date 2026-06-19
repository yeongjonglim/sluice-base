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
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/login",
                ChallengeHttpResult (string? returnUrl) =>
                {
                    var redirectUri = !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                        ? returnUrl
                        : "/";
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