using Microsoft.Extensions.Options;

namespace SluiceBase.Api.Mcp.Clients;

internal sealed class McpClientResolver(
    IOptions<McpOptions> options,
    ICimdClientSource cimdSource,
    ILogger<McpClientResolver> logger) : IMcpClientResolver
{
    private readonly Lazy<IReadOnlyDictionary<string, ResolvedClient>> _configured = new(() =>
        options.Value.GetValidatedClients(logger).ToDictionary(
            c => c.ClientId,
            c => new ResolvedClient(
                c.ClientId,
                string.IsNullOrWhiteSpace(c.Name) ? c.ClientId : c.Name,
                null,
                c.RedirectUris,
                ClientKind.Configured),
            StringComparer.Ordinal));

    public Task<ClientResolution> ResolveAsync(string clientId, CancellationToken ct)
    {
        if (_configured.Value.TryGetValue(clientId, out var configured))
        {
            return Task.FromResult<ClientResolution>(new ClientResolution.Found(configured));
        }

        if (Uri.TryCreate(clientId, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
        {
            return cimdSource.ResolveAsync(url, ct);
        }

        return Task.FromResult<ClientResolution>(ClientResolution.NotFound.Instance);
    }
}
