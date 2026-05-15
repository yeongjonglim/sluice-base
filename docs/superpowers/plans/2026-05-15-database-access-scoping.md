# Database Access Scoping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-database access control so admins can assign operational permissions to users on specific databases, replacing the current global permission model for the 5 operational permissions.

**Architecture:** A new `user_database_role` table stores `(user_id, permission, database_id)` triples. The 5 operational permissions (`query:execute`, `query:audit`, `update:submit`, `update:approve`, `update:execute`) are removed from `user_permission` and managed exclusively via this table. Enforcement moves from ASP.NET authorization policies to inline `DbContext` checks in each endpoint. A new Access admin page provides dual-view (by-database and by-user) role management. The `/api/me` endpoint returns distinct permissions from both tables so frontend permission checks continue to work.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, ASP.NET Minimal APIs, Vogen, React/TypeScript, TanStack Router/Query, Mantine

**Spec:** `docs/superpowers/specs/2026-05-15-database-access-scoping-design.md`

---

### Task 1: Create `UserDatabaseRoleId` Vogen value object

**Files:**
- Create: `src/SluiceBase.Core/Permissions/UserDatabaseRoleId.cs`

- [ ] **Step 1: Write the file**

```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct UserDatabaseRoleId;
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build src/SluiceBase.Core
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Permissions/UserDatabaseRoleId.cs
git commit -m "feat: add UserDatabaseRoleId Vogen value object"
```

---

### Task 2: Create `UserDatabaseRole` entity

**Files:**
- Create: `src/SluiceBase.Core/Permissions/UserDatabaseRole.cs`

- [ ] **Step 1: Write the entity**

```csharp
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserDatabaseRole
{
#pragma warning disable CS8618
    private UserDatabaseRole() { }
#pragma warning restore CS8618

    private UserDatabaseRole(
        UserDatabaseRoleId id, UserId userId, string permission,
        DatabaseId databaseId, UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        UserId = userId;
        Permission = permission;
        DatabaseId = databaseId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public UserDatabaseRoleId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string Permission { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserDatabaseRole Grant(
        UserId userId, string permission, DatabaseId databaseId,
        UserId? grantedById, DateTimeOffset at) =>
        new(UserDatabaseRoleId.FromNewVersion7Guid(), userId, permission, databaseId, grantedById, at);
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build src/SluiceBase.Core
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Permissions/UserDatabaseRole.cs
git commit -m "feat: add UserDatabaseRole entity"
```

---

### Task 3: EF configuration and DbContext registration

**Files:**
- Create: `src/SluiceBase.Api/Data/Configurations/UserDatabaseRoleConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Create EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserDatabaseRoleConfiguration : IEntityTypeConfiguration<UserDatabaseRole>
{
    public void Configure(EntityTypeBuilder<UserDatabaseRole> builder)
    {
        builder.ToTable("user_database_role");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.UserId, r.Permission, r.DatabaseId }).IsUnique();
        builder.Property(r => r.GrantedAt).IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Database>()
            .WithMany()
            .HasForeignKey(r => r.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: Add DbSet to AppDbContext**

In `src/SluiceBase.Api/Data/AppDbContext.cs`, add after the existing `UserPermissions` DbSet (line 20):

```csharp
    public DbSet<UserDatabaseRole> UserDatabaseRoles => Set<UserDatabaseRole>();
```

Also add the using at the top:
```csharp
using SluiceBase.Core.Permissions;
```
(already present — no change needed if it is)

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Data/Configurations/UserDatabaseRoleConfiguration.cs
git add src/SluiceBase.Api/Data/AppDbContext.cs
git commit -m "feat: add UserDatabaseRole EF configuration and DbSet"
```

---

### Task 4: Update `Permissions.cs`

**Files:**
- Modify: `src/SluiceBase.Core/Permissions/Permissions.cs`

Changes:
- Update `All` to contain only `PermissionManage` and `ServerManage`
- Add `Scopeable` set with the 5 operational permissions
- Remove `UpdateAny` and `CatalogRead` virtual constants (no longer used as ASP.NET policies; enforcement moves inline)

- [ ] **Step 1: Rewrite the file**

```csharp
namespace SluiceBase.Core.Permissions;

public static class Permissions
{
    public const string PermissionManage = "permission:manage";
    public const string ServerManage = "server:manage";
    public const string QueryExecute = "query:execute";
    public const string QueryAudit = "query:audit";
    public const string UpdateSubmit = "update:submit";
    public const string UpdateApprove = "update:approve";
    public const string UpdateExecute = "update:execute";

    // Global permissions managed in user_permission — grantable from the Permissions admin page.
    public static readonly IReadOnlySet<string> Global = new HashSet<string>
    {
        PermissionManage,
        ServerManage,
    };

    // Operational permissions managed in user_database_role — grantable per database from the Access admin page.
    public static readonly IReadOnlySet<string> Scopeable = new HashSet<string>
    {
        QueryExecute,
        QueryAudit,
        UpdateSubmit,
        UpdateApprove,
        UpdateExecute,
    };
}
```

- [ ] **Step 2: Build — expect compile errors where removed constants were used**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build errors referencing `Permissions.UpdateAny`, `Permissions.CatalogRead` in `UpdateEndpoints.cs`, `CatalogEndpoints.cs`, and `AuthSetup.cs`. These are fixed in later tasks. Note them and proceed.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Permissions/Permissions.cs
git commit -m "feat: update Permissions — reduce All to global perms, add Scopeable set"
```

---

### Task 5: Update `AuthSetup.cs` — remove obsolete policies

**Files:**
- Modify: `src/SluiceBase.Api/Auth/AuthSetup.cs`

Remove:
- The `UpdateAny` policy registration (lines 124-129)
- The `CatalogRead` policy registration (lines 131-138)
- The `foreach (var permission in Permissions.Global)` loop now only registers 2 policies (this is correct as-is after Task 4)

- [ ] **Step 1: Edit AuthSetup.cs**

Replace lines 116-139 (the full `services.AddAuthorization(...)` block) with:

```csharp
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.Global)
            {
                options.AddPolicy(permission,
                    policy => policy.Requirements.Add(new PermissionRequirement(permission)));
            }
        });
```

- [ ] **Step 2: Build (will still have errors in endpoint files)**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Remaining errors are in `CatalogEndpoints.cs` and `UpdateEndpoints.cs` referencing removed constants. These are fixed in Tasks 10 and 12.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Auth/AuthSetup.cs
git commit -m "feat: remove obsolete CatalogRead and UpdateAny authorization policies"
```

---

### Task 6: Create EF migration `AddUserDatabaseRole`

**Files:**
- Create: `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddUserDatabaseRole.cs` (generated by EF)

- [ ] **Step 1: Generate the migration**

```bash
dotnet build src/SluiceBase.Api && \
dotnet ef migrations add AddUserDatabaseRole \
  --project src/SluiceBase.Api \
  --startup-project src/SluiceBase.Api \
  --output-dir Data/Migrations
```

Expected: `Done. To undo this action, use 'ef migrations remove'`
A new file `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddUserDatabaseRole.cs` is created.

- [ ] **Step 2: Add seeding and cleanup SQL to the migration**

Open the generated migration file. In the `Up` method, AFTER the `migrationBuilder.CreateTable("user_database_role", ...)` call and BEFORE any `CreateIndex` calls, add:

