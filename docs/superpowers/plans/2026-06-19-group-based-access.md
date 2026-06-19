# Group-Based Access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add admin-managed access groups that carry both global and per-database permissions, so a common access pattern can be granted once and inherited live by every member.

**Architecture:** Four new tables mirror the existing per-user grant tables but key on a group. A single `IAccessResolver` seam computes effective access as the union of direct grants and group grants; every authorization check site is migrated onto it. The admin read endpoints are enriched to expose effective access plus provenance (direct vs. which group); a new Groups tab and a provenance overlay surface it in the UI.

**Tech Stack:** .NET 10, EF Core (Npgsql), Vogen strongly-typed IDs, ASP.NET minimal APIs, React + TypeScript + Mantine + TanStack Query, xUnit, Aspire.Hosting.Testing.

**Reference spec:** `docs/superpowers/specs/2026-06-19-group-based-access-design.md`

## Global Constraints

- Develop on this branch (`feat/group-based-access`); never commit to `main`. Commit messages are a single subject line, no body.
- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Never hand-edit EF migration files; analyzer warnings for migrations are already suppressed via `.editorconfig`. No data backfill. If a later task needs a schema change, regenerate this branch's single migration rather than adding a second.
- Suppress experimental API warnings with inline `#pragma warning disable` in the `.cs` file, not `<NoWarn>`.
- Preserve existing comments unless factually wrong.
- Abstract DB-specific operations behind interfaces; no hard-coded Npgsql in domain/business code.
- After any change to API request/response shapes, regenerate `src/SluiceBase.Api/openapi.json` (build the API) and `src/frontend/src/api/schema.ts` (`npm run gen:api`); CI gates both.
- Group-derived grants are treated identically to direct grants for every authorization purpose (including `query:audit` and history visibility). Access is additive — no deny/negative overrides.

---

## File Structure

**Core (`src/SluiceBase.Core/Permissions/`)** — new:
- `AccessGroupId.cs`, `AccessGroupMemberId.cs`, `AccessGroupPermissionId.cs`, `AccessGroupDatabaseRoleId.cs` — Vogen IDs.
- `AccessGroup.cs` — group aggregate.
- `AccessGroupMember.cs` — membership row.
- `AccessGroupPermission.cs` — group global grant.
- `AccessGroupDatabaseRole.cs` — group per-database grant.

**Api (`src/SluiceBase.Api/`)** — new:
- `Data/Configurations/AccessGroupConfiguration.cs` (+ member/permission/role configs in the same file).
- `Auth/IAccessResolver.cs`, `Auth/AccessResolver.cs`.
- `Endpoints/AccessGroupEndpoints.cs` — group CRUD + members + grants.
- `Auth/AccessProvenance.cs` — shared read helper computing provenance.

**Api** — modified:
- `Data/AppDbContext.cs` — four new `DbSet`s.
- `Auth/AuthSetup.cs` — register `IAccessResolver`.
- `Auth/PermissionAuthorizationHandler.cs`, `Auth/AnyPermissionAuthorizationHandler.cs` — use resolver.
- `Endpoints/EndpointMapper.cs` — map `AccessGroupEndpoints`.
- `Endpoints/PermissionEndpoints.cs` (`ListUsers`), `Endpoints/DatabaseRoleEndpoints.cs` (`ListByDatabase`, `ListByUser`) — provenance-enriched responses.
- `Endpoints/QueryEndpoints.cs`, `Endpoints/UpdateEndpoints.cs`, `Endpoints/SchemaEndpoint.cs`, `Endpoints/AuthEndpoints.cs`, `Services/QueryService.cs`, `Services/CatalogService.cs` — route checks through resolver.

**Frontend (`src/frontend/src/`)** — modified/new:
- `api/hooks.ts` — group hooks + provenance types.
- `routes/_authed/access.tsx` — Groups tab + provenance overlay on By User / By Database.
- `routes/_authed/permission.tsx` — provenance display.

**Tests:**
- `tests/SluiceBase.Core.Tests/AccessGroupTests.cs`
- `tests/IntegrationTests/AccessResolverTests.cs`, `AccessGroupEndpointTests.cs`, `EffectiveAccessTests.cs`
- `tests/IntegrationTests/Supports/AccessGroupTestHelper.cs`
- Update `tests/IntegrationTests/Supports/PermissionTestHelper.cs` and any test reading `/api/admin/user` `permissions`.
- `src/frontend/src/routes/_authed/__tests__/` — provenance + groups tests.

---

## Task 1: Core entities, IDs, EF configuration, migration

**Files:**
- Create: `src/SluiceBase.Core/Permissions/AccessGroupId.cs`, `AccessGroupMemberId.cs`, `AccessGroupPermissionId.cs`, `AccessGroupDatabaseRoleId.cs`
- Create: `src/SluiceBase.Core/Permissions/AccessGroup.cs`, `AccessGroupMember.cs`, `AccessGroupPermission.cs`, `AccessGroupDatabaseRole.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/AccessGroupConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`
- Test: `tests/SluiceBase.Core.Tests/AccessGroupTests.cs`

**Interfaces:**
- Produces:
  - `AccessGroup.Create(string name, string? description, UserId? createdById, DateTimeOffset at) -> AccessGroup` with `Rename(string)`, `SetDescription(string?)` mutators; props `Id: AccessGroupId`, `Name`, `Description`, `CreatedAt`, `CreatedById`.
  - `AccessGroupMember.Add(AccessGroupId groupId, UserId userId, UserId? addedById, DateTimeOffset at) -> AccessGroupMember`; props `Id`, `GroupId`, `UserId`, `AddedAt`, `AddedById`.
  - `AccessGroupPermission.Grant(AccessGroupId groupId, string permission, UserId? grantedById, DateTimeOffset at) -> AccessGroupPermission`; props `Id`, `GroupId`, `Permission`, `GrantedAt`, `GrantedById`.
  - `AccessGroupDatabaseRole.Grant(AccessGroupId groupId, string permission, DatabaseId databaseId, UserId? grantedById, DateTimeOffset at) -> AccessGroupDatabaseRole`; props `Id`, `GroupId`, `Permission`, `DatabaseId`, `GrantedAt`, `GrantedById`.
  - `AppDbContext.AccessGroups`, `.AccessGroupMembers`, `.AccessGroupPermissions`, `.AccessGroupDatabaseRoles`.

- [ ] **Step 1: Write the failing test**

Create `tests/SluiceBase.Core.Tests/AccessGroupTests.cs`:

```csharp
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Tests;

public class AccessGroupTests
{
    [Fact]
    public void Create_TrimsName_AndSetsFields()
    {
        var actor = UserId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var group = AccessGroup.Create("  Analysts  ", "  read access  ", actor, at);

        Assert.Equal("Analysts", group.Name);
        Assert.Equal("read access", group.Description);
        Assert.Equal(actor, group.CreatedById);
        Assert.Equal(at, group.CreatedAt);
        Assert.NotEqual(default, group.Id);
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccessGroup.Create("   ", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rename_UpdatesTrimmedName()
    {
        var group = AccessGroup.Create("Old", null, null, DateTimeOffset.UtcNow);
        group.Rename("  New  ");
        Assert.Equal("New", group.Name);
    }

    [Fact]
    public void SetDescription_NormalizesBlankToNull()
    {
        var group = AccessGroup.Create("G", "x", null, DateTimeOffset.UtcNow);
        group.SetDescription("   ");
        Assert.Null(group.Description);
    }

    [Fact]
    public void Member_Add_SetsFields()
    {
        var groupId = AccessGroupId.FromNewVersion7Guid();
        var userId = UserId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var member = AccessGroupMember.Add(groupId, userId, null, at);

        Assert.Equal(groupId, member.GroupId);
        Assert.Equal(userId, member.UserId);
        Assert.Equal(at, member.AddedAt);
    }

    [Fact]
    public void DatabaseRole_Grant_SetsFields()
    {
        var groupId = AccessGroupId.FromNewVersion7Guid();
        var dbId = DatabaseId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var role = AccessGroupDatabaseRole.Grant(groupId, Permissions.QueryExecute, dbId, null, at);

        Assert.Equal(groupId, role.GroupId);
        Assert.Equal(Permissions.QueryExecute, role.Permission);
        Assert.Equal(dbId, role.DatabaseId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SluiceBase.Core.Tests --filter AccessGroupTests`
Expected: FAIL — `AccessGroup`/`AccessGroupMember`/`AccessGroupDatabaseRole` do not exist (compile errors).

