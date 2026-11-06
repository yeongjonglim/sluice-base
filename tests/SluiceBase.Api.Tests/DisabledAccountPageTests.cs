using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SluiceBase.Api.Pages;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Tests;

public class DisabledAccountPageTests
{
    private const string SignOutUrl =
        "https://idp.example/logout?id_token_hint=tok&post_logout_redirect_uri=https%3A%2F%2Fapp%2Fsignout-callback-oidc";

    private static async Task<(HttpContext Ctx, string Html)> RenderAsync(
        string? email, string? signOutUrl, BrandingOptions? branding = null)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();

        await DisabledAccountPage.Render(ctx, email, "/login", signOutUrl, branding ?? new BrandingOptions())
            .ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        return (ctx, await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Render_Is403_AndForbidsFramingCachingAndScripts()
    {
        var (ctx, html) = await RenderAsync("bob@example.com", SignOutUrl);

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
        var csp = ctx.Response.Headers.ContentSecurityPolicy.ToString();
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", ctx.Response.Headers["Referrer-Policy"].ToString());
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task Render_NamesTheAccountAndOffersBothWaysOut()
    {
        var (_, html) = await RenderAsync("bob@example.com", SignOutUrl);

        Assert.Contains("Your account is disabled", html);
        Assert.Contains("You signed in as <strong>bob@example.com</strong>", html);
        Assert.Contains("""<a class="button primary" href="/login">Try again</a>""", html);
        Assert.Contains(
            """<a class="button" href="https://idp.example/logout?id_token_hint=tok&amp;post_logout_redirect_uri=https%3A%2F%2Fapp%2Fsignout-callback-oidc">Use a different account</a>""",
            html);
    }

    [Fact]
    public async Task Render_WithoutSignOutUrl_OffersOnlyTryAgain()
    {
        var (_, html) = await RenderAsync(null, signOutUrl: null);

        Assert.Contains("You signed in, but", html);
        Assert.DoesNotContain("Use a different account", html);
        Assert.DoesNotContain("wrong account", html);
        Assert.Contains("Try again", html);
    }

    [Fact]
    public async Task Render_EncodesIdpAndOperatorControlledValues()
    {
        var (_, html) = await RenderAsync(
            "<img src=x onerror=alert(1)>@example.com", SignOutUrl, new BrandingOptions { AppName = "<b>Evil</b>" });

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;@example.com", html);
        Assert.DoesNotContain("<b>Evil</b>", html);
    }
}
