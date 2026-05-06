using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Servers;

public interface IServerConnectionFactory
{
    Task<string> GetConnectionStringAsync(ServerId serverId, CredentialKind kind, CancellationToken ct);
}