using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace IntegrationTests.Supports;

internal static class AccessGroupTestHelper
{
    public static async Task<AppDbContext> CreateMetadataDbContextAsync(
        SluiceBaseStackFactory factory, CancellationToken ct)
    {
        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("metadata-db", ct);
        // Match the API's EF configuration (Program.cs registers the context with
        // UseSnakeCaseNamingConvention); without it column names default to PascalCase
        // and writes fail with 42703 "column \"Id\" of relation \"server\" does not exist".
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connStr)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }


    public static async Task<(UserId User, DatabaseId Db)> SeedUserAndDatabaseAsync(
        AppDbContext db, CancellationToken ct)
    {
        var user = User.Create($"res-{Guid.NewGuid():N}@example.com", "Res User", DateTimeOffset.UtcNow);
        db.Users.Add(user);

        var server = PostgresServer.Create($"res-{Guid.NewGuid():N}"[..16], "localhost", 5432, DateTimeOffset.UtcNow);
        db.Servers.Add(server);
        var cred = Credential.Create(server.Id, "read", "user", "enc", DateTimeOffset.UtcNow);
        db.Credentials.Add(cred);
        var database = Database.Create(server.Id, "App DB", "appdb", cred.Id, null, DateTimeOffset.UtcNow);
        db.Databases.Add(database);

        await db.SaveChangesAsync(ct);
        return (user.Id, database.Id);
    }

    /// <summary>
    /// Returns the <see cref="User"/> whose <see cref="ExternalLogin"/> has the given email address.
    /// </summary>
    public static async Task<User> GetUserByEmailAsync(AppDbContext db, string email, CancellationToken ct)
    {
        var userId = await db.ExternalLogins
            .Where(l => l.Email == email)
            .Select(l => l.UserId)
            .FirstAsync(ct);
        return await db.Users.FirstAsync(u => u.Id == userId, ct);
    }

    /// <summary>
    /// Seeds a Server + Credential + Database (no user), and returns the resulting ids.
    /// </summary>
    public static async Task<(ServerId ServerId, DatabaseId DatabaseId)> SeedDatabaseOnlyAsync(
        AppDbContext db, CancellationToken ct)
    {
        var server = PostgresServer.Create($"res-{Guid.NewGuid():N}"[..16], "localhost", 5432, DateTimeOffset.UtcNow);
        db.Servers.Add(server);
        var cred = Credential.Create(server.Id, "read", "user", "enc", DateTimeOffset.UtcNow);
        db.Credentials.Add(cred);
        var database = Database.Create(server.Id, "App DB", "appdb", cred.Id, null, DateTimeOffset.UtcNow);
        db.Databases.Add(database);

        await db.SaveChangesAsync(ct);
        return (server.Id, database.Id);
    }
}