```csharp
            // Seed: grant existing users access to all current databases for their existing scopeable permissions
            migrationBuilder.Sql(@"
                INSERT INTO user_database_role (id, user_id, permission, database_id, granted_at, granted_by_id)
                SELECT gen_random_uuid(), up.user_id, up.permission, sd.id, NOW(), NULL
                FROM user_permission up
                CROSS JOIN server_database sd
                WHERE up.permission IN ('query:execute', 'query:audit', 'update:submit', 'update:approve', 'update:execute')
                  AND sd.deleted_at IS NULL
                ON CONFLICT DO NOTHING;
            ");

            // Remove scopeable permissions from user_permission — they are now managed in user_database_role
            migrationBuilder.Sql(@"
                DELETE FROM user_permission
                WHERE permission IN ('query:execute', 'query:audit', 'update:submit', 'update:approve', 'update:execute');
            ");
```

In the `Down` method, BEFORE `migrationBuilder.DropTable("user_database_role")`, add:

```csharp
            // Restore: copy distinct scopeable permissions back to user_permission
            migrationBuilder.Sql(@"
                INSERT INTO user_permission (id, user_id, permission, granted_at, granted_by_id)
                SELECT DISTINCT gen_random_uuid(), user_id, permission, MIN(granted_at), NULL
                FROM user_database_role
                GROUP BY user_id, permission
                ON CONFLICT DO NOTHING;
            ");
```

- [ ] **Step 3: Verify migration applies cleanly**

```bash
dotnet ef database update \
  --project src/SluiceBase.Api \
  --startup-project src/SluiceBase.Api \
  --connection "Host=localhost;Database=sluicebase;Username=postgres;Password=postgres"
```

Expected: `Done.` (or similar success message). If no local DB is available, verify `dotnet build` passes.

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Data/Migrations/
git commit -m "feat: add AddUserDatabaseRole migration with compatibility seeding"
```

---

### Task 7: Test migration seeding SQL with TestContainers

The custom SQL in the `Up` method is outside EF's type safety. This task verifies it produces the correct seeding and cleanup on a real Postgres instance.

**Files:**
- Modify: `tests/IntegrationTests/IntegrationTests.csproj` — add `Testcontainers.PostgreSql`
- Create: `tests/IntegrationTests/MigrationSeedingTests.cs`

- [ ] **Step 1: Add Testcontainers.PostgreSql to the test project**

In `tests/IntegrationTests/IntegrationTests.csproj`, add inside `<ItemGroup>`:

```xml
<PackageReference Include="Testcontainers.PostgreSql" Version="4.4.0" />
```

- [ ] **Step 2: Write the migration seeding test**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SluiceBase.Api.Data;
using Testcontainers.PostgreSql;

namespace IntegrationTests;

public class MigrationSeedingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
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
        // This sets up the full pre-migration schema.
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
```

- [ ] **Step 3: Run the migration tests**

```bash
dotnet test tests/IntegrationTests/ --filter "MigrationSeedingTests" 2>&1 | tail -20
```

Expected: All 3 tests pass. If a test fails, check:
- The migration name `"20260512003313_Initial"` matches the actual initial migration filename in `src/SluiceBase.Api/Data/Migrations/`
- `UseSnakeCaseNamingConvention()` is applied — requires `EFCore.NamingConventions` package which is already in `SluiceBase.Api.csproj`

- [ ] **Step 4: Commit**

```bash
git add tests/IntegrationTests/IntegrationTests.csproj
git add tests/IntegrationTests/MigrationSeedingTests.cs
git commit -m "test: add migration seeding tests using TestContainers"
```

---

### Task 8: Create `DatabaseRoleEndpoints` — write failing tests first

**Files:**
- Create: `tests/IntegrationTests/DatabaseRoleEndpointTests.cs`
- Create: `tests/IntegrationTests/Supports/DatabaseRoleTestHelper.cs`

- [ ] **Step 1: Write the test helper**

```csharp
using System.Net.Http.Json;

namespace IntegrationTests.Supports;

internal static class DatabaseRoleTestHelper
{
    public static async Task<string> AssignByDatabaseAsync(
        AuthenticatedSession adminSession,
        string userId,
        string permission,
        string databaseId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { userId, permission });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<RoleResponse>(ct);
        return body!.Id;
    }

    public static async Task RemoveRoleAsync(
        AuthenticatedSession adminSession,
        string databaseId,
        string userId,
        string permission,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/{userId}/{permission}");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record RoleResponse(string Id);
}
```

- [ ] **Step 2: Write the test file**

