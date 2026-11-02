using SluiceBase.Core.Mcp;

namespace SluiceBase.Core.Tests;

public class GrantTypeTests
{
    [Theory]
    [InlineData("authorization_code")]
    [InlineData("refresh_token")]
    public void TryFrom_SupportedGrant_MatchesInstance(string value)
    {
        Assert.True(GrantType.TryFrom(value, out var grant));
        Assert.Contains(grant, new[] { GrantType.AuthorizationCode, GrantType.RefreshToken });
    }

    [Theory]
    [InlineData("client_credentials")]
    [InlineData("urn:ietf:params:oauth:grant-type:device_code")]
    [InlineData("Authorization_Code")]
    [InlineData("")]
    public void TryFrom_UnsupportedGrant_Fails(string value)
    {
        Assert.False(GrantType.TryFrom(value, out _));
    }
}
