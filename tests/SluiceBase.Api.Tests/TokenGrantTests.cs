using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SluiceBase.Api.Mcp.Oauth;

namespace SluiceBase.Api.Tests;

public class TokenGrantTests
{
    private static FormCollection Form(params (string Key, string Value)[] fields) =>
        new(fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)));

    [Fact]
    public void TryFromForm_AuthorizationCode_ReadsCodeRedirectAndVerifier()
    {
        var form = Form(
            ("grant_type", "authorization_code"), ("client_id", "c"), ("code", "abc"),
            ("redirect_uri", "http://127.0.0.1/cb"), ("code_verifier", "v"), ("refresh_token", "ignored"));

        Assert.True(TokenGrant.TryFromForm(form, out var grant));
        Assert.Equal(new AuthorizationCodeGrant("c", "abc", "http://127.0.0.1/cb", "v"), grant);
    }

    [Fact]
    public void TryFromForm_RefreshToken_ReadsRefreshToken()
    {
        var form = Form(("grant_type", "refresh_token"), ("client_id", "c"), ("refresh_token", "r"), ("code", "ignored"));

        Assert.True(TokenGrant.TryFromForm(form, out var grant));
        Assert.Equal(new RefreshTokenGrant("c", "r"), grant);
    }

    [Theory]
    [InlineData("client_credentials")]
    [InlineData("")]
    public void TryFromForm_UnsupportedGrant_Fails(string grantType)
    {
        Assert.False(TokenGrant.TryFromForm(Form(("grant_type", grantType), ("client_id", "c")), out var grant));
        Assert.Null(grant);
    }

    [Fact]
    public void TryFromForm_MissingGrantType_Fails()
    {
        Assert.False(TokenGrant.TryFromForm(Form(("client_id", "c")), out _));
    }
}
