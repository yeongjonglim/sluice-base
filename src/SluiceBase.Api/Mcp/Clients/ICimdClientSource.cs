namespace SluiceBase.Api.Mcp.Clients;

internal interface ICimdClientSource
{
    Task<ClientResolution> ResolveAsync(Uri clientIdUrl, CancellationToken ct);
}
