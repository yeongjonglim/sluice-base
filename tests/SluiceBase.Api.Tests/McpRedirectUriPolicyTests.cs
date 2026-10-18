using SluiceBase.Api.Mcp.Oauth;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Tests;

public class McpRedirectUriPolicyTests
{
    private static readonly string[] NoAllowlist = [];

    [Theory]
    [InlineData("http://127.0.0.1:33418/")]
    [InlineData("http://127.0.0.1:54321/callback")]
    [InlineData("http://localhost:6274/oauth/callback")]
    [InlineData("http://[::1]:8080/callback")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("cursor://anysphere.cursor-mcp/oauth/callback")]
    public void IsAllowed_AcceptsLoopbackAndNativeAppCallbacks(string uri)
    {
        Assert.True(McpRedirectUriPolicy.IsAllowed(uri, NoAllowlist));
    }

    [Theory]
    [InlineData("https://attacker.example/cb")]
    [InlineData("http://attacker.example/cb")]
    [InlineData("https://vscode.dev/redirect")]
    [InlineData("https://127.0.0.1:8443/callback")]
    [InlineData("http://localhost.attacker.example/cb")]
    [InlineData("http://127.0.0.1@attacker.example/cb")]
    [InlineData("http://attacker.example@127.0.0.1/cb")]
    [InlineData("http://attacker.example\\@127.0.0.1/cb")]
    [InlineData("http://127.0.0.1:8080/cb#frag")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("myapp://callback")]
    [InlineData("cursor://anysphere.cursor-mcp/other")]
    [InlineData("/relative/callback")]
    [InlineData("")]
    public void IsAllowed_RejectsUrisThatCanReachAThirdParty(string uri)
    {
        Assert.False(McpRedirectUriPolicy.IsAllowed(uri, NoAllowlist));
    }

    [Fact]
    public void IsAllowed_AcceptsOperatorAllowlistedUriExactlyOnly()
    {
        string[] allowlist = ["https://claude.ai/api/mcp/auth_callback"];

        Assert.True(McpRedirectUriPolicy.IsAllowed("https://claude.ai/api/mcp/auth_callback", allowlist));
        Assert.False(McpRedirectUriPolicy.IsAllowed("https://claude.ai/api/mcp/auth_callback?x=1", allowlist));
        Assert.False(McpRedirectUriPolicy.IsAllowed("https://CLAUDE.ai/api/mcp/auth_callback", allowlist));
    }

    [Theory]
    [InlineData("http://127.0.0.1:33418/", "http://127.0.0.1:51234/")]
    [InlineData("http://127.0.0.1/callback", "http://127.0.0.1:51234/callback")]
    [InlineData("http://localhost:3000/cb", "http://localhost:4000/cb")]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback")]
    public void Matches_ExactOrLoopbackOnAnyPort(string registered, string requested)
    {
        Assert.True(McpRedirectUriPolicy.Matches(registered, requested));
    }

    [Theory]
    [InlineData("http://127.0.0.1:3000/cb", "http://127.0.0.1:3000/other")]
    [InlineData("http://127.0.0.1:3000/cb", "http://localhost:3000/cb")]
    [InlineData("http://127.0.0.1:3000/cb", "http://127.0.0.1:3000/cb?x=1")]
    [InlineData("https://claude.ai/cb", "https://claude.ai:8443/cb")]
    [InlineData("http://127.0.0.1:3000/cb", "https://attacker.example:3000/cb")]
    public void Matches_RejectsDifferentHostPathQueryOrNonLoopbackPort(string registered, string requested)
    {
        Assert.False(McpRedirectUriPolicy.Matches(registered, requested));
    }

    [Fact]
    public void IsConforming_RequiresAtLeastOneUriAndAllAllowed()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(McpRedirectUriPolicy.IsConforming(
            McpOAuthClient.Register("ok", ["http://127.0.0.1:1/"], now), NoAllowlist));
        Assert.False(McpRedirectUriPolicy.IsConforming(
            McpOAuthClient.Register("mixed", ["http://127.0.0.1:1/", "https://attacker.example/cb"], now), NoAllowlist));
        Assert.False(McpRedirectUriPolicy.IsConforming(
            McpOAuthClient.Register("empty", [], now), NoAllowlist));
    }
}
