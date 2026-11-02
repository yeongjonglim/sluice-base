using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore.Authentication;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;
using SluiceBase.Core.Permissions;

namespace SluiceBase.Api.Auth;

internal static class AuthSetup
{
    private const string CookieName = "sb.auth";
    private const string DisabledAccountMessage = "This account is disabled. Ask an administrator to enable it.";

    public static IHostApplicationBuilder AddSluiceBaseAuth(
        this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);

                // Sliding expiration alone would keep an active session alive forever, so the session also ends
                // a fixed time after sign-in, and as soon as the user is disabled or removed.
                var absoluteLifetime = TimeSpan.FromHours(config.GetValue("Auth:AbsoluteSessionHours", 24));
                options.Events.OnValidatePrincipal = async ctx =>
                {
                    var principal = ctx.Principal;
                    var signedInAt = principal?.GetSignedInAt();
                    var clock = ctx.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                    var valid = principal is not null
                        && principal.TryGetInternalUserId(out var userId)
                        && signedInAt is not null
                        && clock.GetUtcNow() - signedInAt.Value < absoluteLifetime
                        && await ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>().Users
                            .AnyAsync(u => u.Id == userId && u.DisabledAt == null, ctx.HttpContext.RequestAborted);
                    if (!valid)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };

                options.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = config["Oidc:Authority"];
                options.ClientId = config["Oidc:ClientId"];
                options.ClientSecret = config["Oidc:ClientSecret"];
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";

                options.MapInboundClaims = false;

                options.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.HandleResponse();
                    }

                    return Task.CompletedTask;
                };

                options.Events.OnTokenValidated = async ctx =>
                {
                    var requestServices = ctx.HttpContext.RequestServices;
                    var recorder = requestServices.GetRequiredService<IUserLoginRecorder>();
                    var clock = requestServices.GetRequiredService<TimeProvider>();

                    if (ctx.Principal is null)
                    {
                        throw new InvalidOperationException("Principal is null");
                    }

                    // Claims under the sb: prefix are SluiceBase's own (the internal user id, the sign-in time);
                    // one the IdP sends, say from a user-editable attribute, must not be mistaken for them.
                    foreach (var identity in ctx.Principal.Identities)
                    {
                        foreach (var claim in identity.FindAll(c => c.Type.StartsWith(AppClaims.Prefix, StringComparison.Ordinal)).ToList())
                        {
                            identity.TryRemoveClaim(claim);
                        }
                    }

                    var issuer = ctx.Principal.GetIssuer();
                    var sub = ctx.Principal.GetSubject();
                    var email = ctx.Principal.GetEmail();
                    var name = ctx.Principal.GetName();

                    var user = await recorder.RecordLoginAsync(
                        issuer, sub, email, ctx.Principal.GetVerifiedEmail(), name, clock.GetUtcNow(),
                        ctx.Principal.GetClaims(),
                        ctx.HttpContext.RequestAborted);

                    if (user.IsDisabled)
                    {
                        ctx.Fail(DisabledAccountMessage);
                        return;
                    }

                    var claimsIdentity = new ClaimsIdentity();
                    claimsIdentity.AddClaim(new Claim(AppClaims.InternalUserIdClaim, user.Id.ToString()));
                    claimsIdentity.AddClaim(new Claim(AppClaims.SignedInAtClaim,
                        clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
                    ctx.Principal.AddIdentity(claimsIdentity);
                };

                // Without this a disabled user would get the framework's error page for the failed sign-in.
                options.Events.OnRemoteFailure = async ctx =>
                {
                    if (ctx.Failure?.Message == DisabledAccountMessage)
                    {
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await ctx.Response.WriteAsync(DisabledAccountMessage);
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, McpBearerAuthenticationHandler>(
                McpBearerAuthenticationHandler.SchemeName,
                // The MCP SDK's scheme (registered with the MCP server in Program.cs) writes the 401's
                // WWW-Authenticate resource_metadata pointer; this scheme only validates tokens.
                options => options.ForwardChallenge = McpAuthenticationDefaults.AuthenticationScheme);

        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.Global)
            {
                options.AddPolicy(permission,
                    policy => policy.Requirements.Add(new PermissionRequirement(permission)));
            }

            options.AddPolicy(McpBearerAuthenticationHandler.SchemeName, policy => policy
                .AddAuthenticationSchemes(McpBearerAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());
        });

        services.AddScoped<IUserLoginRecorder, UserLoginRecorder>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IAccessResolver, AccessResolver>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();
        services.AddHttpContextAccessor();
        services.Configure<BootstrapAdminOptions>(
            config.GetSection(BootstrapAdminOptions.SectionName));

        return builder;
    }
}