using Microsoft.AspNetCore.Http;
using SluiceBase.Api.Mcp;

namespace SluiceBase.Api.Tests;

public class McpIssuerTests
{
    private static DefaultHttpContext Context()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("internal:8080");
        return ctx;
    }

    [Fact]
    public void BaseUrl_ConfiguredValue_IsCanonicalAndIgnoresRequestHost()
    {
        var options = new McpOptions { PublicBaseUrl = "https://Sluice.Example.com/" };

        Assert.Equal("https://sluice.example.com", McpIssuer.BaseUrl(Context(), options));
    }

    [Fact]
    public void BaseUrl_Unset_FallsBackToRequest()
    {
        Assert.Equal("http://internal:8080", McpIssuer.BaseUrl(Context(), new McpOptions()));
    }

    [Fact]
    public void BaseUrl_Invalid_FallsBackToRequest()
    {
        var options = new McpOptions { PublicBaseUrl = "https://x.example/path" };

        Assert.Equal("http://internal:8080", McpIssuer.BaseUrl(Context(), options));
    }
}
