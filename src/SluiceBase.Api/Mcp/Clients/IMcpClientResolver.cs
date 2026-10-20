namespace SluiceBase.Api.Mcp.Clients;

internal interface IMcpClientResolver
{
    Task<ClientResolution> ResolveAsync(string clientId, CancellationToken ct);
}