- [ ] **Step 3: Create the Vogen IDs**

`src/SluiceBase.Core/Permissions/AccessGroupId.cs`:

```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct AccessGroupId;
```

Repeat verbatim (changing only the struct name) for `AccessGroupMemberId.cs`, `AccessGroupPermissionId.cs`, `AccessGroupDatabaseRoleId.cs`.

- [ ] **Step 4: Create the entities**

`src/SluiceBase.Core/Permissions/AccessGroup.cs`:

```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroup
{
#pragma warning disable CS8618
    private AccessGroup() { }
#pragma warning restore CS8618

    private AccessGroup(
        AccessGroupId id, string name, string? description,
        UserId? createdById, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedById = createdById;
        CreatedAt = createdAt;
    }

    public AccessGroupId Id { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public UserId? CreatedById { get; private set; }

    public static AccessGroup Create(string name, string? description, UserId? createdById, DateTimeOffset at)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Group name is required.", nameof(name));
        }
        return new AccessGroup(AccessGroupId.FromNewVersion7Guid(), trimmed, Normalize(description), createdById, at);
    }

    public void Rename(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Group name is required.", nameof(name));
        }
        Name = trimmed;
    }

    public void SetDescription(string? description) => Description = Normalize(description);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
```

`src/SluiceBase.Core/Permissions/AccessGroupMember.cs`:

```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroupMember
{
#pragma warning disable CS8618
    private AccessGroupMember() { }
#pragma warning restore CS8618

    private AccessGroupMember(
        AccessGroupMemberId id, AccessGroupId groupId, UserId userId,
        UserId? addedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        UserId = userId;
        AddedById = addedById;
        AddedAt = at;
    }

    public AccessGroupMemberId Id { get; private set; }
    public AccessGroupId GroupId { get; private set; }
    public UserId UserId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public UserId? AddedById { get; private set; }

    public static AccessGroupMember Add(AccessGroupId groupId, UserId userId, UserId? addedById, DateTimeOffset at) =>
        new(AccessGroupMemberId.FromNewVersion7Guid(), groupId, userId, addedById, at);
}
```

`src/SluiceBase.Core/Permissions/AccessGroupPermission.cs`:

```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroupPermission
{
#pragma warning disable CS8618
    private AccessGroupPermission() { }
#pragma warning restore CS8618

    private AccessGroupPermission(
        AccessGroupPermissionId id, AccessGroupId groupId, string permission,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public AccessGroupPermissionId Id { get; private set; }
    public AccessGroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static AccessGroupPermission Grant(AccessGroupId groupId, string permission, UserId? grantedById, DateTimeOffset at) =>
        new(AccessGroupPermissionId.FromNewVersion7Guid(), groupId, permission, grantedById, at);
}
```

`src/SluiceBase.Core/Permissions/AccessGroupDatabaseRole.cs`:

```csharp
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroupDatabaseRole
{
#pragma warning disable CS8618
    private AccessGroupDatabaseRole() { }
#pragma warning restore CS8618

    private AccessGroupDatabaseRole(
        AccessGroupDatabaseRoleId id, AccessGroupId groupId, string permission,
        DatabaseId databaseId, UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        DatabaseId = databaseId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public AccessGroupDatabaseRoleId Id { get; private set; }
    public AccessGroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static AccessGroupDatabaseRole Grant(
        AccessGroupId groupId, string permission, DatabaseId databaseId, UserId? grantedById, DateTimeOffset at) =>
        new(AccessGroupDatabaseRoleId.FromNewVersion7Guid(), groupId, permission, databaseId, grantedById, at);
}
```