```csharp
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class DatabaseRoleEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    // Creates a fresh server + credential + database for tests that need a real database ID.
    // Grants alice server:manage temporarily. Returns (session, xsrf, databaseId).
    private async Task<(AuthenticatedSession Session, string Xsrf, string DatabaseId)>
        AliceWithTestDatabaseAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"role-{Guid.NewGuid():N}"[..20];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new { name = serverName, kind = "postgres", host = blueBuilder.Host, port = blueBuilder.Port });
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerBody>(ct))!;

        using var cReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new { label = "read", username = blueBuilder.Username, password = blueBuilder.Password });
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialBody>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new { displayName = "test-db", databaseName = blueBuilder.Database ?? "postgres", readCredentialId = cred.Id });
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseBody>(ct))!;

        return (session, xsrf, db.Id);
    }

    // ── anonymous / unauthorized ──────────────────────────────────────────────

    [Fact]
    public async Task ListByDatabase_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/role",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListByDatabase_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/role",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── list by database ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListByDatabase_NonExistentDatabase_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var resp = await session.Client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/role", ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<RoleListBody>(ct);
        Assert.NotNull(body);
        Assert.Empty(body.Roles);
    }

    // ── assign by database (POST /api/admin/database/{id}/role) ──────────────

    [Fact]
    public async Task AssignByDatabase_HappyPath_Returns201AndAppearsInList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        // ensure bob user row exists
        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = bobUser.Id, permission = Permissions.QueryExecute });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var list = await alice.Client.GetFromJsonAsync<RoleListBody>(
            $"/api/admin/database/{databaseId}/role", ct);
        Assert.Contains(list!.Roles, r => r.UserId == bobUser.Id && r.Permission == Permissions.QueryExecute);

        await DatabaseRoleTestHelper.RemoveRoleAsync(alice, databaseId, bobUser.Id, Permissions.QueryExecute, xsrf, ct);
    }

    [Fact]
    public async Task AssignByDatabase_Duplicate_Returns200()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req1 = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.QueryExecute });
        var resp1 = await alice.Client.SendAsync(req1, ct);
        Assert.True(resp1.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);

        using var req2 = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.QueryExecute });
        var resp2 = await alice.Client.SendAsync(req2, ct);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task AssignByDatabase_UnknownPermission_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = "not:real" });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task AssignByDatabase_NonScopeablePermission_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.ServerManage });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── remove role ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRole_HappyPath_Returns204AndDisappears()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var assignReq = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.UpdateSubmit });
        (await alice.Client.SendAsync(assignReq, ct)).EnsureSuccessStatusCode();

        using var removeReq = MutationRequest(
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/{aliceUser.Id}/{Permissions.UpdateSubmit}", xsrf);
        var removeResp = await alice.Client.SendAsync(removeReq, ct);
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var list = await alice.Client.GetFromJsonAsync<RoleListBody>(
            $"/api/admin/database/{databaseId}/role", ct);
        Assert.DoesNotContain(list!.Roles,
            r => r.UserId == aliceUser.Id && r.Permission == Permissions.UpdateSubmit);
    }

    [Fact]
    public async Task RemoveRole_Idempotent_Returns204WhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await alice.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/database/{Guid.NewGuid()}/role/{Guid.NewGuid()}/{Permissions.QueryExecute}",
            xsrf);
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // ── list by user ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListByUser_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/admin/user/{Guid.NewGuid()}/role",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListByUser_HappyPath_ReturnsAssignedRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var assignReq = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.QueryAudit });
        (await alice.Client.SendAsync(assignReq, ct)).EnsureSuccessStatusCode();

        var list = await alice.Client.GetFromJsonAsync<UserRoleListBody>(
            $"/api/admin/user/{aliceUser.Id}/role", ct);
        Assert.Contains(list!.Roles, r => r.DatabaseId == databaseId && r.Permission == Permissions.QueryAudit);

        await DatabaseRoleTestHelper.RemoveRoleAsync(alice, databaseId, aliceUser.Id, Permissions.QueryAudit, xsrf, ct);
    }

    // ── assign by user (POST /api/admin/user/{id}/role) ──────────────────────

    [Fact]
    public async Task AssignByUser_HappyPath_Returns201()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/user/{aliceUser.Id}/role", xsrf,
            new { databaseId, permission = Permissions.UpdateApprove });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.True(resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);

        await DatabaseRoleTestHelper.RemoveRoleAsync(alice, databaseId, aliceUser.Id, Permissions.UpdateApprove, xsrf, ct);
    }

    // ── admin server list ─────────────────────────────────────────────────────

    [Fact]
    public async Task AdminServerList_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/admin/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AdminServerList_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync("/api/admin/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AdminServerList_Alice_ReturnsServers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await LoginHelper.SignInAsync("alice", "dev", ct);
        var resp = await alice.Client.GetAsync("/api/admin/server", ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AdminServerListBody>(ct);
        Assert.NotNull(body);
    }

    // ── response records ──────────────────────────────────────────────────────

    private sealed record RoleItem(string Id, string UserId, string Permission);
    private sealed record RoleListBody(RoleItem[] Roles);
    private sealed record UserRoleItem(string DatabaseId, string Permission);
    private sealed record UserRoleListBody(UserRoleItem[] Roles);
    private sealed record UserRow(string Id, string Email);
    private sealed record ListUserBody(UserRow[] Users);
    private sealed record AdminDbItem(string Id, string DisplayName);
    private sealed record AdminServerItem(string Id, string Name, AdminDbItem[] Databases);
    private sealed record AdminServerListBody(AdminServerItem[] Servers);

    // Used by AliceWithTestDatabaseAsync helper
    private sealed record ServerBody(string Id, string Name);
    private sealed record CredentialBody(string Id, string Label);
    private sealed record DatabaseBody(string Id, string DisplayName);
}
```

- [ ] **Step 3: Run tests — verify they all fail**

```bash
dotnet test tests/IntegrationTests/ --filter "DatabaseRoleEndpointTests" 2>&1 | tail -20
```

Expected: All tests FAIL with 404 or compilation errors. This confirms the tests are real.

- [ ] **Step 4: Commit failing tests**

```bash
git add tests/IntegrationTests/DatabaseRoleEndpointTests.cs
git add tests/IntegrationTests/Supports/DatabaseRoleTestHelper.cs
git commit -m "test: add failing DatabaseRoleEndpoint integration tests"
```

---

### Task 8: Implement `DatabaseRoleEndpoints`

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`

> **Note on `/api/admin` group:** `PermissionEndpoints.cs` already calls `app.MapGroup("/api/admin")`. Calling it again in `DatabaseRoleEndpoints` is valid — ASP.NET Minimal APIs creates a new independent `RouteGroupBuilder` per call. The routes in each group use distinct paths (`/user`, `/user/{userId}/permission` in `PermissionEndpoints`; `/server`, `/database/{id}/role`, `/user/{userId}/role` in `DatabaseRoleEndpoints`) and distinct `WithName(...)` values, so there are no conflicts.

- [ ] **Step 1: Create the endpoint file**

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class DatabaseRoleEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/server", ListServers).WithName("AdminListServers");

        admin.MapGet("/database/{databaseId}/role", ListByDatabase)
            .WithName("ListDatabaseRoles");
        admin.MapPost("/database/{databaseId}/role", AssignByDatabase)
            .WithName("AssignDatabaseRole");
        admin.MapDelete("/database/{databaseId}/role/{userId}/{permission}", RemoveRole)
            .WithName("RemoveDatabaseRole");

        admin.MapGet("/user/{userId}/role", ListByUser)
            .WithName("ListUserRoles");
        admin.MapPost("/user/{userId}/role", AssignByUser)
            .WithName("AssignUserRole");
    }

    // ── admin server list ─────────────────────────────────────────────────────

    private static async Task<Ok<AdminServerListResponse>> ListServers(
        AppDbContext db, CancellationToken ct)
    {
        var servers = await db.Servers
            .AsNoTracking()
            .Where(s => s.DeletedAt == null)
            .Include(s => s.Databases.Where(d => d.DeletedAt == null))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return TypedResults.Ok(new AdminServerListResponse([
            .. servers.Select(s => new AdminServerItem(
                s.Id,
                s.Name,
                s.IsDisabled,
                [.. s.Databases
                    .Select(d => new AdminDatabaseItem(d.Id, d.DisplayName, d.IsDisabled))
                    .OrderBy(d => d.DisplayName)]))
        ]));
    }

    // ── list by database ──────────────────────────────────────────────────────

    private static async Task<Ok<DatabaseRoleListResponse>> ListByDatabase(
        DatabaseId databaseId, AppDbContext db, CancellationToken ct)
    {
        var roles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.DatabaseId == databaseId)
            .Join(db.ExternalLogins,
                r => r.UserId,
                l => l.UserId,
                (r, l) => new DatabaseRoleItem(r.Id, r.UserId, l.Email, l.Name, r.Permission, r.GrantedAt, r.GrantedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new DatabaseRoleListResponse(roles));
    }

    // ── assign by database ────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> AssignByDatabase(
        DatabaseId databaseId,
        AssignDatabaseRoleRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Scopeable.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a scopeable permission."]
            });
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var dbExists = await db.Databases.AnyAsync(d => d.Id == databaseId, ct);
        if (!dbExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == req.UserId && r.Permission == req.Permission && r.DatabaseId == databaseId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(
            req.UserId, req.Permission, databaseId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/database/{databaseId}/role");
    }

    // ── remove role ───────────────────────────────────────────────────────────

    private static async Task<NoContent> RemoveRole(
        DatabaseId databaseId,
        UserId userId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var role = await db.UserDatabaseRoles.SingleOrDefaultAsync(
            r => r.DatabaseId == databaseId && r.UserId == userId && r.Permission == permission, ct);
        if (role is not null)
        {
            db.UserDatabaseRoles.Remove(role);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── list by user ──────────────────────────────────────────────────────────

    private static async Task<Ok<UserRoleListResponse>> ListByUser(
        UserId userId, AppDbContext db, CancellationToken ct)
    {
        var roles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Join(db.Databases,
                r => r.DatabaseId,
                d => d,
                (r, d) => new UserRoleItem(r.Id, r.DatabaseId, d.DisplayName, d.Server!.Name, r.Permission, r.GrantedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new UserRoleListResponse(roles));
    }

    // ── assign by user ────────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> AssignByUser(
        UserId userId,
        AssignUserRoleRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Scopeable.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a scopeable permission."]
            });
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == userId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var dbExists = await db.Databases.AnyAsync(d => d.Id == req.DatabaseId, ct);
        if (!dbExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == userId && r.Permission == req.Permission && r.DatabaseId == req.DatabaseId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(
            userId, req.Permission, req.DatabaseId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/user/{userId}/role");
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record AssignDatabaseRoleRequest(UserId UserId, string Permission);
    public sealed record AssignUserRoleRequest(DatabaseId DatabaseId, string Permission);

    public sealed record DatabaseRoleItem(
        UserDatabaseRoleId Id,
        UserId UserId,
        string? UserEmail,
        string? UserName,
        string Permission,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record DatabaseRoleListResponse(IReadOnlyList<DatabaseRoleItem> Roles);

    public sealed record UserRoleItem(
        UserDatabaseRoleId Id,
        DatabaseId DatabaseId,
        string DatabaseDisplayName,
        string ServerName,
        string Permission,
        DateTimeOffset GrantedAt);

    public sealed record UserRoleListResponse(IReadOnlyList<UserRoleItem> Roles);

    public sealed record AdminDatabaseItem(DatabaseId Id, string DisplayName, bool IsDisabled);

    public sealed record AdminServerItem(
        ServerId Id,
        string Name,
        bool IsDisabled,
        IReadOnlyList<AdminDatabaseItem> Databases);

    public sealed record AdminServerListResponse(IReadOnlyList<AdminServerItem> Servers);
}
```

