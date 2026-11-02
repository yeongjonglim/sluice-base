using System.Security.Claims;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Targets;

namespace SluiceBase.Api.Tests;

public class AuthHardeningTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/query")]
    [InlineData("/update/123?tab=preview")]
    [InlineData("/%2fstill-a-path")]
    public void IsLocalPath_AcceptsPathsOnThisSite(string url)
    {
        Assert.True(AuthEndpoints.IsLocalPath(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.com")]
    [InlineData("///evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("/\t/evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("evil.com")]
    public void IsLocalPath_RejectsAnythingABrowserWouldSendElsewhere(string? url)
    {
        Assert.False(AuthEndpoints.IsLocalPath(url));
    }

    [Theory]
    [InlineData("target", "dbname='target'")]
    [InlineData("target host=evil", "dbname='target host=evil'")]
    [InlineData("o'brien", @"dbname='o\'brien'")]
    [InlineData(@"a\b", @"dbname='a\\b'")]
    public void ConnInfo_QuotesTheValue(string value, string expected)
    {
        Assert.Equal(expected, PostgresTargetEngine.ConnInfo("dbname", value));
    }

    [Theory]
    [InlineData("true", "admin@example.com")]
    [InlineData("True", "admin@example.com")]
    [InlineData("false", null)]
    [InlineData(null, null)]
    public void GetVerifiedEmail_RequiresEmailVerified(string? emailVerified, string? expected)
    {
        var claims = new List<Claim> { new("email", "admin@example.com"), new("preferred_username", "admin@example.com") };
        if (emailVerified is not null)
        {
            claims.Add(new Claim("email_verified", emailVerified));
        }

        Assert.Equal(expected, new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc")).GetVerifiedEmail());
    }

    [Fact]
    public void GetVerifiedEmail_IgnoresUsernameFallbacks()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("preferred_username", "admin@example.com"), new Claim("email_verified", "true")], "oidc"));

        Assert.Null(principal.GetVerifiedEmail());
        Assert.Equal("admin@example.com", principal.GetEmail());
    }
}
