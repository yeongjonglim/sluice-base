using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace SluiceBase.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/login",
                (string? returnUrl) =>
                {
                    var redirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
                    return Results.Challenge(
                        new AuthenticationProperties { RedirectUri = redirectUri },
                        authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme]);
                })
            .WithName("Login")
            .AllowAnonymous();

        app.MapGet("/logout",
                () =>
                    Results.SignOut(
                        new AuthenticationProperties { RedirectUri = "/" },
                        authenticationSchemes:
                        [
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            OpenIdConnectDefaults.AuthenticationScheme
                        ]))
            .WithName("Logout")
            .AllowAnonymous();

        app.MapGet("/api/me",
                (ClaimsPrincipal user) =>
                {
                    var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value)
                        .Concat(user.FindAll("role").Select(c => c.Value))
                        .Distinct()
                        .ToArray();

                    return Results.Ok(new
                    {
                        sub = user.FindFirstValue("sub"),
                        email = user.FindFirstValue("email"),
                        name = user.FindFirstValue("name"),
                        preferredUsername = user.FindFirstValue("preferred_username")
                                            ?? user.Identity?.Name,
                        roles
                    });
                })
            .WithName("Me")
            .RequireAuthorization();

        app.MapGet("/api/antiforgery-token",
                (HttpContext ctx, IAntiforgery antiforgery) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(ctx);
                    return Results.Ok(new { headerName = tokens.HeaderName });
                })
            .WithName("AntiforgeryToken")
            .RequireAuthorization();
    }
}