Note: The `ListByUser` join on `d.Server!.Name` requires the Server navigation to be loaded. Update the query:

```csharp
    private static async Task<Ok<UserRoleListResponse>> ListByUser(
        UserId userId, AppDbContext db, CancellationToken ct)
    {
        var roles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new UserRoleItem(
                r.Id,
                r.DatabaseId,
                db.Databases.Where(d => d.Id == r.DatabaseId).Select(d => d.DisplayName).FirstOrDefault() ?? "",
                db.Databases.Where(d => d.Id == r.DatabaseId)
                    .Select(d => db.Servers.Where(s => s.Id == d.ServerId).Select(s => s.Name).FirstOrDefault())
                    .FirstOrDefault() ?? "",
                r.Permission,
                r.GrantedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new UserRoleListResponse(roles));
    }
```

- [ ] **Step 2: Register in EndpointMapper**

In `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`, add after `PermissionEndpoints.Map(app);`:

```csharp
        DatabaseRoleEndpoints.Map(app);
```

- [ ] **Step 3: Build**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the new tests**

```bash
dotnet test tests/IntegrationTests/ --filter "DatabaseRoleEndpointTests" 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs
git add src/SluiceBase.Api/Endpoints/EndpointMapper.cs
git commit -m "feat: add DatabaseRoleEndpoints for per-database access control management"
```

---

### Task 9: Update `/api/me` to include database role permissions

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs`

The frontend `useHasPermission("query:execute")` checks `me.permissions`. After this change, users won't have operational permissions in `user_permission` — they come from `user_database_role`. Update `/api/me` to include distinct permissions from both tables.

- [ ] **Step 1: Update the `/api/me` handler in AuthEndpoints.cs**

Replace the `/api/me` handler (lines 38-54) with:

```csharp
        app.MapGet("/api/me",
                async Task<Results<UnauthorizedHttpResult, Ok<MeResponse>>> (
                    ICurrentUserAccessor currentUser,
                    AppDbContext db,
                    CancellationToken ct) =>
                {
                    var user = await currentUser.GetAsync(ct);
                    if (user is null)
                    {
                        return TypedResults.Unauthorized();
                    }

                    var databaseRolePermissions = await db.UserDatabaseRoles
                        .AsNoTracking()
                        .Where(r => r.UserId == user.Id)
                        .Select(r => r.Permission)
                        .Distinct()
                        .ToListAsync(ct);

                    var allPermissions = user.Permissions.Select(p => p.Permission)
                        .Concat(databaseRolePermissions)
                        .Distinct()
                        .ToArray();

                    return TypedResults.Ok(new MeResponse(
                        Id: user.Id,
                        Email: user.Email,
                        Name: user.Name,
                        Permissions: allPermissions));
                })
            .WithName("Me")
            .RequireAuthorization();
```

Also add the missing using at the top of the file:

```csharp
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/AuthEndpoints.cs
git commit -m "feat: include database role permissions in /api/me response"
```

---

### Task 10: Update `CatalogEndpoints` — inline role filtering

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs`
- Modify: `tests/IntegrationTests/CatalogEndpointTests.cs`

- [ ] **Step 1: Update the catalog test expectations first**

Read `tests/IntegrationTests/CatalogEndpointTests.cs` and update the test setup. Tests that previously granted `query:execute` as a permission now need to assign a database role instead. Replace any `grant query:execute` calls with a `DatabaseRoleTestHelper.AssignByDatabaseAsync(...)` call using the actual database ID from the test server.

Look in `CatalogEndpointTests.cs` for the test setup helper methods and update them. The pattern is: instead of:

```csharp
// Before: grant global permission
using var grantReq = MutationRequest(HttpMethod.Post,
    $"/api/admin/user/{userId}/permission", xsrf,
    new { permission = Permissions.QueryExecute });
await session.Client.SendAsync(grantReq, ct);
```

Use:

```csharp
// After: assign database role
await DatabaseRoleTestHelper.AssignByDatabaseAsync(
    session, userId, Permissions.QueryExecute, databaseId.ToString(), xsrf, ct);
```

Also update any test assertions that verify catalog returns data (the catalog now only returns databases the user has a role on).

- [ ] **Step 2: Update CatalogEndpoints.cs**

