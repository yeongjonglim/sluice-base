using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

public sealed class BrandingEndpointTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Branding_Anonymous_ReturnsOk()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/branding", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Branding_ReturnsDefaultValues()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/branding", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadFromJsonAsync<BrandingResponseDto>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(content);
        Assert.Equal("SluiceBase", content.AppName);
        Assert.Equal("teal", content.PrimaryColor);
        Assert.False(content.HasLogo);
        Assert.False(content.HasFavicon);
    }

    [Fact]
    public async Task BrandingLogo_NotConfigured_ReturnsNotFound()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/branding/logo", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BrandingFavicon_NotConfigured_ReturnsNotFound()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/branding/favicon", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

internal sealed record BrandingResponseDto(string AppName, string PrimaryColor, bool HasLogo, bool HasFavicon);