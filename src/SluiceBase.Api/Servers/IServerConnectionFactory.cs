using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Servers;

internal interface IServerConnectionFactory
{
    Task<string> GetConnectionStringAsync(DatabaseId databaseId, CredentialKind kind, CancellationToken ct);
}