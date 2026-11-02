using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SluiceBase.Api.Services;

namespace SluiceBase.Api.Tests;

public class BaseUrlResolverTests
{
    private static DefaultHttpContext Request(string scheme, string host)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        return ctx;
    }

    private static BaseUrlResolver Create(HttpContextAccessor accessor, string? publicBaseUrl) =>
        new(accessor, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [BaseUrlResolver.ConfigKey] = publicBaseUrl })
            .Build());

    [Fact]
    public void Resolve_Unset_FollowsEachRequestThroughTheSameInstance()
    {
        var accessor = new HttpContextAccessor();
        var resolver = Create(accessor, null);

        accessor.HttpContext = Request("http", "sluicebase.internal:8080");
        Assert.Equal("http://sluicebase.internal:8080", resolver.Resolve());

        accessor.HttpContext = Request("https", "db.example.com");
        Assert.Equal("https://db.example.com", resolver.Resolve());
    }

    [Fact]
    public void Resolve_Configured_IsUsedForInternalAndExternalRequests()
    {
        var accessor = new HttpContextAccessor();
        var resolver = Create(accessor, "https://Db.Example.com/");

        accessor.HttpContext = Request("http", "sluicebase.internal:8080");
        Assert.Equal("https://db.example.com", resolver.Resolve());

        accessor.HttpContext = Request("https", "db.example.com");
        Assert.Equal("https://db.example.com", resolver.Resolve());
    }

    [Fact]
    public void Resolve_Invalid_FallsBackToRequest()
    {
        var accessor = new HttpContextAccessor { HttpContext = Request("http", "sluicebase.internal:8080") };

        Assert.Equal("http://sluicebase.internal:8080", Create(accessor, "https://x.example/path").Resolve());
    }

    [Fact]
    public void Resolve_UnsetOutsideRequest_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Create(new HttpContextAccessor(), null).Resolve());
    }

    [Fact]
    public void Resolve_ConfiguredOutsideRequest_ReturnsConfigured()
    {
        Assert.Equal("https://db.example.com", Create(new HttpContextAccessor(), "https://db.example.com").Resolve());
    }

    [Theory]
    [InlineData("https://sluice.example.com/", "https://sluice.example.com")]
    [InlineData("https://Sluice.Example.com", "https://sluice.example.com")]
    [InlineData("http://localhost:5443", "http://localhost:5443")]
    [InlineData("https://sluice.example.com:443", "https://sluice.example.com")]
    public void Validate_Canonicalises(string input, string expected)
    {
        Assert.Equal(expected, BaseUrlResolver.Validate(input));
    }

    [Theory]
    [InlineData("https://x.example/sluice")]
    [InlineData("https://x.example?a=1")]
    [InlineData("https://x.example#f")]
    [InlineData("ftp://x.example")]
    [InlineData("x.example")]
    [InlineData("https://user@x.example")]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejectsNonOriginsAndEmpty(string? input)
    {
        Assert.Null(BaseUrlResolver.Validate(input));
    }
}
