using Microsoft.Extensions.Options;
using SluiceBase.Api.Mcp;
using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Tests;

public class McpClientResolverTests
{
    private const string Guid = "0b8e3c1a-5d2f-4c7e-9a41-6f2d8b7c1e90";
    private const string CimdId = "https://app.example/client.json";

    private sealed class FakeCimdSource(ClientResolution result) : ICimdClientSource
    {
        public List<Uri> Calls { get; } = [];

        public Task<ClientResolution> ResolveAsync(Uri clientIdUrl, CancellationToken ct)
        {
            Calls.Add(clientIdUrl);
            return Task.FromResult(result);
        }
    }

    private static McpClientResolver Create(McpOptions options, FakeCimdSource source) =>
        new(Options.Create(options), source);

    private static McpConfiguredClient Configured(string id) =>
        new() { ClientId = id, Name = "Configured App", RedirectUris = ["https://cb.example/cb"] };

    [Fact]
    public async Task Resolve_ConfiguredId_ReturnsConfigured()
    {
        var source = new FakeCimdSource(ClientResolution.NotFound.Instance);
        var resolver = Create(new McpOptions { Clients = [Configured(Guid)] }, source);

        var result = await resolver.ResolveAsync(Guid, TestContext.Current.CancellationToken);

        var found = Assert.IsType<ClientResolution.Found>(result);
        Assert.Equal(ClientKind.Configured, found.Client.Kind);
        Assert.Null(found.Client.VerifiedHost);
        Assert.Equal("Configured App", found.Client.DisplayName);
        Assert.Equal(["https://cb.example/cb"], found.Client.RedirectUris);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task Resolve_ConfiguredHttpsId_WinsOverCimd()
    {
        var source = new FakeCimdSource(ClientResolution.NotFound.Instance);
        var resolver = Create(new McpOptions { Clients = [Configured(CimdId)] }, source);

        var result = await resolver.ResolveAsync(CimdId, TestContext.Current.CancellationToken);

        var found = Assert.IsType<ClientResolution.Found>(result);
        Assert.Equal(ClientKind.Configured, found.Client.Kind);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task Resolve_HttpsId_DelegatesToCimdSource()
    {
        var cimd = new ResolvedClient(CimdId, "App", "app.example", ["https://app.example/cb"], ClientKind.Cimd);
        var source = new FakeCimdSource(new ClientResolution.Found(cimd));
        var resolver = Create(new McpOptions(), source);

        var result = await resolver.ResolveAsync(CimdId, TestContext.Current.CancellationToken);

        Assert.Equal(new ClientResolution.Found(cimd), result);
        Assert.Equal([new Uri(CimdId)], source.Calls);
    }

    [Theory]
    [InlineData(Guid)]
    [InlineData("http://x.example/c.json")]
    [InlineData("")]
    public async Task Resolve_UnknownNonHttpsId_NotFound(string clientId)
    {
        var source = new FakeCimdSource(ClientResolution.TemporarilyUnavailable.Instance);
        var resolver = Create(new McpOptions(), source);

        var result = await resolver.ResolveAsync(clientId, TestContext.Current.CancellationToken);

        Assert.Same(ClientResolution.NotFound.Instance, result);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void GetValidatedClients_SkipsInvalidAndDuplicate()
    {
        var options = new McpOptions
        {
            Clients =
            [
                Configured("good"),
                new McpConfiguredClient { ClientId = "", Name = "x", RedirectUris = ["https://a.example/cb"] },
                new McpConfiguredClient { ClientId = "no-uris", Name = "x", RedirectUris = [] },
                new McpConfiguredClient { ClientId = "relative", Name = "x", RedirectUris = ["relative/cb"] },
                Configured("good"),
                Configured("second"),
            ],
        };

        var clients = options.GetValidatedClients();

        Assert.Equal(["good", "second"], clients.Select(c => c.ClientId));
    }
}
