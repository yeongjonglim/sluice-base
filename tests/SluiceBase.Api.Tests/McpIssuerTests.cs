using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Mcp;

namespace SluiceBase.Api.Tests;

public class McpIssuerTests
{
    private static McpIssuer Create(McpOptions options)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("internal:8080");
        return new McpIssuer(new HttpContextAccessor { HttpContext = ctx }, Options.Create(options));
    }

    [Fact]
    public void BaseUrl_ConfiguredValue_IsCanonicalAndIgnoresRequestHost()
    {
        var issuer = Create(new McpOptions { PublicBaseUrl = "https://Sluice.Example.com/" });

        Assert.Equal("https://sluice.example.com", issuer.BaseUrl());
    }

    [Fact]
    public void BaseUrl_Unset_FallsBackToRequest()
    {
        Assert.Equal("http://internal:8080", Create(new McpOptions()).BaseUrl());
    }

    [Fact]
    public void BaseUrl_Invalid_FallsBackToRequest()
    {
        var issuer = Create(new McpOptions { PublicBaseUrl = "https://x.example/path" });

        Assert.Equal("http://internal:8080", issuer.BaseUrl());
    }

    [Fact]
    public void BaseUrl_UnsetOutsideRequest_Throws()
    {
        var issuer = new McpIssuer(new HttpContextAccessor(), Options.Create(new McpOptions()));

        Assert.Throws<InvalidOperationException>(() => issuer.BaseUrl());
    }
}
