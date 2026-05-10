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

    public async Task<string> GetConnectionStringAsync(DatabaseId databaseId, CredentialKind kind, CancellationToken ct)
    {
        var database = await db.Databases
            .AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct)
            ?? throw new InvalidOperationException($"Database {databaseId} not found.");

        if (database.Server!.IsDisabled)
        {
            throw new InvalidOperationException($"Server '{database.Server.Name}' is disabled.");
        }

        if (database.IsDisabled)
        {
            throw new InvalidOperationException($"Database '{database.DisplayName}' is disabled.");
        }

        var credentialId = kind == CredentialKind.Read
            ? database.ReadCredentialId
            : database.WriteCredentialId
                ?? throw new InvalidOperationException(
                    $"Database '{database.DisplayName}' has no write credential configured.");

        var credential = await db.Credentials
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == credentialId, ct)
            ?? throw new InvalidOperationException($"Credential {credentialId} not found.");

        var password = _protector.Unprotect(credential.EncryptedPassword);

        return new NpgsqlConnectionStringBuilder
        {
            Host = database.Server.Host,
            Port = database.Server.Port,
            Database = database.DatabaseName,
            Username = credential.Username,
            Password = password,
        }.ConnectionString;
    }
}