Replace the full file with:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalog/server", ListServers)
            .RequireAuthorization()
            .WithName("CatalogListServers");
    }

    private static async Task<Ok<CatalogServersResponse>> ListServers(
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var isServerAdmin = user?.HasPermission(Permissions.ServerManage) ?? false;

        IQueryable<Database> dbQuery = db.Databases
            .AsNoTracking()
            .Where(d => d.DeletedAt == null && !d.IsDisabled);

        if (!isServerAdmin)
        {
            var allowedDatabaseIds = db.UserDatabaseRoles
                .Where(r => r.UserId == user!.Id)
                .Select(r => r.DatabaseId);

            dbQuery = dbQuery.Where(d => allowedDatabaseIds.Contains(d.Id));
        }

        var databases = await dbQuery
            .Include(d => d.Server)
            .ToListAsync(ct);

        var servers = databases
            .Where(d => d.Server != null && d.Server.DeletedAt == null && !d.Server.IsDisabled)
            .GroupBy(d => d.Server!)
            .OrderBy(g => g.Key.Name)
            .Select(g => new CatalogServerItem(
                g.Key.Id,
                g.Key.Name,
                [.. g.Select(d => new CatalogDatabaseItem(d.Id, d.DisplayName, d.CanWrite))
                     .OrderBy(d => d.DisplayName)]))
            .ToList();

        return TypedResults.Ok(new CatalogServersResponse(servers));
    }

    public sealed record CatalogServersResponse(IReadOnlyList<CatalogServerItem> Servers);

    public sealed record CatalogServerItem(
        ServerId Id,
        string Name,
        IReadOnlyList<CatalogDatabaseItem> Databases);

    public sealed record CatalogDatabaseItem(
        DatabaseId Id,
        string DisplayName,
        bool CanWrite);
}
```

Note: the `Database` entity needs a `Server` navigation property. Check `DatabaseConfiguration.cs` — if no navigation exists, add a lazy-loaded navigation to `Database.cs` in `SluiceBase.Core/Servers/Database.cs`. It likely already exists (referenced as `d.Server!.Name` in the server endpoint tests).

- [ ] **Step 3: Build**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run catalog tests**

```bash
dotnet test tests/IntegrationTests/ --filter "CatalogEndpointTests" 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs
git add tests/IntegrationTests/CatalogEndpointTests.cs
git commit -m "feat: update CatalogEndpoints to filter by user database roles"
```

---

### Task 11: Update `QueryEndpoints` — inline role enforcement

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Modify: `tests/IntegrationTests/QueryEndpointTest.cs`
- Modify: `tests/IntegrationTests/QueryHistoryEndpointTests.cs`

- [ ] **Step 1: Update query endpoint tests**

In `QueryEndpointTest.cs`, find the `AuthorizedSessionWithBlueServerAsync` helper (around line 29). It currently grants `query:execute` as a global permission. Update the step that grants `query:execute` to instead assign a database role on the newly-created test database.

Replace the grant step:
```csharp
// Before — remove these lines:
using var grantQuery = MutationRequest(HttpMethod.Post,
    $"/api/admin/user/{alice.Id}/permission", xsrf,
    new { permission = Permissions.QueryExecute });
(await session.Client.SendAsync(grantQuery, ct)).EnsureSuccessStatusCode();
```

With (after the database is created and `databaseId` is known):
```csharp
// After — assign database role:
await DatabaseRoleTestHelper.AssignByDatabaseAsync(
    session, alice.Id, Permissions.QueryExecute, databaseId.ToString(), xsrf, ct);
```

Make the same update in `QueryHistoryEndpointTests.cs` for any test setup that grants `query:execute` or `query:audit`.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/IntegrationTests/ --filter "QueryEndpointTests|QueryHistoryEndpointTests" 2>&1 | tail -20
```

Expected: Tests fail because the endpoint still checks the old `query:execute` authorization policy.

- [ ] **Step 3: Update QueryEndpoints.cs**

Replace the `Map` method route registrations:

```csharp
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", ExecuteQuery)
            .RequireAuthorization()
            .WithName("ExecuteQuery");

        app.MapGet("/api/query/history", GetHistory)
            .RequireAuthorization()
            .WithName("GetQueryHistory");
    }
```

In `ExecuteQuery`, after the database null check (after `if (database is null) return TypedResults.NotFound();`), add:

```csharp
        // Enforce database role: user must have query:execute on this specific database
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == database.Id, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
        }
```

Also update the return type signature to include `ForbidHttpResult`:
```csharp
    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>, ForbidHttpResult>> ExecuteQuery(
```

In `GetHistory`, replace the `hasAudit` filter logic:

```csharp
        var user = await currentUser.GetAsync(ct);

        // databases where user has query:audit (can see all queries)
        var auditDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id && r.Permission == Permissions.QueryAudit)
            .Select(r => r.DatabaseId)
            .ToListAsync(ct);

        // databases where user has any role (can see own queries)
        var anyRoleDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id)
            .Select(r => r.DatabaseId)
            .Distinct()
            .ToListAsync(ct);

        var items = await db.QueryLogs
            .AsNoTracking()
            .Where(q => (auditDatabaseIds.Contains(q.DatabaseId!.Value)) ||
                        (anyRoleDatabaseIds.Contains(q.DatabaseId!.Value) && q.UserId == user!.Id))
            ...
```

Remove the old `var hasAudit = user?.HasPermission(Permissions.QueryAudit) ?? false;` and `.Where(q => hasAudit || q.UserId == user!.Id)` lines.

- [ ] **Step 4: Build**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/IntegrationTests/ --filter "QueryEndpointTests|QueryHistoryEndpointTests" 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/QueryEndpoints.cs
git add tests/IntegrationTests/QueryEndpointTest.cs
git add tests/IntegrationTests/QueryHistoryEndpointTests.cs
git commit -m "feat: enforce query:execute and query:audit database roles in QueryEndpoints"
```

---

### Task 12: Update `UpdateEndpoints` — inline role enforcement

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`
- Modify: `tests/IntegrationTests/UpdateEndpointTests.cs`

- [ ] **Step 1: Update update endpoint tests**

In `UpdateEndpointTests.cs`, find all test setup steps that grant `update:submit`, `update:approve`, or `update:execute` as global permissions. Replace each grant with a database role assignment using `DatabaseRoleTestHelper.AssignByDatabaseAsync(...)` on the relevant test database.

Pattern to replace:
```csharp
// Before:
using var grantReq = MutationRequest(HttpMethod.Post,
    $"/api/admin/user/{userId}/permission", xsrf,
    new { permission = Permissions.UpdateSubmit });
await session.Client.SendAsync(grantReq, ct);
```

Replace with:
```csharp
// After:
await DatabaseRoleTestHelper.AssignByDatabaseAsync(
    session, userId, Permissions.UpdateSubmit, databaseId.ToString(), xsrf, ct);
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/IntegrationTests/ --filter "UpdateEndpointTests" 2>&1 | tail -20
```

Expected: Tests fail.

- [ ] **Step 3: Update UpdateEndpoints.cs — route registrations**

Replace the `Map` method with:

```csharp
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/update").RequireAuthorization();

        group.MapPost("/", Submit).WithName("SubmitUpdate");
        group.MapGet("/", List).WithName("ListUpdates");
        group.MapGet("/{id}", Get).WithName("GetUpdate");
        group.MapPost("/{id}/approve", Approve).WithName("ApproveUpdate");
        group.MapPost("/{id}/reject", Reject).WithName("RejectUpdate");
        group.MapPost("/{id}/cancel", Cancel).WithName("CancelUpdate");
        group.MapPost("/{id}/execute", Execute).WithName("ExecuteUpdate");
    }
```

- [ ] **Step 4: Update Submit — add role check**

In `Submit`, after `if (database is null) return TypedResults.NotFound();`, add:

```csharp
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.UpdateSubmit && r.DatabaseId == database.Id, ct);
        if (!hasRole)
        {
            return TypedResults.Unauthorized();
        }
```

Update the return type to include `UnauthorizedHttpResult` (already present). Replace the existing early-return unauthorized check pattern.

- [ ] **Step 5: Update List — filter by database roles**

In `List`, add `ICurrentUserAccessor currentUser` parameter and replace the entire handler body with:

