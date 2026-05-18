using System.Net;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

public sealed class BrandingHtmlTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Root_ReturnsHtmlWithInjectedTitle()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>TestCompany</title>", html);
    }

    [Fact]
    public async Task Root_ReturnsHtmlWithBrandingScript()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("window.__BRANDING__", html);
        Assert.Contains("\"appName\":\"TestCompany\"", html);
        Assert.Contains("\"primaryColor\":\"indigo\"", html);
    }

    [Fact]
    public async Task Root_NoBrandingFiles_LogoAndFaviconAreNull()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"logoUrl\":null", html);
        Assert.Contains("\"faviconUrl\":null", html);
    }

    [Fact]
    public async Task DeepRoute_ReturnsSameInjectedHtml()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/some/deep/route", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("window.__BRANDING__", html);
        Assert.Contains("<title>TestCompany</title>", html);
    }

    [Fact]
    public async Task ApiRoute_NotInterceptedByMiddleware()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("window.__BRANDING__", content);
    }
}
