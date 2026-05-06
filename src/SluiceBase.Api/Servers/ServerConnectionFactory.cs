using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SluiceBase.Api.Data;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Servers;

internal sealed class ServerConnectionFactory(
    AppDbContext db,
    IDataProtectionProvider dataProtection) : IServerConnectionFactory
{
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector(ProtectorPurpose);

    public const string ProtectorPurpose = "SluiceBase.ServerPassword";

    public async Task<string> GetConnectionStringAsync(ServerId serverId, CredentialKind kind, CancellationToken ct)
    {
        var server = await db.Servers
                         .AsNoTracking()
                         .SingleOrDefaultAsync(s => s.Id == serverId, ct)
                     ?? throw new InvalidOperationException($"Server {serverId} not found.");

        if (kind == CredentialKind.Write && !server.HasWriteCredential)
        {
            throw new InvalidOperationException(
                $"Server '{server.Name}' has no write credential configured.");
        }

        var encryptedPassword = kind == CredentialKind.Read
            ? server.EncryptedReadPassword
            : server.EncryptedWritePassword!;

        var username = kind == CredentialKind.Read
            ? server.ReadUsername
            : server.WriteUsername!;

        var password = _protector.Unprotect(encryptedPassword);

        return new NpgsqlConnectionStringBuilder
        {
            Host = server.Host,
            Port = server.Port,
            Database = server.Database,
            Username = username,
            Password = password,
        }.ConnectionString;
    }
}