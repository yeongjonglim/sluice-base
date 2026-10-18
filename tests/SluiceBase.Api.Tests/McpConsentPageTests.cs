using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SluiceBase.Api.Mcp.Oauth;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Tests;

public class McpConsentPageTests
{
    private static async Task<(HttpContext Ctx, string Html)> RenderAsync(string redirectUri, string clientName)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        var request = new AuthorizeRequest("client-1", redirectUri, "code", "challenge", "S256", "st\"ate");
        var tokens = new AntiforgeryTokenSet("request-token", "cookie-token", "__RequestVerificationToken", "X-XSRF-TOKEN");

        var result = McpConsentPage.Render(ctx, request, clientName, "alice@example.com", tokens, new BrandingOptions());
        await result.ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        return (ctx, await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Render_ForbidsFramingAndCaching()
    {
        var (ctx, _) = await RenderAsync("http://127.0.0.1:33418/", "Claude Code");

        Assert.Contains("frame-ancestors 'none'", ctx.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("DENY", ctx.Response.Headers.XFrameOptions.ToString());
        Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Render_EncodesClientControlledValues()
    {
        var (_, html) = await RenderAsync("http://127.0.0.1:33418/", "<script>alert(1)</script>");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("value=\"st&quot;ate\"", html);
    }

    [Fact]
    public async Task Render_PostsDecisionWithAuthorizeParametersAndAntiforgeryToken()
    {
        var (_, html) = await RenderAsync("http://127.0.0.1:33418/", "Claude Code");

        Assert.Contains("<form method=\"post\" action=\"/mcp/oauth/authorize\">", html);
        Assert.Contains("name=\"__RequestVerificationToken\" value=\"request-token\"", html);
        Assert.Contains("name=\"code_challenge\" value=\"challenge\"", html);
        Assert.Contains("name=\"decision\" value=\"allow\"", html);
        Assert.Contains("name=\"decision\" value=\"deny\"", html);
    }

    [Theory]
    [InlineData("http://127.0.0.1:33418/", "127.0.0.1:33418", "an app running on this computer")]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "claude.ai", "this website")]
    [InlineData("cursor://anysphere.cursor-mcp/oauth/callback", "cursor://anysphere.cursor-mcp", "this desktop app")]
    public async Task Render_ShowsWhereTheCodeWillBeSent(string redirectUri, string destination, string kind)
    {
        var (_, html) = await RenderAsync(redirectUri, "Some Client");

        Assert.Contains($"<p class=\"host\">{destination}</p>", html);
        Assert.Contains(kind, html);
    }
}
