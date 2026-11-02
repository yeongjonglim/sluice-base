using SluiceBase.Api.Mcp.Clients;
using SluiceBase.Api.Mcp.Oauth;

namespace SluiceBase.Api.Tests;

public class OAuthAuthorizeEndpointsTests
{
    [Theory]
    [InlineData(true, "cursor://anysphere.cursor-mcp/oauth/callback", true)]
    [InlineData(true, "https://127.0.0.1/cb", true)]
    [InlineData(true, "https://claude.ai/cb", true)]
    [InlineData(true, "http://[::1]:5/cb", false)]
    [InlineData(true, "http://localhost:5/cb", false)]
    [InlineData(true, "http://127.0.0.1:5/cb", false)]
    [InlineData(false, "https://x.example/cb", false)]
    [InlineData(false, "cursor://anysphere.cursor-mcp/oauth/callback", false)]
    public void RequiresConsent(bool cimd, string redirectUri, bool expected)
    {
        var client = new ResolvedClient("client-1", "Client", null, [redirectUri], cimd ? ClientKind.Cimd : ClientKind.Configured);

        Assert.Equal(expected, OAuthAuthorizeEndpoints.RequiresConsent(client, redirectUri));
    }

    [Theory]
    [InlineData("https://claude.ai/oauth/claude-code-client-metadata", true)]
    [InlineData("sluicebase-dev", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("tab\there", false)]
    [InlineData("caf\u00e9", false)]
    public void IsAcceptableLoopbackClientId(string clientId, bool expected)
    {
        Assert.Equal(expected, OAuthAuthorizeEndpoints.IsAcceptableLoopbackClientId(clientId));
    }

    [Fact]
    public void IsAcceptableLoopbackClientId_RejectsOverlongIds()
    {
        Assert.True(OAuthAuthorizeEndpoints.IsAcceptableLoopbackClientId(new string('a', 512)));
        Assert.False(OAuthAuthorizeEndpoints.IsAcceptableLoopbackClientId(new string('a', 513)));
    }
}
