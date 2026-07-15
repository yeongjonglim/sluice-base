using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SluiceBase.Api.Data;
using Testcontainers.PostgreSql;

namespace IntegrationTests;

public sealed class MigrationSeedingTests : IAsyncLifetime
{
    // Image tag sourced from https://github.com/microsoft/aspire/blob/main/src/Aspire.Hosting.PostgreSQL/PostgresContainerImageTags.cs
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("sluice_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    [Fact]
    public async Task AddUserDatabaseRole_Migration_SeedsRolesFromExistingPermissions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        // Apply all migrations up to (not including) the new one.
        await migrator.MigrateAsync("20260512003313_Initial", ct);

        // Insert a user, a server + credential + database, and two scopeable user_permissions.
        await ctx.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""user"" (id, email, name, created_at)
            VALUES ('11111111-1111-1111-1111-111111111111'::uuid, 'user@example.com', 'Test User', NOW());

            INSERT INTO server (id, name, kind, host, port, is_disabled, created_at, updated_at)
            VALUES ('22222222-2222-2222-2222-222222222222'::uuid, 'test-srv', 'postgres', 'localhost', 5432, false, NOW(), NOW());

            INSERT INTO server_credential (id, server_id, label, username, encrypted_password, created_at, updated_at)
            VALUES ('33333333-3333-3333-3333-333333333333'::uuid,
                    '22222222-2222-2222-2222-222222222222'::uuid,
                    'read', 'pg', 'enc', NOW(), NOW());

            INSERT INTO server_database (id, server_id, display_name, database_name, read_credential_id, is_disabled, created_at, updated_at)
            VALUES ('44444444-4444-4444-4444-444444444444'::uuid,
                    '22222222-2222-2222-2222-222222222222'::uuid,
                    'Test DB', 'testdb',
                    '33333333-3333-3333-3333-333333333333'::uuid,
                    false, NOW(), NOW());

            INSERT INTO user_permission (id, user_id, permission, granted_at)
            VALUES
                ('55555555-5555-5555-5555-555555555555'::uuid,
                 '11111111-1111-1111-1111-111111111111'::uuid, 'query:execute', NOW()),
                ('66666666-6666-6666-6666-666666666666'::uuid,
                 '11111111-1111-1111-1111-111111111111'::uuid, 'update:submit', NOW()),
                ('77777777-7777-7777-7777-777777777777'::uuid,
                 '11111111-1111-1111-1111-111111111111'::uuid, 'permission:manage', NOW());
        ", cancellationToken: ct);

        // Apply the new migration.
        await migrator.MigrateAsync(cancellationToken: ct);

        // Verify: one user_database_role row per (scopeable permission × non-deleted database)
        var roles = await ctx.UserDatabaseRoles.ToListAsync(ct);
        Assert.Equal(2, roles.Count); // query:execute + update:submit, each on the 1 database

        Assert.Contains(roles, r =>
            r.UserId.Value == Guid.Parse("11111111-1111-1111-1111-111111111111") &&
            r.Permission == "query:execute" &&
            r.DatabaseId.Value == Guid.Parse("44444444-4444-4444-4444-444444444444"));

        Assert.Contains(roles, r =>
            r.UserId.Value == Guid.Parse("11111111-1111-1111-1111-111111111111") &&
            r.Permission == "update:submit" &&
            r.DatabaseId.Value == Guid.Parse("44444444-4444-4444-4444-444444444444"));

        // Verify: scopeable permissions removed from user_permission
        var remainingScopeable = await ctx.UserPermissions
            .CountAsync(p => p.Permission == "query:execute" || p.Permission == "update:submit", ct);
        Assert.Equal(0, remainingScopeable);

        // Verify: non-scopeable permission (permission:manage) is preserved
        var globalPerms = await ctx.UserPermissions
            .CountAsync(p => p.Permission == "permission:manage", ct);
        Assert.Equal(1, globalPerms);
    }

    [Fact]
    public async Task AddUserDatabaseRole_Migration_SeedIsIdempotent_NoConflictOnDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260512003313_Initial", ct);

        // Insert same user with same permission and two databases — expect 2 role rows (one per db)
        await ctx.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""user"" (id, email, name, created_at)
            VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'::uuid, 'u2@example.com', 'U2', NOW());

            INSERT INTO server (id, name, kind, host, port, is_disabled, created_at, updated_at)
            VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'::uuid, 'srv2', 'postgres', 'localhost', 5432, false, NOW(), NOW());

            INSERT INTO server_credential (id, server_id, label, username, encrypted_password, created_at, updated_at)
            VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc'::uuid,
                    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'::uuid,
                    'read', 'pg', 'enc', NOW(), NOW());

            INSERT INTO server_database (id, server_id, display_name, database_name, read_credential_id, is_disabled, created_at, updated_at)
            VALUES
                ('dddddddd-dddd-dddd-dddd-dddddddddddd'::uuid,
                 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'::uuid,
                 'DB1', 'db1', 'cccccccc-cccc-cccc-cccc-cccccccccccc'::uuid, false, NOW(), NOW()),
                ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'::uuid,
                 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'::uuid,
                 'DB2', 'db2', 'cccccccc-cccc-cccc-cccc-cccccccccccc'::uuid, false, NOW(), NOW());

            INSERT INTO user_permission (id, user_id, permission, granted_at)
            VALUES ('ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid,
                    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'::uuid, 'query:execute', NOW());
        ", cancellationToken: ct);

        // Migration should produce 2 rows (1 user × 1 permission × 2 databases) with no error
        await migrator.MigrateAsync(cancellationToken: ct);

        var roles = await ctx.UserDatabaseRoles.ToListAsync(ct);
        Assert.Equal(2, roles.Count);
    }

    [Fact]
    public async Task AddUserDatabaseRole_Migration_SoftDeletedDatabasesExcludedFromSeed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260512003313_Initial", ct);

        await ctx.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""user"" (id, email, name, created_at)
            VALUES ('12121212-1212-1212-1212-121212121212'::uuid, 'u3@example.com', 'U3', NOW());

            INSERT INTO server (id, name, kind, host, port, is_disabled, created_at, updated_at)
            VALUES ('23232323-2323-2323-2323-232323232323'::uuid, 'srv3', 'postgres', 'localhost', 5432, false, NOW(), NOW());

            INSERT INTO server_credential (id, server_id, label, username, encrypted_password, created_at, updated_at)
            VALUES ('34343434-3434-3434-3434-343434343434'::uuid,
                    '23232323-2323-2323-2323-232323232323'::uuid,
                    'read', 'pg', 'enc', NOW(), NOW());

            INSERT INTO server_database (id, server_id, display_name, database_name, read_credential_id, is_disabled, deleted_at, created_at, updated_at)
            VALUES ('45454545-4545-4545-4545-454545454545'::uuid,
                    '23232323-2323-2323-2323-232323232323'::uuid,
                    'Deleted DB', 'deldb',
                    '34343434-3434-3434-3434-343434343434'::uuid,
                    false, NOW(), NOW(), NOW());

            INSERT INTO user_permission (id, user_id, permission, granted_at)
            VALUES ('56565656-5656-5656-5656-565656565656'::uuid,
                    '12121212-1212-1212-1212-121212121212'::uuid, 'query:execute', NOW());
        ", cancellationToken: ct);

        await migrator.MigrateAsync(cancellationToken: ct);

        // Soft-deleted database should not appear in seeded roles
        var roles = await ctx.UserDatabaseRoles.ToListAsync(ct);
        Assert.Empty(roles);
    }
}
