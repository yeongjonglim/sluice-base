using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using SluiceBase.Core.Permissions;

namespace SluiceBase.Api.Auth;

internal static class AuthSetup
{
    private const string CookieName = "sb.auth";

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

                    var issuer = ctx.Principal.GetIssuer();
                    var sub = ctx.Principal.GetSubject();
                    var email = ctx.Principal.GetEmail();
                    var name = ctx.Principal.GetName();

                    var user = await recorder.RecordLoginAsync(
                        issuer, sub, email, name, clock.GetUtcNow(),
                        ctx.Principal.GetClaims(),
                        ctx.HttpContext.RequestAborted);

                    var claimsIdentity = new ClaimsIdentity();
                    claimsIdentity.AddClaim(new Claim(AppClaims.InternalUserIdClaim, user.Id.ToString()));
                    ctx.Principal.AddIdentity(claimsIdentity);
                };
            });

        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.Global)
            {
                options.AddPolicy(permission,
                    policy => policy.Requirements.Add(new PermissionRequirement(permission)));
            }
        });

        services.AddScoped<IUserLoginRecorder, UserLoginRecorder>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();
        services.AddHttpContextAccessor();
        services.Configure<BootstrapAdminOptions>(
            config.GetSection(BootstrapAdminOptions.SectionName));

        return builder;
    }
}