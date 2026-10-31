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
}