```csharp
    private static async Task<Ok<ListUpdateRequestsResponse>> List(
        DateTimeOffset? @from,
        DateTimeOffset? to,
        string? databaseId,
        string? status,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        var allowedDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id && (
                r.Permission == Permissions.UpdateSubmit ||
                r.Permission == Permissions.UpdateApprove ||
                r.Permission == Permissions.UpdateExecute))
            .Select(r => r.DatabaseId)
            .Distinct()
            .ToListAsync(ct);

        DatabaseId? filterDb = databaseId is not null && Guid.TryParse(databaseId, out var dbGuid)
            ? DatabaseId.From(dbGuid)
            : null;

        UpdateRequestStatus? filterStatus = status is not null
            && Enum.TryParse<UpdateRequestStatus>(status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : null;

        var requests = await db.UpdateRequests
            .Include(r => r.Database)
            .Include(r => r.Submitter)
            .AsNoTracking()
            .Where(r => allowedDatabaseIds.Contains(r.DatabaseId!.Value))
            .Where(r => @from == null || r.SubmittedAt >= @from)
            .Where(r => to == null || r.SubmittedAt <= to)
            .Where(r => filterDb == null || r.DatabaseId == filterDb)
            .Where(r => filterStatus == null || r.Status == filterStatus)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

        var items = requests
            .Select(r => new UpdateSummaryItem(
                r.Id,
                r.Database?.DisplayName,
                r.Submitter?.Name ?? r.Submitter?.Email,
                r.Reason,
                r.Status,
                r.SubmittedAt,
                r.ExecSuccess))
            .ToList();

        return TypedResults.Ok(new ListUpdateRequestsResponse(items));
    }
```

- [ ] **Step 6: Update Get — inline role check**

In `Get`, after loading the request (`if (request is null) return TypedResults.NotFound();`), add:

```csharp
        var user = await currentUser.GetAsync(ct);
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.DatabaseId == request.DatabaseId &&
                 (r.Permission == Permissions.UpdateSubmit ||
                  r.Permission == Permissions.UpdateApprove ||
                  r.Permission == Permissions.UpdateExecute), ct);
        if (!hasRole)
        {
            return TypedResults.NotFound();
        }
```

Add `ICurrentUserAccessor currentUser` and `AppDbContext db` parameters to `Get`.

- [ ] **Step 7: Update Approve and Reject — add role check**

In `Approve` and `Reject`, after loading the request, add:

```csharp
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.UpdateApprove && r.DatabaseId == request.DatabaseId, ct);
        if (!hasRole)
        {
            return TypedResults.Unauthorized();
        }
```

- [ ] **Step 8: Update Execute — add role check**

In `Execute`, after loading the request, add:

```csharp
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.UpdateExecute && r.DatabaseId == request.DatabaseId, ct);
        if (!hasRole)
        {
            return TypedResults.Unauthorized();
        }
```

- [ ] **Step 9: Update Cancel — add role check**

In `Cancel`, after loading the request, add:

```csharp
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.UpdateSubmit && r.DatabaseId == request.DatabaseId, ct);
        if (!hasRole)
        {
            return TypedResults.Unauthorized();
        }
```

- [ ] **Step 10: Build**

```bash
dotnet build src/SluiceBase.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 11: Run tests**

```bash
dotnet test tests/IntegrationTests/ --filter "UpdateEndpointTests" 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 12: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs
git add tests/IntegrationTests/UpdateEndpointTests.cs
git commit -m "feat: enforce update database roles in UpdateEndpoints"
```

---

### Task 13: Update remaining tests and verify full test suite

**Files:**
- Modify: `tests/IntegrationTests/PermissionCatalogTests.cs`
- Modify: `tests/IntegrationTests/Supports/PermissionTestHelper.cs`

The `PermissionCatalog` test currently asserts all 7 permissions are present. After this change, the catalog only returns 2 global permissions. Update this test.

- [ ] **Step 1: Update PermissionCatalogTests.cs**

Find the test that asserts 7 permissions and update it to expect only `permission:manage` and `server:manage`:

```csharp
    [Fact]
    public async Task GetCatalog_Authenticated_ReturnsOnlyGlobalPermissions()
    {
        // ... setup ...
        var body = await response.Content.ReadFromJsonAsync<CatalogBody>(ct);
        Assert.NotNull(body);
        Assert.Equal(2, body.Permissions.Length);
        Assert.Contains("permission:manage", body.Permissions);
        Assert.Contains("server:manage", body.Permissions);
    }
```

- [ ] **Step 2: Update PermissionTestHelper.cs**

The `RevokeAllPermissionsAsync` helper iterates over `Permissions.Global`. After the change it only covers the 2 global permissions, which is correct. No change needed.

However, add a helper to revoke all database roles for a user (useful for cleanup in tests):

```csharp
    public static async Task RevokeAllDatabaseRolesAsync(
        AuthenticatedSession adminSession,
        string userId,
        string xsrf,
        CancellationToken ct)
    {
        var roles = await adminSession.Client.GetFromJsonAsync<UserRoleBody>(
            $"/api/admin/user/{userId}/role", ct);
        if (roles is null) return;

        foreach (var role in roles.Roles)
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/admin/database/{role.DatabaseId}/role/{userId}/{role.Permission}");
            req.Headers.Add("X-XSRF-TOKEN", xsrf);
            await adminSession.Client.SendAsync(req, ct);
        }
    }

    private sealed record UserRoleItem(string DatabaseId, string Permission);
    private sealed record UserRoleBody(UserRoleItem[] Roles);
```

- [ ] **Step 3: Run the full integration test suite**

```bash
dotnet test tests/IntegrationTests/ 2>&1 | tail -30
```

Expected: All tests pass. Investigate and fix any remaining failures before proceeding.

- [ ] **Step 4: Commit**

```bash
git add tests/IntegrationTests/PermissionCatalogTests.cs
git add tests/IntegrationTests/Supports/PermissionTestHelper.cs
git commit -m "test: update PermissionCatalog test and PermissionTestHelper for new access model"
```

---

### Task 14: Regenerate OpenAPI schema and frontend TypeScript types

**Files:**
- Regenerated: `src/SluiceBase.Api/openapi.json`
- Regenerated: `src/frontend/src/api/schema.ts`

- [ ] **Step 1: Build API to regenerate openapi.json**

```bash
dotnet build src/SluiceBase.Api
```

Expected: `src/SluiceBase.Api/openapi.json` is updated with new endpoints (`/api/admin/database/{databaseId}/role`, `/api/admin/user/{userId}/role`, `/api/admin/server`).

- [ ] **Step 2: Regenerate frontend TypeScript types**

```bash
cd src/frontend && npm run gen:api && cd ../..
```

Expected: `src/frontend/src/api/schema.ts` is updated. No TypeScript errors.

- [ ] **Step 3: Verify no frontend type errors**

```bash
cd src/frontend && npx tsc --noEmit && cd ../..
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/openapi.json
git add src/frontend/src/api/schema.ts
git commit -m "chore: regenerate OpenAPI schema with database role endpoints"
```

---

### Task 15: Add frontend API hooks for database roles

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Add the hooks at the end of hooks.ts**

```typescript
// ── Database role management ───────────────────────────────────────────────

export type AdminServerListResponse =
  paths["/api/admin/server"]["get"]["responses"][200]["content"]["application/json"];
export type AdminServerItem = AdminServerListResponse["servers"][0];
export type AdminDatabaseItem = AdminServerItem["databases"][0];

