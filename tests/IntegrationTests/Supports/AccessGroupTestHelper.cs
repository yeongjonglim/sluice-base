using SluiceBase.Api.Data;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace IntegrationTests.Supports;

internal static class AccessGroupTestHelper
{
    public static async Task<(UserId User, DatabaseId Db)> SeedUserAndDatabaseAsync(
        AppDbContext db, CancellationToken ct)
    {
        var user = User.Create($"res-{Guid.NewGuid():N}@example.com", "Res User", DateTimeOffset.UtcNow);
        db.Users.Add(user);

        var server = Server.Create($"res-{Guid.NewGuid():N}"[..16], "postgres", "localhost", 5432, DateTimeOffset.UtcNow);
        db.Servers.Add(server);
        var cred = Credential.Create(server.Id, "read", "user", "enc", DateTimeOffset.UtcNow);
        db.Credentials.Add(cred);
        var database = Database.Create(server.Id, "App DB", "appdb", cred.Id, null, DateTimeOffset.UtcNow);
        db.Databases.Add(database);

        await db.SaveChangesAsync(ct);
        return (user.Id, database.Id);
    }
}