- [ ] **Step 5: Run the Core tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Core.Tests --filter AccessGroupTests`
Expected: PASS (6 tests).

- [ ] **Step 6: Add the EF configuration**

`src/SluiceBase.Api/Data/Configurations/AccessGroupConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class AccessGroupConfiguration : IEntityTypeConfiguration<AccessGroup>
{
    public void Configure(EntityTypeBuilder<AccessGroup> builder)
    {
        builder.ToTable("access_group");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();
        builder.Property(g => g.Description).HasMaxLength(512);
        builder.Property(g => g.CreatedAt).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.CreatedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccessGroupMemberConfiguration : IEntityTypeConfiguration<AccessGroupMember>
{
    public void Configure(EntityTypeBuilder<AccessGroupMember> builder)
    {
        builder.ToTable("access_group_member");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
        builder.Property(m => m.AddedAt).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.AddedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccessGroupPermissionConfiguration : IEntityTypeConfiguration<AccessGroupPermission>
{
    public void Configure(EntityTypeBuilder<AccessGroupPermission> builder)
    {
        builder.ToTable("access_group_permission");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => new { p.GroupId, p.Permission }).IsUnique();
        builder.Property(p => p.GrantedAt).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(p => p.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.GrantedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccessGroupDatabaseRoleConfiguration : IEntityTypeConfiguration<AccessGroupDatabaseRole>
{
    public void Configure(EntityTypeBuilder<AccessGroupDatabaseRole> builder)
    {
        builder.ToTable("access_group_database_role");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.GroupId, r.Permission, r.DatabaseId }).IsUnique();
        builder.Property(r => r.GrantedAt).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Database>().WithMany().HasForeignKey(r => r.DatabaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.GrantedById).OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 7: Register the DbSets**

In `src/SluiceBase.Api/Data/AppDbContext.cs`, after the `UserDatabaseRoles` line (currently line 22), add:

```csharp
    public DbSet<AccessGroup> AccessGroups => Set<AccessGroup>();
    public DbSet<AccessGroupMember> AccessGroupMembers => Set<AccessGroupMember>();
    public DbSet<AccessGroupPermission> AccessGroupPermissions => Set<AccessGroupPermission>();
    public DbSet<AccessGroupDatabaseRole> AccessGroupDatabaseRoles => Set<AccessGroupDatabaseRole>();
```

(`SluiceBase.Core.Permissions` is already imported.)

- [ ] **Step 8: Generate the migration**

Run: `dotnet ef migrations add AddAccessGroups --project src/SluiceBase.Api`
Expected: `Done.` — a new migration file plus an updated `AppDbContextModelSnapshot.cs`. Do not edit the generated files.

- [ ] **Step 9: Build to verify the migration compiles and model is consistent**

Run: `dotnet build src/SluiceBase.Api`
Expected: Build succeeded. Then verify no pending model changes:
Run: `dotnet ef migrations has-pending-model-changes --project src/SluiceBase.Api`
Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 10: Commit**

```bash
git add src/SluiceBase.Core/Permissions src/SluiceBase.Api/Data tests/SluiceBase.Core.Tests/AccessGroupTests.cs
git commit -m "Add access group entities and migration"
```

---

## Task 2: Access resolver

**Files:**
- Create: `src/SluiceBase.Api/Auth/IAccessResolver.cs`, `src/SluiceBase.Api/Auth/AccessResolver.cs`
- Modify: `src/SluiceBase.Api/Auth/AuthSetup.cs:133` (registration block)
- Test: `tests/IntegrationTests/AccessResolverTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 1 DbSets), `Permissions.Scopeable`.
- Produces:
```csharp
internal interface IAccessResolver
{
    Task<bool> HasGlobalPermissionAsync(UserId user, string permission, CancellationToken ct);
    Task<bool> HasDatabasePermissionAsync(UserId user, DatabaseId db, string permission, CancellationToken ct);
    Task<IReadOnlySet<DatabaseId>> DatabasesWithPermissionAsync(UserId user, string permission, CancellationToken ct);
    Task<IReadOnlySet<DatabaseId>> DatabasesWithAnyScopeableAsync(UserId user, CancellationToken ct);
    Task<IReadOnlySet<string>> EffectivePermissionsAsync(UserId user, CancellationToken ct);
}
```

> Note: `DatabasesWithAnyScopeableAsync` replaces the `anyRoleDatabaseIds` query in `QueryEndpoints.GetHistory` (any scopeable role on a database, direct or via group).

- [ ] **Step 1: Write the failing test**

Create `tests/IntegrationTests/AccessResolverTests.cs`. This exercises the resolver against the real Postgres-backed `AppDbContext` from the Aspire stack. Use the existing `SluiceBaseStackFactory` pattern; resolve a scoped `AppDbContext` and a fresh `AccessResolver` over it.

```csharp
using IntegrationTests.Supports;
using Microsoft.Extensions.DependencyInjection;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace IntegrationTests;

public class AccessResolverTests(SluiceBaseStackFactory factory)
{
    // Seeds: a user, a server+database, optionally direct and/or group grants.
    // Returns the user id and database id. See AccessGroupTestHelper for seeding utilities.
    private async Task<(UserId User, DatabaseId Db)> SeedAsync(
        Func<AppDbContext, UserId, DatabaseId, Task> arrange, CancellationToken ct)
    {
        await using var scope = factory.InitialisedApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (userId, dbId) = await AccessGroupTestHelper.SeedUserAndDatabaseAsync(db, ct);
        await arrange(db, userId, dbId);
        await db.SaveChangesAsync(ct);
        return (userId, dbId);
    }

    private static AccessResolver ResolverOver(AppDbContext db) => new(db);

    [Fact]
    public async Task HasDatabasePermission_TrueForDirectGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync((db, u, d) =>
        {
            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(u, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }, ct);

        await using var scope = factory.InitialisedApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await ResolverOver(db).HasDatabasePermissionAsync(user, dbId, Permissions.QueryExecute, ct));
    }

    [Fact]
    public async Task HasDatabasePermission_TrueForGroupGrant_ViaMembership()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync(async (db, u, d) =>
        {
            var group = AccessGroup.Create("Analysts", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var scope = factory.InitialisedApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await ResolverOver(db).HasDatabasePermissionAsync(user, dbId, Permissions.QueryExecute, ct));
    }

    [Fact]
    public async Task HasDatabasePermission_FalseWhenGroupGrantButNotMember()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync((db, u, d) =>
        {
            var group = AccessGroup.Create("Analysts", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            // grant to group but DO NOT add user as member
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }, ct);

        await using var scope = factory.InitialisedApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await ResolverOver(db).HasDatabasePermissionAsync(user, dbId, Permissions.QueryExecute, ct));
    }

    [Fact]
    public async Task DatabasesWithPermission_UnionsDirectAndGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync(async (db, u, d) =>
        {
            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(u, Permissions.QueryAudit, d, null, DateTimeOffset.UtcNow));
            var group = AccessGroup.Create("A", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryAudit, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var scope = factory.InitialisedApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await ResolverOver(db).DatabasesWithPermissionAsync(user, Permissions.QueryAudit, ct);
        Assert.Contains(dbId, result);
        Assert.Single(result); // de-duplicated across direct + group
    }

    [Fact]
    public async Task HasGlobalPermission_TrueForGroupGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, _) = await SeedAsync((db, u, d) =>
        {
            var group = AccessGroup.Create("Admins", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupPermissions.Add(
                AccessGroupPermission.Grant(group.Id, Permissions.ServerManage, null, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }, ct);

        await using var scope = factory.InitialisedApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await ResolverOver(db).HasGlobalPermissionAsync(user, Permissions.ServerManage, ct));
    }
}
```

Also create the seeding helper `tests/IntegrationTests/Supports/AccessGroupTestHelper.cs` with `SeedUserAndDatabaseAsync`:

```csharp
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace IntegrationTests.Supports;

internal static class AccessGroupTestHelper
{
    // Inserts a user, a server+credential+database directly via the context.
    // Returns the new user id and database id for resolver-level tests.
    public static async Task<(UserId User, DatabaseId Db)> SeedUserAndDatabaseAsync(
        AppDbContext db, CancellationToken ct)
    {
        var user = User.Create($"res-{Guid.NewGuid():N}@example.com", "Res User", DateTimeOffset.UtcNow);
        db.Users.Add(user);

        var server = Server.Create($"res-{Guid.NewGuid():N}"[..18], "localhost", 5432, DateTimeOffset.UtcNow);
        db.Servers.Add(server);
        var database = Database.Create(server.Id, "appdb", "App DB", DateTimeOffset.UtcNow);
        db.Databases.Add(database);

        await db.SaveChangesAsync(ct);
        return (user.Id, database.Id);
    }
}
```

> If `Server.Create` / `Database.Create` signatures differ, open `src/SluiceBase.Core/Servers/Server.cs` and `Database.cs` and match the actual factory parameters; the goal is simply a persisted user + database.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter AccessResolverTests`
Expected: FAIL — `AccessResolver`/`IAccessResolver` do not exist (compile error).

- [ ] **Step 3: Create the interface**

`src/SluiceBase.Api/Auth/IAccessResolver.cs`:

```csharp
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal interface IAccessResolver
{
    Task<bool> HasGlobalPermissionAsync(UserId user, string permission, CancellationToken ct);
    Task<bool> HasDatabasePermissionAsync(UserId user, DatabaseId db, string permission, CancellationToken ct);
    Task<IReadOnlySet<DatabaseId>> DatabasesWithPermissionAsync(UserId user, string permission, CancellationToken ct);
    Task<IReadOnlySet<DatabaseId>> DatabasesWithAnyScopeableAsync(UserId user, CancellationToken ct);
    Task<IReadOnlySet<string>> EffectivePermissionsAsync(UserId user, CancellationToken ct);
}
```

- [ ] **Step 4: Implement the resolver**

`src/SluiceBase.Api/Auth/AccessResolver.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal sealed class AccessResolver(AppDbContext db) : IAccessResolver
{
    // group ids the user belongs to
    private IQueryable<AccessGroupId> MemberGroupIds(UserId user) =>
        db.AccessGroupMembers.Where(m => m.UserId == user).Select(m => m.GroupId);

    public async Task<bool> HasGlobalPermissionAsync(UserId user, string permission, CancellationToken ct)
    {
        var direct = await db.UserPermissions
            .AnyAsync(p => p.UserId == user && p.Permission == permission, ct);
        if (direct)
        {
            return true;
        }
        return await db.AccessGroupPermissions
            .Where(p => p.Permission == permission)
            .Where(p => MemberGroupIds(user).Contains(p.GroupId))
            .AnyAsync(ct);
    }

    public async Task<bool> HasDatabasePermissionAsync(UserId user, DatabaseId dbId, string permission, CancellationToken ct)
    {
        var direct = await db.UserDatabaseRoles
            .AnyAsync(r => r.UserId == user && r.DatabaseId == dbId && r.Permission == permission, ct);
        if (direct)
        {
            return true;
        }
        return await db.AccessGroupDatabaseRoles
            .Where(r => r.DatabaseId == dbId && r.Permission == permission)
            .Where(r => MemberGroupIds(user).Contains(r.GroupId))
            .AnyAsync(ct);
    }

    public async Task<IReadOnlySet<DatabaseId>> DatabasesWithPermissionAsync(UserId user, string permission, CancellationToken ct)
    {
        var direct = db.UserDatabaseRoles
            .Where(r => r.UserId == user && r.Permission == permission)
            .Select(r => r.DatabaseId);
        var viaGroup = db.AccessGroupDatabaseRoles
            .Where(r => r.Permission == permission)
            .Where(r => MemberGroupIds(user).Contains(r.GroupId))
            .Select(r => r.DatabaseId);
        var ids = await direct.Union(viaGroup).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlySet<DatabaseId>> DatabasesWithAnyScopeableAsync(UserId user, CancellationToken ct)
    {
        var direct = db.UserDatabaseRoles
            .Where(r => r.UserId == user)
            .Select(r => r.DatabaseId);
        var viaGroup = db.AccessGroupDatabaseRoles
            .Where(r => MemberGroupIds(user).Contains(r.GroupId))
            .Select(r => r.DatabaseId);
        var ids = await direct.Union(viaGroup).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlySet<string>> EffectivePermissionsAsync(UserId user, CancellationToken ct)
    {
        var directGlobal = db.UserPermissions.Where(p => p.UserId == user).Select(p => p.Permission);
        var directDb = db.UserDatabaseRoles.Where(r => r.UserId == user).Select(r => r.Permission);
        var groupGlobal = db.AccessGroupPermissions
            .Where(p => MemberGroupIds(user).Contains(p.GroupId)).Select(p => p.Permission);
        var groupDb = db.AccessGroupDatabaseRoles
            .Where(r => MemberGroupIds(user).Contains(r.GroupId)).Select(r => r.Permission);

        var all = await directGlobal.Union(directDb).Union(groupGlobal).Union(groupDb).ToListAsync(ct);
        return all.ToHashSet();
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/SluiceBase.Api/Auth/AuthSetup.cs`, in the registration block near line 133, add after the `ICurrentUserAccessor` line:

```csharp
        services.AddScoped<IAccessResolver, AccessResolver>();
```

- [ ] **Step 6: Run the resolver tests to verify they pass**

Run: `dotnet test tests/IntegrationTests --filter AccessResolverTests`
Expected: PASS (5 tests). (Integration tests require a healthy Aspire stack; if it cannot start locally, build instead — `dotnet build src/SluiceBase.Api tests/IntegrationTests` — and rely on CI.)

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Auth tests/IntegrationTests/AccessResolverTests.cs tests/IntegrationTests/Supports/AccessGroupTestHelper.cs
git commit -m "Add access resolver unioning direct and group grants"
```

---

## Task 3: Route authorization through the resolver

Migrate every existing check site so group-derived access is honored. Behavior for direct-only users is unchanged; users with group grants now pass.

**Files:**
- Modify: `src/SluiceBase.Api/Auth/PermissionAuthorizationHandler.cs`, `src/SluiceBase.Api/Auth/AnyPermissionAuthorizationHandler.cs`
- Modify: `src/SluiceBase.Api/Services/QueryService.cs`, `src/SluiceBase.Api/Services/CatalogService.cs`
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`, `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`, `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs`
- Test: `tests/IntegrationTests/EffectiveAccessTests.cs`

**Interfaces:**
- Consumes: `IAccessResolver` (Task 2).

- [ ] **Step 1: Write the failing end-to-end test**

Create `tests/IntegrationTests/EffectiveAccessTests.cs`. This proves a member can execute a query on a database purely via a group grant, and loses access when removed. Use the Keycloak login + admin endpoints (built in Task 4) — so this test is authored here but its group-creation calls depend on Task 4's endpoints; if executing strictly in order, seed the group directly via a scoped `AppDbContext` instead (shown below) to keep Task 3 self-contained.

```csharp
using System.Net;
using IntegrationTests.Supports;
using Microsoft.Extensions.DependencyInjection;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class EffectiveAccessTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    [Fact]
    public async Task Member_CanQueryDatabase_GrantedOnlyViaGroup()
    {
        var ct = TestContext.Current.CancellationToken;

        // alice signs in; resolve her user id
        var alice = await LoginHelper.SignInAsync("alice", "dev", ct);

        // seed a database + a group that grants query:execute on it, with alice as member
        await using (var scope = factory.InitialisedApp.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aliceUser = await AccessGroupTestHelper.GetUserByEmailAsync(db, "alice@example.com", ct);
            var (_, dbId) = await AccessGroupTestHelper.SeedDatabaseOnlyAsync(db, ct);

            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}"[..16], null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, aliceUser.Id, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryExecute, dbId, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);

            AccessGroupTestHelper.LastSeededDatabaseId = dbId.Value.ToString();
        }

        // alice executes a query on that database — allowed purely via the group
        var resp = await alice.Client.PostAsJsonAsync("/api/query",
            new { databaseId = AccessGroupTestHelper.LastSeededDatabaseId, sql = "SELECT 1" }, ct);

        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
```

> Add `GetUserByEmailAsync`, `SeedDatabaseOnlyAsync`, and a `LastSeededDatabaseId` field to `AccessGroupTestHelper` (mirror `SeedUserAndDatabaseAsync` from Task 2, splitting out the database-only seed). The query may still fail to *run* against a non-real database, but it must not return `403 Forbidden` — authorization is what we assert.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter EffectiveAccessTests`
Expected: FAIL — `403 Forbidden`, because `QueryService` still checks only `UserDatabaseRoles`.

- [ ] **Step 3: Update the global permission handlers**

Replace the body of `src/SluiceBase.Api/Auth/PermissionAuthorizationHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class PermissionAuthorizationHandler(
    ICurrentUserAccessor currentUser,
    IAccessResolver resolver) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var user = await currentUser.GetAsync(CancellationToken.None);
        if (user is null)
        {
            return;
        }
        if (await resolver.HasGlobalPermissionAsync(user.Id, req.Permission, CancellationToken.None))
        {
            ctx.Succeed(req);
        }
    }
}
```

Open `src/SluiceBase.Api/Auth/AnyPermissionAuthorizationHandler.cs` and apply the same pattern: inject `IAccessResolver`, and succeed if `HasGlobalPermissionAsync` returns true for ANY permission in the requirement's set (loop with `await`, succeed on first match).

- [ ] **Step 4: Update QueryService**

In `src/SluiceBase.Api/Services/QueryService.cs`, find the `query:execute` authorization check (an `AnyAsync` against `UserDatabaseRoles` or a `user.HasPermission`-style check) and replace it with a constructor-injected `IAccessResolver resolver` call:

```csharp
// was: db.UserDatabaseRoles.AnyAsync(r => r.UserId == user.Id && r.DatabaseId == databaseId && r.Permission == Permissions.QueryExecute, ct)
var allowed = await resolver.HasDatabasePermissionAsync(user.Id, databaseId, Permissions.QueryExecute, ct);
if (!allowed)
{
    return /* existing Forbidden result */;
}
```

Add `IAccessResolver resolver` to the `QueryService` primary constructor parameters.

- [ ] **Step 5: Update the remaining check sites**

Apply the same mechanical substitution (inject `IAccessResolver`, replace the inline `UserDatabaseRoles` query) at:

- `src/SluiceBase.Api/Services/CatalogService.cs` — `allowedIds` list → `await resolver.DatabasesWithPermissionAsync(user.Id, Permissions.QueryExecute, ct)` (match the permission the existing code filters on).
- `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs` — `hasRole` → `await resolver.HasDatabasePermissionAsync(user.Id, databaseId, <existing permission>, ct)`.
- `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` — each `AnyAsync` submit/approve/execute check → `HasDatabasePermissionAsync` with the same permission; `allowedDatabaseIds` → `DatabasesWithPermissionAsync`. Add `IAccessResolver resolver` to each endpoint handler's parameter list (minimal-API handlers receive it via DI).
- `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` (`GetHistory`) — `auditDatabaseIds` → `DatabasesWithPermissionAsync(user.Id, Permissions.QueryAudit, ct)`; `anyRoleDatabaseIds` → `DatabasesWithAnyScopeableAsync(user.Id, ct)`. Add `IAccessResolver resolver` parameter.
- `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs` (`/api/me`) — replace the `databaseRolePermissions` query + `Concat` block with:
  ```csharp
  var effective = await resolver.EffectivePermissionsAsync(user.Id, ct);
  return TypedResults.Ok(new MeResponse(user.Id, user.Email, user.Name, effective.ToArray()));
  ```
  Add `IAccessResolver resolver` to the `/api/me` handler lambda parameters; remove the now-unused `AppDbContext db` parameter if nothing else uses it.

- [ ] **Step 6: Build**

Run: `dotnet build src/SluiceBase.Api`
Expected: Build succeeded (resolve any leftover unused-variable warnings from removed queries).

- [ ] **Step 7: Run the effective-access + existing auth tests**

Run: `dotnet test tests/IntegrationTests --filter "EffectiveAccessTests|MeEndpointTests|QueryHistoryEndpointTests|QueryServiceTests|SchemaEndpointTests|UpdateEndpointTests"`
Expected: PASS. The new test now returns non-403; existing direct-grant tests still pass (direct grants unaffected).

- [ ] **Step 8: Commit**

```bash
git add src/SluiceBase.Api tests/IntegrationTests/EffectiveAccessTests.cs tests/IntegrationTests/Supports/AccessGroupTestHelper.cs
git commit -m "Route authorization checks through access resolver"
```

---

## Task 4: Group management API

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/AccessGroupEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`
- Create: `tests/IntegrationTests/AccessGroupEndpointTests.cs`
- Create: `tests/IntegrationTests/Supports/AccessGroupTestHelper.cs` additions (admin HTTP helpers)

**Interfaces:**
- Consumes: `AppDbContext`, `ICurrentUserAccessor`, `Permissions.Global`/`Scopeable`.
- Produces these routes (all under `/api/admin`, `RequireAuthorization(Permissions.PermissionManage)`):
  - `GET /api/admin/group` → `GroupListResponse(IReadOnlyList<GroupSummary> Groups)`, `GroupSummary(AccessGroupId Id, string Name, string? Description, int MemberCount, int GlobalPermissionCount, int DatabaseRoleCount)`
  - `POST /api/admin/group` body `CreateGroupRequest(string Name, string? Description)` → `Created`
  - `GET /api/admin/group/{groupId}` → `GroupDetailResponse(AccessGroupId Id, string Name, string? Description, IReadOnlyList<GroupMemberItem> Members, IReadOnlyList<string> GlobalPermissions, IReadOnlyList<GroupDatabaseRoleItem> DatabaseRoles)` where `GroupMemberItem(UserId UserId, string? Email, string? Name)` and `GroupDatabaseRoleItem(DatabaseId DatabaseId, string Permission)`
  - `PATCH /api/admin/group/{groupId}` body `UpdateGroupRequest(string Name, string? Description)` → `NoContent`/`NotFound`
  - `DELETE /api/admin/group/{groupId}` → `NoContent`
  - `POST` / `DELETE /api/admin/group/{groupId}/member/{userId}` → `Created`/`NoContent`
  - `POST` / `DELETE /api/admin/group/{groupId}/permission/{permission}` → `Created`/`NoContent`/`ValidationProblem`
  - `POST` / `DELETE /api/admin/group/{groupId}/database/{databaseId}/role/{permission}` → `Created`/`NoContent`/`ValidationProblem`

> Gateway note: the YARP gateway routes `/api/{**rest}` (`src/AppHost/Program.cs:66`), so `/api/admin/group` needs no AppHost change. Verify by reading that line — no edit expected.

- [ ] **Step 1: Write the failing test**

Create `tests/IntegrationTests/AccessGroupEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class AccessGroupEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage Mutation(HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task CreateListAndDeleteGroup_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await LoginHelper.SignInAsync("alice", "dev", ct); // alice is bootstrap admin
        var xsrf = await admin.FetchXsrfTokenAsync(ct);

        var name = $"grp-{Guid.NewGuid():N}"[..16];
        var create = Mutation(HttpMethod.Post, "/api/admin/group", xsrf, new { name, description = "desc" });
        var createResp = await admin.Client.SendAsync(create, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var list = await admin.Client.GetFromJsonAsync<GroupListBody>("/api/admin/group", ct);
        var created = Assert.Single(list!.Groups, g => g.Name == name);

        var del = Mutation(HttpMethod.Delete, $"/api/admin/group/{created.Id}", xsrf);
        (await admin.Client.SendAsync(del, ct)).EnsureSuccessStatusCode();

        var afterList = await admin.Client.GetFromJsonAsync<GroupListBody>("/api/admin/group", ct);
        Assert.DoesNotContain(afterList!.Groups, g => g.Name == name);
    }

    [Fact]
    public async Task GrantGlobalPermission_RejectsNonGlobal()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await admin.FetchXsrfTokenAsync(ct);

        var name = $"grp-{Guid.NewGuid():N}"[..16];
        (await admin.Client.SendAsync(Mutation(HttpMethod.Post, "/api/admin/group", xsrf, new { name }), ct))
            .EnsureSuccessStatusCode();
        var list = await admin.Client.GetFromJsonAsync<GroupListBody>("/api/admin/group", ct);
        var group = Assert.Single(list!.Groups, g => g.Name == name);

        // query:execute is scopeable, not global → 400
        var bad = Mutation(HttpMethod.Post, $"/api/admin/group/{group.Id}/permission/{Permissions.QueryExecute}", xsrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.SendAsync(bad, ct)).StatusCode);

        // server:manage is global → 201
        var ok = Mutation(HttpMethod.Post, $"/api/admin/group/{group.Id}/permission/{Permissions.ServerManage}", xsrf);
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.SendAsync(ok, ct)).StatusCode);
    }

    private sealed record GroupListBody(IReadOnlyList<GroupSummaryBody> Groups);
    private sealed record GroupSummaryBody(string Id, string Name, string? Description, int MemberCount);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter AccessGroupEndpointTests`
Expected: FAIL — routes return 404 (not mapped).

- [ ] **Step 3: Implement the endpoints**

Create `src/SluiceBase.Api/Endpoints/AccessGroupEndpoints.cs`. Follow the structure of `PermissionEndpoints`/`DatabaseRoleEndpoints` (static `Map`, `MapGroup("/api/admin").RequireAuthorization(Permissions.PermissionManage)`, `TypedResults`):

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class AccessGroupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/group", ListGroups).WithName("ListAccessGroups");
        admin.MapPost("/group", CreateGroup).WithName("CreateAccessGroup");
        admin.MapGet("/group/{groupId}", GetGroup).WithName("GetAccessGroup");
        admin.MapPatch("/group/{groupId}", UpdateGroup).WithName("UpdateAccessGroup");
        admin.MapDelete("/group/{groupId}", DeleteGroup).WithName("DeleteAccessGroup");

        admin.MapPost("/group/{groupId}/member/{userId}", AddMember).WithName("AddAccessGroupMember");
        admin.MapDelete("/group/{groupId}/member/{userId}", RemoveMember).WithName("RemoveAccessGroupMember");

        admin.MapPost("/group/{groupId}/permission/{permission}", GrantGlobal).WithName("GrantAccessGroupPermission");
        admin.MapDelete("/group/{groupId}/permission/{permission}", RevokeGlobal).WithName("RevokeAccessGroupPermission");

        admin.MapPost("/group/{groupId}/database/{databaseId}/role/{permission}", GrantDbRole).WithName("GrantAccessGroupDatabaseRole");
        admin.MapDelete("/group/{groupId}/database/{databaseId}/role/{permission}", RevokeDbRole).WithName("RevokeAccessGroupDatabaseRole");
    }

    private static async Task<Ok<GroupListResponse>> ListGroups(AppDbContext db, CancellationToken ct)
    {
        var groups = await db.AccessGroups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupSummary(
                g.Id, g.Name, g.Description,
                db.AccessGroupMembers.Count(m => m.GroupId == g.Id),
                db.AccessGroupPermissions.Count(p => p.GroupId == g.Id),
                db.AccessGroupDatabaseRoles.Count(r => r.GroupId == g.Id)))
            .ToListAsync(ct);
        return TypedResults.Ok(new GroupListResponse(groups));
    }

    private static async Task<Created> CreateGroup(
        CreateGroupRequest req, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        var group = AccessGroup.Create(req.Name, req.Description, actor?.Id, clock.GetUtcNow());
        db.AccessGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{group.Id}");
    }

    private static async Task<Results<Ok<GroupDetailResponse>, NotFound>> GetGroup(
        AccessGroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var group = await db.AccessGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return TypedResults.NotFound();

        var members = await db.AccessGroupMembers
            .Where(m => m.GroupId == groupId)
            .Join(db.ExternalLogins, m => m.UserId, l => l.UserId,
                (m, l) => new GroupMemberItem(m.UserId, l.Email, l.Name))
            .ToListAsync(ct);
        var global = await db.AccessGroupPermissions
            .Where(p => p.GroupId == groupId).Select(p => p.Permission).ToListAsync(ct);
        var roles = await db.AccessGroupDatabaseRoles
            .Where(r => r.GroupId == groupId)
            .Select(r => new GroupDatabaseRoleItem(r.DatabaseId, r.Permission)).ToListAsync(ct);

        return TypedResults.Ok(new GroupDetailResponse(group.Id, group.Name, group.Description, members, global, roles));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateGroup(
        AccessGroupId groupId, UpdateGroupRequest req, AppDbContext db, CancellationToken ct)
    {
        var group = await db.AccessGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return TypedResults.NotFound();
        group.Rename(req.Name);
        group.SetDescription(req.Description);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeleteGroup(AccessGroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var group = await db.AccessGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is not null)
        {
            db.AccessGroups.Remove(group); // cascades members + grant rows
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Ok>> AddMember(
        AccessGroupId groupId, UserId userId, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.AccessGroups.AnyAsync(g => g.Id == groupId, ct)) return TypedResults.NotFound();
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return TypedResults.NotFound();
        if (await db.AccessGroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct))
            return TypedResults.Ok();
        var actor = await currentUser.GetAsync(ct);
        db.AccessGroupMembers.Add(AccessGroupMember.Add(groupId, userId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{groupId}/member/{userId}");
    }

    private static async Task<NoContent> RemoveMember(
        AccessGroupId groupId, UserId userId, AppDbContext db, CancellationToken ct)
    {
        var member = await db.AccessGroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is not null) { db.AccessGroupMembers.Remove(member); await db.SaveChangesAsync(ct); }
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Ok, ValidationProblem>> GrantGlobal(
        AccessGroupId groupId, string permission, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (!Permissions.Global.Contains(permission))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["permission"] = [$"'{permission}' is not a global permission."] });
        if (!await db.AccessGroups.AnyAsync(g => g.Id == groupId, ct)) return TypedResults.NotFound();
        if (await db.AccessGroupPermissions.AnyAsync(p => p.GroupId == groupId && p.Permission == permission, ct))
            return TypedResults.Ok();
        var actor = await currentUser.GetAsync(ct);
        db.AccessGroupPermissions.Add(AccessGroupPermission.Grant(groupId, permission, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{groupId}/permission/{permission}");
    }

    private static async Task<NoContent> RevokeGlobal(
        AccessGroupId groupId, string permission, AppDbContext db, CancellationToken ct)
    {
        var row = await db.AccessGroupPermissions.SingleOrDefaultAsync(p => p.GroupId == groupId && p.Permission == permission, ct);
        if (row is not null) { db.AccessGroupPermissions.Remove(row); await db.SaveChangesAsync(ct); }
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Ok, ValidationProblem>> GrantDbRole(
        AccessGroupId groupId, DatabaseId databaseId, string permission, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (!Permissions.Scopeable.Contains(permission))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["permission"] = [$"'{permission}' is not a scopeable permission."] });
        if (!await db.AccessGroups.AnyAsync(g => g.Id == groupId, ct)) return TypedResults.NotFound();
        if (!await db.Databases.AnyAsync(d => d.Id == databaseId, ct)) return TypedResults.NotFound();
        if (await db.AccessGroupDatabaseRoles.AnyAsync(r => r.GroupId == groupId && r.Permission == permission && r.DatabaseId == databaseId, ct))
            return TypedResults.Ok();
        var actor = await currentUser.GetAsync(ct);
        db.AccessGroupDatabaseRoles.Add(AccessGroupDatabaseRole.Grant(groupId, permission, databaseId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{groupId}/database/{databaseId}/role/{permission}");
    }

    private static async Task<NoContent> RevokeDbRole(
        AccessGroupId groupId, DatabaseId databaseId, string permission, AppDbContext db, CancellationToken ct)
    {
        var row = await db.AccessGroupDatabaseRoles.SingleOrDefaultAsync(
            r => r.GroupId == groupId && r.DatabaseId == databaseId && r.Permission == permission, ct);
        if (row is not null) { db.AccessGroupDatabaseRoles.Remove(row); await db.SaveChangesAsync(ct); }
        return TypedResults.NoContent();
    }

    public sealed record CreateGroupRequest(string Name, string? Description);
    public sealed record UpdateGroupRequest(string Name, string? Description);
    public sealed record GroupSummary(
        AccessGroupId Id, string Name, string? Description, int MemberCount, int GlobalPermissionCount, int DatabaseRoleCount);
    public sealed record GroupListResponse(IReadOnlyList<GroupSummary> Groups);
    public sealed record GroupMemberItem(UserId UserId, string? Email, string? Name);
    public sealed record GroupDatabaseRoleItem(DatabaseId DatabaseId, string Permission);
    public sealed record GroupDetailResponse(
        AccessGroupId Id, string Name, string? Description,
        IReadOnlyList<GroupMemberItem> Members, IReadOnlyList<string> GlobalPermissions,
        IReadOnlyList<GroupDatabaseRoleItem> DatabaseRoles);
}
```

- [ ] **Step 4: Map the endpoints**

In `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`, after `DatabaseRoleEndpoints.Map(app);`, add:

```csharp
        AccessGroupEndpoints.Map(app);
```

- [ ] **Step 5: Run the group endpoint tests**

Run: `dotnet test tests/IntegrationTests --filter AccessGroupEndpointTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Regenerate OpenAPI**

Run: `dotnet build src/SluiceBase.Api`
Expected: `src/SluiceBase.Api/openapi.json` updated with the new routes.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Endpoints tests/IntegrationTests/AccessGroupEndpointTests.cs src/SluiceBase.Api/openapi.json
git commit -m "Add access group management endpoints"
```

---

## Task 5: Provenance-enriched read endpoints

Enrich the three per-user/per-database read endpoints so each permission carries `fromDirect` + `fromGroups`. No new endpoint.

**Files:**
- Create: `src/SluiceBase.Api/Auth/AccessProvenance.cs` — shared read helper.
- Modify: `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs` (`ListUsers`, response records), `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs` (`ListByDatabase`, `ListByUser`, response records).
- Modify: `tests/IntegrationTests/Supports/PermissionTestHelper.cs` and any test deserializing `/api/admin/user` `permissions` (search below).
- Test: extend `tests/IntegrationTests/AccessGroupEndpointTests.cs` (or a new `EffectiveAccessReadTests.cs`).

**Interfaces:**
- Consumes: Task 1 group tables.
- Produces:
  - `GroupRef(AccessGroupId GroupId, string Name)`
  - `EffectivePermission(string Permission, bool FromDirect, IReadOnlyList<GroupRef> FromGroups)`
  - `ListUsers` `UserSummaryResponse.Permissions` becomes `IReadOnlyList<EffectivePermission>`.
  - `ListByUser` returns `EffectiveUserRoleItem(DatabaseId DatabaseId, string DatabaseDisplayName, string ServerName, string Permission, bool FromDirect, IReadOnlyList<GroupRef> FromGroups)`.
  - `ListByDatabase` returns `EffectiveDatabaseRoleItem(UserId UserId, string? UserEmail, string? UserName, string Permission, bool FromDirect, IReadOnlyList<GroupRef> FromGroups)`.

- [ ] **Step 1: Find every consumer of the current `/api/admin/user` permissions shape**

Run: `grep -rn "Permissions" tests/IntegrationTests | grep -i "user\|ListUserBody\|UserSummary"`
Expected: identifies `PermissionTestHelper.cs` (`ListUserBody`) and any assertion in `AdminPermissionTests.cs` / `MeEndpointTests.cs`. Note them — they must be updated in Step 6.

- [ ] **Step 2: Write the failing test**

Add to `tests/IntegrationTests/AccessGroupEndpointTests.cs`:

```csharp
    [Fact]
    public async Task ListUsers_ReportsGroupProvenanceForGlobalPermission()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await admin.FetchXsrfTokenAsync(ct);

        var users = await admin.Client.GetFromJsonAsync<UsersProvBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        var name = $"grp-{Guid.NewGuid():N}"[..16];
        (await admin.Client.SendAsync(Mutation(HttpMethod.Post, "/api/admin/group", xsrf, new { name }), ct)).EnsureSuccessStatusCode();
        var groups = await admin.Client.GetFromJsonAsync<GroupListBody2>("/api/admin/group", ct);
        var group = Assert.Single(groups!.Groups, g => g.Name == name);

        (await admin.Client.SendAsync(Mutation(HttpMethod.Post, $"/api/admin/group/{group.Id}/member/{alice.Id}", xsrf), ct)).EnsureSuccessStatusCode();
        (await admin.Client.SendAsync(Mutation(HttpMethod.Post, $"/api/admin/group/{group.Id}/permission/{Permissions.ServerManage}", xsrf), ct)).EnsureSuccessStatusCode();

        var after = await admin.Client.GetFromJsonAsync<UsersProvBody>("/api/admin/user", ct);
        var aliceAfter = Assert.Single(after!.Users, u => u.Email == "alice@example.com");
        var serverManage = Assert.Single(aliceAfter.Permissions, p => p.Permission == Permissions.ServerManage);
        Assert.Contains(serverManage.FromGroups, g => g.Name == name);
    }

    private sealed record UsersProvBody(IReadOnlyList<UserProvItem> Users);
    private sealed record UserProvItem(string Id, string? Email, IReadOnlyList<EffPermBody> Permissions);
    private sealed record EffPermBody(string Permission, bool FromDirect, IReadOnlyList<GroupRefBody> FromGroups);
    private sealed record GroupRefBody(string GroupId, string Name);
    private sealed record GroupListBody2(IReadOnlyList<GroupItem2> Groups);
    private sealed record GroupItem2(string Id, string Name);
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter ListUsers_ReportsGroupProvenanceForGlobalPermission`
Expected: FAIL — current `permissions` is `string[]`, so deserialization into `EffPermBody` yields empty/throws.

- [ ] **Step 4: Add the shared provenance DTOs**

These records are the shared shape returned by all three enriched endpoints, so they live in one file. `src/SluiceBase.Api/Auth/AccessProvenance.cs`:

```csharp
using SluiceBase.Core.Permissions;

namespace SluiceBase.Api.Auth;

public sealed record GroupRef(AccessGroupId GroupId, string Name);

public sealed record EffectivePermission(string Permission, bool FromDirect, IReadOnlyList<GroupRef> FromGroups);
```

> The actual union/grouping is done inline in each endpoint (Steps 5–6): `ListUsers` batches across all users, while `ListByUser`/`ListByDatabase` key on `(DatabaseId, Permission)` / `(UserId, Permission)` for one subject. The per-endpoint queries differ enough (batched-all-users vs. single-subject, global vs. per-database) that a shared method would not DRY cleanly — only the DTOs are shared.

- [ ] **Step 5: Enrich `ListUsers`**

In `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs`:
- Change `UserSummaryResponse`'s last field from `string[] Permissions` to `IReadOnlyList<EffectivePermission> Permissions`.
- Rewrite `ListUsers` to compute provenance per user. Because this loads all users, compute the group permissions once and group in memory:

```csharp
private static async Task<Ok<ListUsersResponse>> ListUsers(AppDbContext db, CancellationToken ct)
{
    var users = await db.ExternalLogins
        .Include(u => u.User).ThenInclude(u => u.Permissions)
        .AsNoTracking()
        .OrderBy(u => u.Email)
        .Select(u => new { u.UserId, u.Email, u.Name, u.LastLoginAt,
            Direct = u.User.Permissions.Select(p => p.Permission).ToList() })
        .ToListAsync(ct);

    // all group-global grants joined to membership + group name
    var groupGrants = await db.AccessGroupMembers
        .Join(db.AccessGroupPermissions, m => m.GroupId, p => p.GroupId,
            (m, p) => new { m.UserId, p.Permission, p.GroupId })
        .Join(db.AccessGroups, x => x.GroupId, g => g.Id,
            (x, g) => new { x.UserId, x.Permission, Group = new GroupRef(g.Id, g.Name) })
        .ToListAsync(ct);

    var result = users.Select(u =>
    {
        var groupsForUser = groupGrants.Where(g => g.UserId == u.UserId).ToList();
        var perms = u.Direct.Concat(groupsForUser.Select(g => g.Permission)).Distinct()
            .Select(perm => new EffectivePermission(
                perm, u.Direct.Contains(perm),
                groupsForUser.Where(g => g.Permission == perm).Select(g => g.Group).ToList()))
            .ToList();
        return new UserSummaryResponse(u.UserId, u.Email, u.Name, u.LastLoginAt, perms);
    }).ToList();

    return TypedResults.Ok(new ListUsersResponse(result));
}
```

Update the `using` to include `SluiceBase.Api.Auth;` and change `UserSummaryResponse`:

```csharp
internal sealed record UserSummaryResponse(
    UserId Id, string? Email, string? Name, DateTimeOffset? LastLoginAt,
    IReadOnlyList<EffectivePermission> Permissions);
```

- [ ] **Step 6: Enrich `ListByUser` and `ListByDatabase`**

In `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs`, replace the `ListByUser` and `ListByDatabase` bodies and their item records so each `(database/user, permission)` carries `FromDirect` + `FromGroups`. For `ListByUser`:

```csharp
private static async Task<Ok<UserRoleListResponse>> ListByUser(
    UserId userId, AppDbContext db, CancellationToken ct)
{
    var direct = await db.UserDatabaseRoles
        .Where(r => r.UserId == userId)
        .Select(r => new { r.DatabaseId, r.Permission })
        .ToListAsync(ct);

    var viaGroups = await db.AccessGroupMembers
        .Where(m => m.UserId == userId)
        .Join(db.AccessGroupDatabaseRoles, m => m.GroupId, r => r.GroupId,
            (m, r) => new { r.DatabaseId, r.Permission, r.GroupId })
        .Join(db.AccessGroups, x => x.GroupId, g => g.Id,
            (x, g) => new { x.DatabaseId, x.Permission, Group = new GroupRef(g.Id, g.Name) })
        .ToListAsync(ct);

    var dbNames = await db.Databases
        .Join(db.Servers, d => d.ServerId, s => s.Id, (d, s) => new { d.Id, d.DisplayName, ServerName = s.Name })
        .ToListAsync(ct);

    var keys = direct.Select(d => (d.DatabaseId, d.Permission))
        .Concat(viaGroups.Select(v => (v.DatabaseId, v.Permission))).Distinct();

    var items = keys.Select(k =>
    {
        var name = dbNames.FirstOrDefault(n => n.Id == k.DatabaseId);
        return new EffectiveUserRoleItem(
            k.DatabaseId, name?.DisplayName ?? "", name?.ServerName ?? "", k.Permission,
            direct.Any(d => d.DatabaseId == k.DatabaseId && d.Permission == k.Permission),
            viaGroups.Where(v => v.DatabaseId == k.DatabaseId && v.Permission == k.Permission)
                .Select(v => v.Group).ToList());
    }).ToList();

    return TypedResults.Ok(new UserRoleListResponse(items));
}
```

Replace the records:

```csharp
public sealed record EffectiveUserRoleItem(
    DatabaseId DatabaseId, string DatabaseDisplayName, string ServerName, string Permission,
    bool FromDirect, IReadOnlyList<GroupRef> FromGroups);
public sealed record UserRoleListResponse(IReadOnlyList<EffectiveUserRoleItem> Roles);
```

Apply the symmetric change to `ListByDatabase` (key on `(UserId, Permission)` for one database, join `ExternalLogins` for email/name), producing:

```csharp
public sealed record EffectiveDatabaseRoleItem(
    UserId UserId, string? UserEmail, string? UserName, string Permission,
    bool FromDirect, IReadOnlyList<GroupRef> FromGroups);
public sealed record DatabaseRoleListResponse(IReadOnlyList<EffectiveDatabaseRoleItem> Roles);
```

Add `using SluiceBase.Api.Auth;`.

- [ ] **Step 7: Update broken test deserializers**

In `tests/IntegrationTests/Supports/PermissionTestHelper.cs`, `ListUserBody`'s user record currently expects `string[] Permissions` (or similar). Update the nested record so `Permissions` is `IReadOnlyList<EffPerm>` where `EffPerm(string Permission, bool FromDirect, ...)`, OR — if the helper only reads `Id`/`Email` — drop the `Permissions` property from its DTO entirely. Apply the same fix to any assertion found in Step 1 (e.g. a test that asserted a user's `permissions` contained a string now asserts `.Any(p => p.Permission == ...)`).

- [ ] **Step 8: Run the affected tests**

Run: `dotnet test tests/IntegrationTests --filter "AccessGroupEndpointTests|AdminPermissionTests|MeEndpointTests|DatabaseRoleEndpointTests|PermissionCatalogTests"`
Expected: PASS.

- [ ] **Step 9: Regenerate OpenAPI + schema**

```bash
dotnet build src/SluiceBase.Api
cd src/frontend && npm run gen:api && cd ../..
```
Expected: `openapi.json` and `src/frontend/src/api/schema.ts` reflect `EffectivePermission`/`GroupRef`/effective role items.

- [ ] **Step 10: Commit**

```bash
git add src/SluiceBase.Api tests/IntegrationTests src/frontend/src/api/schema.ts
git commit -m "Enrich access read endpoints with group provenance"
```

---

## Task 6: Frontend API hooks and types

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Test: `src/frontend/src/api/__tests__/role-hooks.test.ts` (extend) or new `group-hooks.test.ts`

**Interfaces:**
- Consumes: regenerated `schema.ts` types (`EffectivePermission`, `GroupRef`, group endpoints).
- Produces hooks: `useGroups()`, `useGroup(groupId)`, `useCreateGroup()`, `useUpdateGroup()`, `useDeleteGroup()`, `useAddGroupMember()`, `useRemoveGroupMember()`, `useAssignGroupPermission()`, `useRemoveGroupPermission()`, `useAssignGroupDatabaseRole()`, `useRemoveGroupDatabaseRole()`. Existing `useUsers`, `useUserRoles`, `useDatabaseRoles` now return provenance-shaped data.

- [ ] **Step 1: Write the failing test**

Add a test asserting `useGroups` calls `GET /api/admin/group` and returns the list, following the existing `role-hooks.test.ts` mocking pattern (inspect that file first for the exact MSW/queryClient harness used). Example shape:

```ts
it("useGroups fetches the group list", async () => {
  // arrange: mock GET /api/admin/group → { groups: [{ id: "g1", name: "Analysts", memberCount: 2 }] }
  // act: renderHook(() => useGroups(), { wrapper })
  // assert: result.current.data?.groups[0].name === "Analysts"
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npm run test -- group-hooks`
Expected: FAIL — `useGroups` is not exported.

- [ ] **Step 3: Implement the hooks**

In `src/frontend/src/api/hooks.ts`, following the existing `useUserRoles`/`useAssignUserRole` patterns (same `client`, query keys, and `useMutation` + `invalidateQueries` style), add the group hooks. Use `Array<T>` for any array types. Example for two of them (mirror for the rest):

```ts
export function useGroups() {
  return useQuery({
    queryKey: ["admin", "groups"],
    queryFn: async () => {
      const { data } = await client.GET("/api/admin/group");
      return data;
    },
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { name: string; description?: string }) => {
      const { data, error } = await client.POST("/api/admin/group", { body });
      if (error) throw error;
      return data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["admin", "groups"] }),
  });
}
```

Add `useGroup(groupId)` (`GET /api/admin/group/{groupId}`, `invalidate ["admin","group",groupId]`), `useUpdateGroup`, `useDeleteGroup`, member add/remove, permission grant/revoke, and database-role grant/revoke — each invalidating `["admin","group",groupId]` (and `["admin","groups"]` for create/delete). Export an `EffectivePermission`/`GroupRef` type alias from `schema.ts` for reuse in components.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- group-hooks`
Expected: PASS.

- [ ] **Step 5: Typecheck**

Run: `cd src/frontend && npm run build` (or the project's `tsc`/lint script)
Expected: no type errors; `Array<T>` lint passes.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/api
git commit -m "Add frontend hooks for access groups"
```

---

## Task 7: Groups tab UI

**Files:**
- Modify: `src/frontend/src/routes/_authed/access.tsx`
- Test: `src/frontend/src/routes/_authed/__tests__/groups-tab.test.tsx` (new)

**Interfaces:**
- Consumes: Task 6 hooks; `SCOPEABLE_PERMISSIONS` (already defined in `access.tsx`); `useAdminServers`, `useUsers`.
- Produces: a `Groups` tab + `GroupPanel` component.

- [ ] **Step 1: Write the failing test**

Create `groups-tab.test.tsx` asserting that, given mocked `useGroups`, the Groups tab renders a group name and a "Create group" control. Follow the harness in the existing `access-matrix.test.tsx`.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npm run test -- groups-tab`
Expected: FAIL — no Groups tab.

- [ ] **Step 3: Add the tab and panel**

In `access.tsx`, add a fourth `Tabs.Tab value="groups"` (icon `IconUsers`) and `Tabs.Panel`. Implement `GroupsTab` (left list from `useGroups` + a create modal using `useCreateGroup`) and `GroupPanel` for the selected group:
- Members: list from `useGroup(groupId).data.members`, remove buttons (`useRemoveGroupMember`), a user `Select` to add (`useAddGroupMember`).
- Global permissions: a `Checkbox` per entry in a `GLOBAL_PERMISSIONS` constant (`[{ value: "server:manage", label: "Server Manage" }, { value: "permission:manage", label: "Permission Manage" }]`), toggling `useAssignGroupPermission`/`useRemoveGroupPermission`.
- Per-database roles: reuse the server-grouped matrix layout from `UserRolePanel` (databases × `SCOPEABLE_PERMISSIONS`), toggling `useAssignGroupDatabaseRole`/`useRemoveGroupDatabaseRole`, checked when `group.databaseRoles` contains `(databaseId, permission)`.

Match existing component conventions (Mantine `Stack`/`Group`/`Table`, `notifications.show` on settle). Use `Array<T>` in all annotations.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- groups-tab`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/routes/_authed/access.tsx src/frontend/src/routes/_authed/__tests__/groups-tab.test.tsx
git commit -m "Add Groups tab to access page"
```

---

## Task 8: Provenance overlay UI

Render the four-state effective marker (none / direct / inherited / both) on the By User and By Database matrices and the Permission page, per the design's marker system.

**Files:**
- Modify: `src/frontend/src/routes/_authed/access.tsx` (`UserRolePanel`, `DatabaseRolePanel`)
- Modify: `src/frontend/src/routes/_authed/permission.tsx`
- Create: `src/frontend/src/components/EffectiveCell.tsx` — the shared marker component
- Test: `src/frontend/src/routes/_authed/__tests__/provenance.test.tsx` (new)

**Interfaces:**
- Consumes: `useUserRoles`/`useDatabaseRoles`/`useUsers` (now provenance-shaped).
- Produces: `EffectiveCell({ fromDirect, fromGroups, onToggle })` rendering the correct state.

- [ ] **Step 1: Write the failing test**

Create `provenance.test.tsx` asserting that a role with `fromDirect: false, fromGroups: [{ name: "Analysts" }]` renders a non-interactive inherited marker exposing "Analysts" (e.g. via `title`/`aria-label`/tooltip), and a role with `fromDirect: true` renders an editable checkbox.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/frontend && npm run test -- provenance`
Expected: FAIL — `EffectiveCell` does not exist.

- [ ] **Step 3: Build the `EffectiveCell` component**

`src/frontend/src/components/EffectiveCell.tsx`:

```tsx
import { Checkbox, Indicator, Tooltip } from "@mantine/core";
import { IconUsers } from "@tabler/icons-react";

export interface GroupRef {
  groupId: string;
  name: string;
}

export interface EffectiveCellProps {
  fromDirect: boolean;
  fromGroups: Array<GroupRef>;
  onToggle: (next: boolean) => void;
  ariaLabel: string;
}

export function EffectiveCell({ fromDirect, fromGroups, onToggle, ariaLabel }: EffectiveCellProps) {
  const inherited = fromGroups.length > 0;

  if (inherited && !fromDirect) {
    const via = `Inherited via ${fromGroups.map((g) => g.name).join(", ")}`;
    return (
      <Tooltip label={via} withArrow>
        <span style={{ display: "inline-flex", color: "var(--mantine-color-cyan-7)", cursor: "not-allowed" }}
          aria-label={`${ariaLabel} — ${via}`}>
          <IconUsers size={16} />
        </span>
      </Tooltip>
    );
  }

  const checkbox = (
    <Checkbox
      checked={fromDirect}
      onChange={(e) => onToggle(e.currentTarget.checked)}
      aria-label={ariaLabel}
    />
  );

  // direct AND also inherited → mark redundancy with a corner indicator
  if (fromDirect && inherited) {
    const via = `Also inherited via ${fromGroups.map((g) => g.name).join(", ")}`;
    return (
      <Tooltip label={via} withArrow>
        <Indicator color="cyan" size={8} offset={2}>{checkbox}</Indicator>
      </Tooltip>
    );
  }

  return checkbox;
}
```

- [ ] **Step 4: Use it in the matrices**

In `access.tsx`, `UserRolePanel` and `DatabaseRolePanel` currently render a `<Checkbox>` per cell driven by `isChecked(...)`. Replace each cell's checkbox with `<EffectiveCell ... />`, deriving `fromDirect`/`fromGroups` from the now-enriched role rows. Update the `isChecked`/lookup helpers to find the matching role row and read `fromDirect` + `fromGroups`; `onToggle` keeps calling the existing `assign`/`remove` mutations (which still write *direct* grants). Add a small legend above each matrix mirroring the three marker states.

- [ ] **Step 5: Use it on the permission page**

In `permission.tsx`, render each user's global permission using the same `fromDirect`/`fromGroups` data from `useUsers`, plus a textual "via «group»" source. Direct-only stays an editable toggle (existing grant/revoke mutations); inherited-only shows the read-only marker.

- [ ] **Step 6: Run the provenance test + full frontend suite**

```bash
cd src/frontend && npm run test -- provenance && npm run test
```
Expected: PASS, including the existing `access-matrix.test.tsx` (update it if it asserted the old role shape).

- [ ] **Step 7: Typecheck/lint**

Run: `cd src/frontend && npm run build`
Expected: no errors; `Array<T>` enforced.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src
git commit -m "Show group provenance across access and permission views"
```

---

## Final verification

- [ ] **Backend:** `dotnet build SluiceBase.slnx` and `dotnet test tests/SluiceBase.Core.Tests tests/IntegrationTests` (integration tests need a healthy Aspire stack; otherwise rely on CI).
- [ ] **Migration consistency:** `dotnet ef migrations has-pending-model-changes --project src/SluiceBase.Api` → no pending changes.
- [ ] **OpenAPI current:** `dotnet build src/SluiceBase.Api` then `git diff --exit-code -- src/SluiceBase.Api/openapi.json` → clean.
- [ ] **Frontend:** `cd src/frontend && npm run test && npm run build`.
- [ ] **Schema current:** `cd src/frontend && npm run gen:api` then `git diff --exit-code -- src/frontend/src/api/schema.ts` → clean.
- [ ] **Manual smoke (optional):** create a group, add a member, grant `query:execute` on a database, confirm the member can query it and the By-User matrix shows the inherited marker; remove membership and confirm access disappears.