export type DatabaseRoleListResponse =
  paths["/api/admin/database/{databaseId}/role"]["get"]["responses"][200]["content"]["application/json"];
export type DatabaseRoleItem = DatabaseRoleListResponse["roles"][0];

export type UserRoleListResponse =
  paths["/api/admin/user/{userId}/role"]["get"]["responses"][200]["content"]["application/json"];
export type UserRoleItem = UserRoleListResponse["roles"][0];

export function useAdminServers() {
  return useQuery({
    queryKey: ["admin", "server"] as const,
    queryFn: () =>
      apiRequest<void, AdminServerListResponse>("/api/admin/server"),
  });
}

export function useDatabaseRoles(databaseId: string | null) {
  return useQuery({
    queryKey: ["admin", "database", databaseId, "role"] as const,
    queryFn: () =>
      apiRequest<void, DatabaseRoleListResponse>(
        `/api/admin/database/${databaseId}/role`,
      ),
    enabled: databaseId !== null,
  });
}

export function useUserRoles(userId: string | null) {
  return useQuery({
    queryKey: ["admin", "user", userId, "role"] as const,
    queryFn: () =>
      apiRequest<void, UserRoleListResponse>(`/api/admin/user/${userId}/role`),
    enabled: userId !== null,
  });
}

export function useAssignDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      userId,
      permission,
    }: {
      databaseId: string;
      userId: string;
      permission: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/role"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/database/${databaseId}/role`, {
        method: "POST",
        body: { userId, permission },
      }),
    onSuccess: (_data, { databaseId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      notifications.show({ title: "Role assigned", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Assignment failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useAssignUserRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      userId,
      databaseId,
      permission,
    }: {
      userId: string;
      databaseId: string;
      permission: string;
    }) =>
      apiRequest<
        paths["/api/admin/user/{userId}/role"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/user/${userId}/role`, {
        method: "POST",
        body: { databaseId, permission },
      }),
    onSuccess: (_data, { userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
      notifications.show({ title: "Role assigned", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Assignment failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      userId,
      permission,
    }: {
      databaseId: string;
      userId: string;
      permission: string;
    }) =>
      apiRequest(
        `/api/admin/database/${databaseId}/role/${userId}/${permission}`,
        { method: "DELETE" },
      ),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
      notifications.show({ title: "Role removed", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Removal failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd src/frontend && npx tsc --noEmit && cd ../..
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/api/hooks.ts
git commit -m "feat: add frontend hooks for database role management"
```

---

### Task 16: Create frontend `access.tsx` route

**Files:**
- Create: `src/frontend/src/routes/_authed/access.tsx`

- [ ] **Step 1: Create the file**

```tsx
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconDatabase, IconPlus, IconTrash, IconUser } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import {
  meQueryOptions,
  useAdminServers,
  useAssignDatabaseRole,
  useAssignUserRole,
  useDatabaseRoles,
  useRemoveDatabaseRole,
  useUserRoles,
  useUsers,
  type AdminDatabaseItem,
  type AdminServerItem,
} from "@/api/hooks";

const SCOPEABLE_PERMISSIONS = [
  { value: "query:execute", label: "Query Execute" },
  { value: "query:audit", label: "Query Audit" },
  { value: "update:submit", label: "Update Submit" },
  { value: "update:approve", label: "Update Approve" },
  { value: "update:execute", label: "Update Execute" },
];

export const Route = createFileRoute("/_authed/access")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: AccessPage,
});

function AccessPage() {
  return (
    <Stack gap="md">
      <Title order={2}>Access control</Title>
      <Tabs defaultValue="database">
        <Tabs.List>
          <Tabs.Tab value="database" leftSection={<IconDatabase size={14} />}>
            By Database
          </Tabs.Tab>
          <Tabs.Tab value="user" leftSection={<IconUser size={14} />}>
            By User
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="database" pt="md">
          <ByDatabaseTab />
        </Tabs.Panel>
        <Tabs.Panel value="user" pt="md">
          <ByUserTab />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}

// ── By Database tab ───────────────────────────────────────────────────────────

function ByDatabaseTab() {
  const servers = useAdminServers();
  const [selectedDb, setSelectedDb] = useState<AdminDatabaseItem & { serverName: string } | null>(null);

  const allDatabases = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases.map((d) => ({ ...d, serverName: s.name, serverDisabled: s.isDisabled })),
  );

  return (
    <Group align="flex-start" gap="md">
      <Stack gap={4} style={{ minWidth: 220, maxWidth: 260 }}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">
          Databases
        </Text>
        {(servers.data?.servers ?? []).map((s) => (
          <Stack key={s.id} gap={2}>
            <Text size="xs" c="dimmed" fw={500} pl={4}>
              {s.name}
              {s.isDisabled && (
                <Badge size="xs" color="gray" ml={4}>
                  disabled
                </Badge>
              )}
            </Text>
            {s.databases.map((d) => (
              <Button
                key={d.id}
                variant={selectedDb?.id === d.id ? "filled" : "subtle"}
                size="xs"
                justify="left"
                leftSection={<IconDatabase size={12} />}
                onClick={() => setSelectedDb({ ...d, serverName: s.name })}
                disabled={d.isDisabled}
                style={{ opacity: d.isDisabled ? 0.5 : 1 }}
              >
                {d.displayName}
              </Button>
            ))}
          </Stack>
        ))}
      </Stack>

      <Stack flex={1} gap="md">
        {selectedDb ? (
          <DatabaseRolePanel database={selectedDb} />
        ) : (
          <Text c="dimmed" size="sm">
            Select a database to manage its access assignments.
          </Text>
        )}
      </Stack>
    </Group>
  );
}

function DatabaseRolePanel({
  database,
}: {
  database: AdminDatabaseItem & { serverName: string };
}) {
  const roles = useDatabaseRoles(database.id);
  const users = useUsers();
  const remove = useRemoveDatabaseRole();
  const [addOpen, { open: openAdd, close: closeAdd }] = useDisclosure(false);
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [selectedPermission, setSelectedPermission] = useState<string | null>(null);
  const assign = useAssignDatabaseRole();

  const userOptions = (users.data?.users ?? []).map((u) => ({
    value: u.id,
    label: u.email ?? u.name ?? u.id,
  }));

  function handleAdd() {
    if (!selectedUserId || !selectedPermission) return;
    assign.mutate(
      { databaseId: database.id, userId: selectedUserId, permission: selectedPermission },
      {
        onSuccess: () => {
          closeAdd();
          setSelectedUserId(null);
          setSelectedPermission(null);
        },
      },
    );
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Stack gap={0}>
          <Text fw={600}>{database.displayName}</Text>
          <Text size="xs" c="dimmed">
            {database.serverName}
          </Text>
        </Stack>
        <Button size="xs" leftSection={<IconPlus size={14} />} onClick={openAdd}>
          Add assignment
        </Button>
      </Group>

      <Table.ScrollContainer minWidth={500}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              <Table.Th>Permission</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(roles.data?.roles ?? []).map((r) => (
              <Table.Tr key={r.id}>
                <Table.Td>
                  <Text size="sm">{r.userEmail ?? r.userId}</Text>
                  {r.userName && (
                    <Text size="xs" c="dimmed">
                      {r.userName}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <Badge variant="light" size="sm">
                    {r.permission}
                  </Badge>
                </Table.Td>
                <Table.Td>
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    size="sm"
                    onClick={() =>
                      remove.mutate({
                        databaseId: database.id,
                        userId: r.userId,
                        permission: r.permission,
                      })
                    }
                    aria-label="Remove assignment"
                  >
                    <IconTrash size={14} />
                  </ActionIcon>
                </Table.Td>
              </Table.Tr>
            ))}
            {(roles.data?.roles ?? []).length === 0 && !roles.isLoading && (
              <Table.Tr>
                <Table.Td colSpan={3}>
                  <Text size="sm" c="dimmed">
                    No assignments yet.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal opened={addOpen} onClose={closeAdd} title="Add assignment">
        <Stack gap="sm">
          <Select
            label="User"
            placeholder="Select user…"
            data={userOptions}
            value={selectedUserId}
            onChange={setSelectedUserId}
            searchable
          />
          <Select
            label="Permission"
            placeholder="Select permission…"
            data={SCOPEABLE_PERMISSIONS}
            value={selectedPermission}
            onChange={setSelectedPermission}
          />
          <Button
            onClick={handleAdd}
            loading={assign.isPending}
            disabled={!selectedUserId || !selectedPermission}
          >
            Assign
          </Button>
        </Stack>
      </Modal>
    </Stack>
  );
}

// ── By User tab ───────────────────────────────────────────────────────────────

function ByUserTab() {
  const users = useUsers();
  const servers = useAdminServers();
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);

  const selectedUser = (users.data?.users ?? []).find((u) => u.id === selectedUserId);

  const allDatabases = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases.map((d) => ({
      value: d.id,
      label: `${s.name} / ${d.displayName}`,
    })),
  );

  return (
    <Group align="flex-start" gap="md">
      <Stack gap={4} style={{ minWidth: 220, maxWidth: 280 }}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">
          Users
        </Text>
        {(users.data?.users ?? []).map((u) => (
          <Button
            key={u.id}
            variant={selectedUserId === u.id ? "filled" : "subtle"}
            size="xs"
            justify="left"
            leftSection={<IconUser size={12} />}
            onClick={() => setSelectedUserId(u.id)}
          >
            {u.email ?? u.name ?? u.id}
          </Button>
        ))}
      </Stack>

      <Stack flex={1} gap="md">
        {selectedUser ? (
          <UserRolePanel user={selectedUser} databaseOptions={allDatabases} />
        ) : (
          <Text c="dimmed" size="sm">
            Select a user to manage their database access.
          </Text>
        )}
      </Stack>
    </Group>
  );
}

function UserRolePanel({
  user,
  databaseOptions,
}: {
  user: { id: string; email?: string | null; name?: string | null };
  databaseOptions: { value: string; label: string }[];
}) {
  const roles = useUserRoles(user.id);
  const remove = useRemoveDatabaseRole();
  const assign = useAssignUserRole();
  const [addOpen, { open: openAdd, close: closeAdd }] = useDisclosure(false);
  const [selectedDbId, setSelectedDbId] = useState<string | null>(null);
  const [selectedPermission, setSelectedPermission] = useState<string | null>(null);

  function handleAdd() {
    if (!selectedDbId || !selectedPermission) return;
    assign.mutate(
      { userId: user.id, databaseId: selectedDbId, permission: selectedPermission },
      {
        onSuccess: () => {
          closeAdd();
          setSelectedDbId(null);
          setSelectedPermission(null);
        },
      },
    );
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Stack gap={0}>
          <Text fw={600}>{user.email ?? user.id}</Text>
          {user.name && (
            <Text size="xs" c="dimmed">
              {user.name}
            </Text>
          )}
        </Stack>
        <Button size="xs" leftSection={<IconPlus size={14} />} onClick={openAdd}>
          Add assignment
        </Button>
      </Group>

      <Table.ScrollContainer minWidth={500}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Database</Table.Th>
              <Table.Th>Permission</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(roles.data?.roles ?? []).map((r) => (
              <Table.Tr key={r.id}>
                <Table.Td>
                  <Text size="sm">{r.databaseDisplayName}</Text>
                  <Text size="xs" c="dimmed">
                    {r.serverName}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Badge variant="light" size="sm">
                    {r.permission}
                  </Badge>
                </Table.Td>
                <Table.Td>
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    size="sm"
                    onClick={() =>
                      remove.mutate({
                        databaseId: r.databaseId,
                        userId: user.id,
                        permission: r.permission,
                      })
                    }
                    aria-label="Remove assignment"
                  >
                    <IconTrash size={14} />
                  </ActionIcon>
                </Table.Td>
              </Table.Tr>
            ))}
            {(roles.data?.roles ?? []).length === 0 && !roles.isLoading && (
              <Table.Tr>
                <Table.Td colSpan={3}>
                  <Text size="sm" c="dimmed">
                    No assignments yet.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal opened={addOpen} onClose={closeAdd} title="Add assignment">
        <Stack gap="sm">
          <Select
            label="Database"
            placeholder="Select database…"
            data={databaseOptions}
            value={selectedDbId}
            onChange={setSelectedDbId}
            searchable
          />
          <Select
            label="Permission"
            placeholder="Select permission…"
            data={SCOPEABLE_PERMISSIONS}
            value={selectedPermission}
            onChange={setSelectedPermission}
          />
          <Button
            onClick={handleAdd}
            loading={assign.isPending}
            disabled={!selectedDbId || !selectedPermission}
          >
            Assign
          </Button>
        </Stack>
      </Modal>
    </Stack>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd src/frontend && npx tsc --noEmit && cd ../..
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/routes/_authed/access.tsx
git commit -m "feat: add Access route with By Database and By User tabs"
```

---

### Task 17: Add Access link to nav and run full verification

**Files:**
- Modify: `src/frontend/src/routes/_authed.tsx`

- [ ] **Step 1: Add the Access nav link**

In `_authed.tsx`, add the import for `IconLock` from `@tabler/icons-react` (add to the existing icon imports):

```typescript
  IconLock,
```

Add the nav item after the `{isAdmin && ...}` Permissions nav block (around line 195):

```tsx
          {isAdmin && (
            <NavLink
              label="Access"
              leftSection={<IconLock size={16} />}
              component={Link}
              to="/access"
              active={location.pathname === "/access"}
              onClick={closeMobileNav}
            />
          )}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd src/frontend && npx tsc --noEmit && cd ../..
```

Expected: 0 errors.

- [ ] **Step 3: Run the full integration test suite one final time**

```bash
dotnet test tests/IntegrationTests/ 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 4: Final commit**

```bash
git add src/frontend/src/routes/_authed.tsx
git commit -m "feat: add Access nav link for permission:manage users"
```

---

## Summary

The implementation produces:
- `UserDatabaseRole` entity + EF migration seeding full access for existing users
- `DatabaseRoleEndpoints` with 5 admin endpoints (list/assign/remove by database, list/assign by user, admin server list)
- Updated `CatalogEndpoints`, `QueryEndpoints`, `UpdateEndpoints` with inline role enforcement
- `/api/me` returns distinct permissions from both tables (frontend permission checks unchanged)
- New `access.tsx` route with By-Database and By-User dual-view tabs
- Full integration test coverage for new endpoints; updated tests for changed endpoints
