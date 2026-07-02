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
}
