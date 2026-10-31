using SluiceBase.Api.Mcp;

namespace SluiceBase.Api.Tests;

public class McpOptionsTests
{
    [Fact]
    public void GetValidatedServerName_DefaultsToSluicebase()
    {
        var options = new McpOptions();

        Assert.Equal("sluicebase", options.GetValidatedServerName());
    }

    [Theory]
    [InlineData("acme-db")]
    [InlineData("acme_db")]
    [InlineData("AcmeDB2")]
    public void GetValidatedServerName_ReturnsValidIdentifierUnchanged(string name)
    {
        var options = new McpOptions { ServerName = name };

        Assert.Equal(name, options.GetValidatedServerName());
    }

    [Theory]
    [InlineData("acme db")]
    [InlineData("acme.db")]
    [InlineData("")]
    [InlineData("bad/name")]
    public void GetValidatedServerName_FallsBackOnInvalidIdentifier(string name)
    {
        var options = new McpOptions { ServerName = name };

        Assert.Equal("sluicebase", options.GetValidatedServerName());
    }

    [Theory]
    [InlineData("https://sluice.example.com/", "https://sluice.example.com")]
    [InlineData("https://Sluice.Example.com", "https://sluice.example.com")]
    [InlineData("http://localhost:5443", "http://localhost:5443")]
    [InlineData("https://sluice.example.com:443", "https://sluice.example.com")]
    public void GetValidatedPublicBaseUrl_Canonicalises(string input, string expected)
    {
        var options = new McpOptions { PublicBaseUrl = input };

        Assert.Equal(expected, options.GetValidatedPublicBaseUrl());
    }

    [Theory]
    [InlineData("https://x.example/sluice")]
    [InlineData("https://x.example?a=1")]
    [InlineData("https://x.example#f")]
    [InlineData("ftp://x.example")]
    [InlineData("x.example")]
    [InlineData("https://user@x.example")]
    public void GetValidatedPublicBaseUrl_RejectsNonOrigins(string input)
    {
        var options = new McpOptions { PublicBaseUrl = input };

        Assert.Null(options.GetValidatedPublicBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetValidatedPublicBaseUrl_NullOrEmpty_ReturnsNull(string? input)
    {
        var options = new McpOptions { PublicBaseUrl = input };

        Assert.Null(options.GetValidatedPublicBaseUrl());
    }

    [Fact]
    public void GetValidatedClients_RedirectUriWithFragment_Skipped()
    {
        var options = new McpOptions
        {
            Clients =
            [
                new McpConfiguredClient { ClientId = "frag", RedirectUris = ["https://x.example/cb#f"] },
                new McpConfiguredClient { ClientId = "ok", RedirectUris = ["https://x.example/cb"] },
            ],
        };

        Assert.Equal(["ok"], options.GetValidatedClients().Select(c => c.ClientId));
    }
}
