# Group/Role-Based Permission Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add group-based permission management so admins can bundle permissions into groups and assign users to those groups, with effective permissions being the union of individual + group grants.

**Architecture:** Five new database tables (`group`, `group_member`, `group_permission_map`, `group_database_role`, `group_column_bypass`) mirroring the existing user permission tables. Permission resolution changes to union individual and group grants. New API endpoints for group CRUD/membership under `/api/admin/group`, and existing database role + column bypass endpoints extended with `/user/` and `/group/` path segments. New frontend `/group` admin page and updates to existing `/access`, `/permission`, and sensitive column UIs.

**Tech Stack:** .NET 10, EF Core (Postgres), ASP.NET Minimal APIs, Vogen value objects, React + TypeScript + Mantine + TanStack Query

---

## File Structure

### New Files — Core Domain

| File | Responsibility |
|---|---|
| `src/SluiceBase.Core/Permissions/Group.cs` | Group entity with factory method |
| `src/SluiceBase.Core/Permissions/GroupId.cs` | Vogen value object for Group PK |
| `src/SluiceBase.Core/Permissions/GroupMember.cs` | Group membership entity |
| `src/SluiceBase.Core/Permissions/GroupMemberId.cs` | Vogen value object |
| `src/SluiceBase.Core/Permissions/GroupPermissionMap.cs` | Group global permission entity |
| `src/SluiceBase.Core/Permissions/GroupPermissionId.cs` | Vogen value object |
| `src/SluiceBase.Core/Permissions/GroupDatabaseRole.cs` | Group scoped permission entity |
| `src/SluiceBase.Core/Permissions/GroupDatabaseRoleId.cs` | Vogen value object |
| `src/SluiceBase.Core/Permissions/GroupColumnBypass.cs` | Group column bypass entity |
| `src/SluiceBase.Core/Permissions/GroupColumnBypassId.cs` | Vogen value object |

### New Files — API Layer

| File | Responsibility |
|---|---|
| `src/SluiceBase.Api/Data/Configurations/GroupConfiguration.cs` | EF config for `group` table |
| `src/SluiceBase.Api/Data/Configurations/GroupMemberConfiguration.cs` | EF config for `group_member` table |
| `src/SluiceBase.Api/Data/Configurations/GroupPermissionConfiguration.cs` | EF config for `group_permission_map` table |
| `src/SluiceBase.Api/Data/Configurations/GroupDatabaseRoleConfiguration.cs` | EF config for `group_database_role` table |
| `src/SluiceBase.Api/Data/Configurations/GroupColumnBypassConfiguration.cs` | EF config for `group_column_bypass` table |
| `src/SluiceBase.Api/Endpoints/GroupEndpoints.cs` | Group CRUD, membership, and group permission endpoints |

### New Files — Tests

| File | Responsibility |
|---|---|
| `tests/IntegrationTests/GroupEndpointTests.cs` | Integration tests for group CRUD, membership, permissions |
| `tests/IntegrationTests/Supports/GroupTestHelper.cs` | Test helpers for group operations |

### New Files — Frontend

| File | Responsibility |
|---|---|
| `src/frontend/src/routes/_authed/group.tsx` | Group admin page (list + detail views) |

### Modified Files

| File | Change |
|---|---|
| `src/SluiceBase.Core/Permissions/Permissions.cs` | Add `GroupManage` constant and add it to `Global` set |
| `src/SluiceBase.Api/Data/AppDbContext.cs` | Add DbSets for five new entities |
| `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` | Register `GroupEndpoints.Map(app)` |
| `src/SluiceBase.Api/Auth/CurrentUserAccessor.cs` | Load group-sourced global permissions into user |
| `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs` | Include group-sourced permissions in `/api/me` response |
| `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs` | Restructure DELETE routes with `/user/` segment; add group assignment/removal; update list to include group entries |
| `src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs` | Restructure DELETE bypass route with `/user/` segment; add group bypass grant/revoke; update list to include group entries |
| `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` | Expand role check and bypass check to union with group grants |
| `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` | Expand role checks to union with group grants |
| `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs` | Include group memberships in `ListUsers` response |
| `src/frontend/src/auth/permission.ts` | Add `"group:manage"` to Permission type |
| `src/frontend/src/api/hooks.ts` | Add group hooks; update DELETE URLs for roles/bypasses with `/user/` segment |
| `src/frontend/src/routes/_authed/access.tsx` | Show group entries in role lists and bypass lists; add group target option to assign forms |
| `src/frontend/src/routes/_authed/permission.tsx` | Add group global permission section |
| `tests/IntegrationTests/Supports/DatabaseRoleTestHelper.cs` | Update `RemoveRoleAsync` URL to include `/user/` segment |
| `tests/IntegrationTests/Supports/PermissionTestHelper.cs` | Update `RevokeAllDatabaseRolesAsync` URL to include `/user/` segment |
| `tests/IntegrationTests/DatabaseRoleEndpointTests.cs` | Update DELETE URLs in tests to include `/user/` segment |
| `tests/IntegrationTests/SensitiveColumnEndpointTests.cs` | Update DELETE bypass URLs to include `/user/` segment |

---

### Task 1: Add `group:manage` Permission Constant

**Files:**
- Modify: `src/SluiceBase.Core/Permissions/Permissions.cs`

- [ ] **Step 1: Add the constant and register it**

In `src/SluiceBase.Core/Permissions/Permissions.cs`, add the new constant after `ServerManage` (line 6) and add it to the `Global` set:

```csharp
public const string GroupManage = "group:manage";
```

Add `GroupManage` to the `Global` HashSet initializer (after `ServerManage`, line 17):

```csharp
public static readonly IReadOnlySet<string> Global = new HashSet<string>
{
    PermissionManage,
    ServerManage,
    GroupManage,
};
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Permissions/Permissions.cs
git commit -m "$(cat <<'EOF'
Add group:manage global permission constant
EOF
)"
```

---

### Task 2: Create Group Domain Entities and Value Objects

**Files:**
- Create: `src/SluiceBase.Core/Permissions/GroupId.cs`
- Create: `src/SluiceBase.Core/Permissions/Group.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupMemberId.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupMember.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupPermissionId.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupPermissionMap.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupDatabaseRoleId.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupDatabaseRole.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupColumnBypassId.cs`
- Create: `src/SluiceBase.Core/Permissions/GroupColumnBypass.cs`

- [ ] **Step 1: Create Vogen value object IDs**

`src/SluiceBase.Core/Permissions/GroupId.cs`:
```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct GroupId;
```

`src/SluiceBase.Core/Permissions/GroupMemberId.cs`:
```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct GroupMemberId;
```

`src/SluiceBase.Core/Permissions/GroupPermissionId.cs`:
```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct GroupPermissionId;
```

`src/SluiceBase.Core/Permissions/GroupDatabaseRoleId.cs`:
```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct GroupDatabaseRoleId;
```

`src/SluiceBase.Core/Permissions/GroupColumnBypassId.cs`:
```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct GroupColumnBypassId;
```

- [ ] **Step 2: Create Group entity**

`src/SluiceBase.Core/Permissions/Group.cs`:
```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class Group
{
#pragma warning disable CS8618
    private Group() { }
#pragma warning restore CS8618

    private Group(
        GroupId id, string name, string? description,
        UserId createdById, DateTimeOffset at)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedById = createdById;
        CreatedAt = at;
    }

    public GroupId Id { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public UserId CreatedById { get; private set; }

    public static Group Create(
        string name, string? description,
        UserId createdById, DateTimeOffset at) =>
        new(GroupId.FromNewVersion7Guid(), name, description, createdById, at);

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
```

- [ ] **Step 3: Create GroupMember entity**

`src/SluiceBase.Core/Permissions/GroupMember.cs`:
```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupMember
{
#pragma warning disable CS8618
    private GroupMember() { }
#pragma warning restore CS8618

    private GroupMember(
        GroupMemberId id, GroupId groupId, UserId userId,
        UserId? addedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        UserId = userId;
        AddedById = addedById;
        AddedAt = at;
    }

    public GroupMemberId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public UserId UserId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public UserId? AddedById { get; private set; }

    public static GroupMember Add(
        GroupId groupId, UserId userId,
        UserId? addedById, DateTimeOffset at) =>
        new(GroupMemberId.FromNewVersion7Guid(), groupId, userId, addedById, at);
}
```

- [ ] **Step 4: Create GroupPermissionMap entity**

`src/SluiceBase.Core/Permissions/GroupPermissionMap.cs`:
```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupPermissionMap
{
#pragma warning disable CS8618
    private GroupPermissionMap() { }
#pragma warning restore CS8618

    private GroupPermissionMap(
        GroupPermissionId id, GroupId groupId, string permission,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public GroupPermissionId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static GroupPermissionMap Grant(
        GroupId groupId, string permission,
        UserId? grantedById, DateTimeOffset at) =>
        new(GroupPermissionId.FromNewVersion7Guid(), groupId, permission, grantedById, at);
}
```

- [ ] **Step 5: Create GroupDatabaseRole entity**

`src/SluiceBase.Core/Permissions/GroupDatabaseRole.cs`:
```csharp
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupDatabaseRole
{
#pragma warning disable CS8618
    private GroupDatabaseRole() { }
#pragma warning restore CS8618

    private GroupDatabaseRole(
        GroupDatabaseRoleId id, GroupId groupId, string permission,
        DatabaseId databaseId, UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        DatabaseId = databaseId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public GroupDatabaseRoleId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static GroupDatabaseRole Grant(
        GroupId groupId, string permission, DatabaseId databaseId,
        UserId? grantedById, DateTimeOffset at) =>
        new(GroupDatabaseRoleId.FromNewVersion7Guid(), groupId, permission, databaseId, grantedById, at);
}
```

- [ ] **Step 6: Create GroupColumnBypass entity**

`src/SluiceBase.Core/Permissions/GroupColumnBypass.cs`:
```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupColumnBypass
{
#pragma warning disable CS8618
    private GroupColumnBypass() { }
#pragma warning restore CS8618

    private GroupColumnBypass(
        GroupColumnBypassId id, GroupId groupId,
        SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        SensitiveColumnId = sensitiveColumnId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public GroupColumnBypassId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public SensitiveColumnId SensitiveColumnId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static GroupColumnBypass Grant(
        GroupId groupId, SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at) =>
        new(GroupColumnBypassId.FromNewVersion7Guid(), groupId, sensitiveColumnId, grantedById, at);
}
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add src/SluiceBase.Core/Permissions/Group*.cs
git commit -m "$(cat <<'EOF'
Add group domain entities and Vogen value objects
EOF
)"
```

---

### Task 3: Add EF Configurations and DbContext Registration

**Files:**
- Create: `src/SluiceBase.Api/Data/Configurations/GroupConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/GroupMemberConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/GroupPermissionConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/GroupDatabaseRoleConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/GroupColumnBypassConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Create GroupConfiguration**

`src/SluiceBase.Api/Data/Configurations/GroupConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("group");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.CreatedAt).IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

Note: `CreatedById` is not nullable on the entity but using SetNull for FK consistency with other audit FKs. Actually, `CreatedById` is a non-nullable `UserId` on the entity. To match this, use `Restrict` instead since the FK is not nullable:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("group");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.CreatedAt).IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: Create GroupMemberConfiguration**

`src/SluiceBase.Api/Data/Configurations/GroupMemberConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_member");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
        builder.Property(m => m.AddedAt).IsRequired();
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.AddedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 3: Create GroupPermissionConfiguration**

`src/SluiceBase.Api/Data/Configurations/GroupPermissionConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupPermissionConfiguration : IEntityTypeConfiguration<GroupPermissionMap>
{
    public void Configure(EntityTypeBuilder<GroupPermissionMap> builder)
    {
        builder.ToTable("group_permission_map");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => new { p.GroupId, p.Permission }).IsUnique();
        builder.Property(p => p.GrantedAt).IsRequired();
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 4: Create GroupDatabaseRoleConfiguration**

`src/SluiceBase.Api/Data/Configurations/GroupDatabaseRoleConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupDatabaseRoleConfiguration : IEntityTypeConfiguration<GroupDatabaseRole>
{
    public void Configure(EntityTypeBuilder<GroupDatabaseRole> builder)
    {
        builder.ToTable("group_database_role");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.GroupId, r.Permission, r.DatabaseId }).IsUnique();
        builder.Property(r => r.GrantedAt).IsRequired();
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(r => r.GroupId)
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

- [ ] **Step 5: Create GroupColumnBypassConfiguration**

`src/SluiceBase.Api/Data/Configurations/GroupColumnBypassConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupColumnBypassConfiguration : IEntityTypeConfiguration<GroupColumnBypass>
{
    public void Configure(EntityTypeBuilder<GroupColumnBypass> builder)
    {
        builder.ToTable("group_column_bypass");
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.GroupId, b.SensitiveColumnId }).IsUnique();
        builder.Property(b => b.GrantedAt).IsRequired();
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(b => b.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SensitiveColumn>()
            .WithMany()
            .HasForeignKey(b => b.SensitiveColumnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 6: Add DbSets to AppDbContext**

In `src/SluiceBase.Api/Data/AppDbContext.cs`, add after `UserColumnBypasses` (line 23):

```csharp
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupPermissionMap> GroupPermissions => Set<GroupPermissionMap>();
    public DbSet<GroupDatabaseRole> GroupDatabaseRoles => Set<GroupDatabaseRole>();
    public DbSet<GroupColumnBypass> GroupColumnBypasses => Set<GroupColumnBypass>();
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add src/SluiceBase.Api/Data/Configurations/Group*.cs src/SluiceBase.Api/Data/AppDbContext.cs
git commit -m "$(cat <<'EOF'
Add EF configurations and DbSets for group entities
EOF
)"
```

---

### Task 4: Create EF Core Migration

**Files:**
- Create: `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddGroups.cs` (auto-generated)

- [ ] **Step 1: Generate migration**

Run from the repo root:
```bash
dotnet ef migrations add AddGroups --project src/SluiceBase.Api/SluiceBase.Api.csproj
```
Expected: Migration files created in `src/SluiceBase.Api/Data/Migrations/`

- [ ] **Step 2: Review the generated migration**

Open the generated migration file and verify it creates exactly these tables with the correct columns, indexes, and foreign keys:
- `group` (id, name, description, created_at, created_by_id) with unique index on name
- `group_member` (id, group_id, user_id, added_at, added_by_id) with unique index on (group_id, user_id)
- `group_permission_map` (id, group_id, permission, granted_at, granted_by_id) with unique index on (group_id, permission)
- `group_database_role` (id, group_id, permission, database_id, granted_at, granted_by_id) with unique index on (group_id, permission, database_id)
- `group_column_bypass` (id, group_id, sensitive_column_id, granted_at, granted_by_id) with unique index on (group_id, sensitive_column_id)

Do NOT manually edit the migration file.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Data/Migrations/
git commit -m "$(cat <<'EOF'
Add EF migration for group tables
EOF
)"
```

---

### Task 5: Group CRUD and Membership Endpoints

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/GroupEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`

- [ ] **Step 1: Create GroupEndpoints with CRUD and membership**

`src/SluiceBase.Api/Endpoints/GroupEndpoints.cs`:
```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class GroupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/group")
            .RequireAuthorization(Permissions.GroupManage);

        admin.MapGet("/", ListGroups).WithName("ListGroups");
        admin.MapPost("/", CreateGroup).WithName("CreateGroup");
        admin.MapPut("/{groupId}", UpdateGroup).WithName("UpdateGroup");
        admin.MapDelete("/{groupId}", DeleteGroup).WithName("DeleteGroup");

        admin.MapGet("/{groupId}/member", ListMembers).WithName("ListGroupMembers");
        admin.MapPost("/{groupId}/member", AddMember).WithName("AddGroupMember");
        admin.MapDelete("/{groupId}/member/{userId}", RemoveMember).WithName("RemoveGroupMember");

        admin.MapGet("/{groupId}/permission", ListGroupPermissions).WithName("ListGroupPermissions");
        admin.MapPost("/{groupId}/permission", GrantGroupPermission).WithName("GrantGroupPermission");
        admin.MapDelete("/{groupId}/permission/{permission}", RevokeGroupPermission).WithName("RevokeGroupPermission");
    }

    // ── group CRUD ───────────────────────────────────────────────────────────

    private static async Task<Ok<GroupListResponse>> ListGroups(
        AppDbContext db, CancellationToken ct)
    {
        var groups = await db.Groups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupItem(
                g.Id,
                g.Name,
                g.Description,
                g.CreatedAt,
                db.GroupMembers.Count(m => m.GroupId == g.Id)))
            .ToListAsync(ct);

        return TypedResults.Ok(new GroupListResponse(groups));
    }

    private static async Task<Results<ValidationProblem, Created<GroupItem>>> CreateGroup(
        CreateGroupRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var nameExists = await db.Groups.AnyAsync(g => g.Name == req.Name, ct);
        if (nameExists)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"A group named '{req.Name}' already exists."]
            });
        }

        var actor = await currentUser.GetAsync(ct);
        var group = Group.Create(req.Name, req.Description, actor!.Id, clock.GetUtcNow());
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/admin/group/{group.Id}",
            new GroupItem(group.Id, group.Name, group.Description, group.CreatedAt, 0));
    }

    private static async Task<Results<ValidationProblem, NotFound, Ok<GroupItem>>> UpdateGroup(
        GroupId groupId,
        UpdateGroupRequest req,
        AppDbContext db,
        CancellationToken ct)
    {
        var group = await db.Groups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return TypedResults.NotFound();
        }

        var nameConflict = await db.Groups.AnyAsync(g => g.Name == req.Name && g.Id != groupId, ct);
        if (nameConflict)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"A group named '{req.Name}' already exists."]
            });
        }

        group.Update(req.Name, req.Description);
        await db.SaveChangesAsync(ct);

        var memberCount = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, ct);
        return TypedResults.Ok(new GroupItem(group.Id, group.Name, group.Description, group.CreatedAt, memberCount));
    }

    private static async Task<NoContent> DeleteGroup(
        GroupId groupId,
        AppDbContext db,
        CancellationToken ct)
    {
        var group = await db.Groups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is not null)
        {
            db.Groups.Remove(group);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── membership ───────────────────────────────────────────────────────────

    private static async Task<Results<NotFound, Ok<GroupMemberListResponse>>> ListMembers(
        GroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var members = await db.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Join(db.ExternalLogins,
                m => m.UserId,
                l => l.UserId,
                (m, l) => new GroupMemberItem(m.Id, m.UserId, l.Email, l.Name, m.AddedAt, m.AddedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new GroupMemberListResponse(members));
    }

    private static async Task<Results<NotFound, Ok, Created>> AddMember(
        GroupId groupId,
        AddGroupMemberRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.GroupMembers.AnyAsync(
            m => m.GroupId == groupId && m.UserId == req.UserId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.GroupMembers.Add(GroupMember.Add(groupId, req.UserId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/group/{groupId}/member");
    }

    private static async Task<NoContent> RemoveMember(
        GroupId groupId,
        UserId userId,
        AppDbContext db,
        CancellationToken ct)
    {
        var member = await db.GroupMembers.SingleOrDefaultAsync(
            m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is not null)
        {
            db.GroupMembers.Remove(member);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── group global permissions ─────────────────────────────────────────────

    private static async Task<Results<NotFound, Ok<GroupPermissionListResponse>>> ListGroupPermissions(
        GroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var permissions = await db.GroupPermissions
            .AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .Select(p => new GroupPermissionItem(p.Id, p.Permission, p.GrantedAt, p.GrantedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new GroupPermissionListResponse(permissions));
    }

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> GrantGroupPermission(
        GroupId groupId,
        GrantGroupPermissionRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Global.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a known global permission."]
            });
        }

        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.GroupPermissions.AnyAsync(
            p => p.GroupId == groupId && p.Permission == req.Permission, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.GroupPermissions.Add(GroupPermissionMap.Grant(
            groupId, req.Permission, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/group/{groupId}/permission");
    }

    private static async Task<NoContent> RevokeGroupPermission(
        GroupId groupId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var grant = await db.GroupPermissions.SingleOrDefaultAsync(
            p => p.GroupId == groupId && p.Permission == permission, ct);
        if (grant is not null)
        {
            db.GroupPermissions.Remove(grant);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── request / response records ───────────────────────────────────────────

    public sealed record CreateGroupRequest(string Name, string? Description);
    public sealed record UpdateGroupRequest(string Name, string? Description);
    public sealed record AddGroupMemberRequest(UserId UserId);
    public sealed record GrantGroupPermissionRequest(string Permission);

    public sealed record GroupItem(
        GroupId Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        int MemberCount);

    public sealed record GroupListResponse(IReadOnlyList<GroupItem> Groups);

    public sealed record GroupMemberItem(
        GroupMemberId Id,
        UserId UserId,
        string? UserEmail,
        string? UserName,
        DateTimeOffset AddedAt,
        UserId? AddedById);

    public sealed record GroupMemberListResponse(IReadOnlyList<GroupMemberItem> Members);

    public sealed record GroupPermissionItem(
        GroupPermissionId Id,
        string Permission,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record GroupPermissionListResponse(IReadOnlyList<GroupPermissionItem> Permissions);
}
```

- [ ] **Step 2: Register in EndpointMapper**

In `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`, add after `SensitiveColumnEndpoints.Map(app);` (line 11):

```csharp
        GroupEndpoints.Map(app);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/GroupEndpoints.cs src/SluiceBase.Api/Endpoints/EndpointMapper.cs
git commit -m "$(cat <<'EOF'
Add group CRUD, membership, and permission endpoints
EOF
)"
```

---

### Task 6: Update Permission Resolution — CurrentUserAccessor and /api/me

**Files:**
- Modify: `src/SluiceBase.Api/Auth/CurrentUserAccessor.cs`
- Modify: `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs`

- [ ] **Step 1: Update CurrentUserAccessor to load group-sourced global permissions**

In `src/SluiceBase.Api/Auth/CurrentUserAccessor.cs`, after loading the user (line 38-41), add group permission loading. Replace the query block:

```csharp
        _cached = await db.Users
            .Include(u => u.Permissions)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, ct);
```

With:

```csharp
        var user = await db.Users
            .Include(u => u.Permissions)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, ct);

        if (user is not null)
        {
            var groupPermissions = await db.GroupPermissions
                .AsNoTracking()
                .Where(gp => db.GroupMembers.Any(
                    gm => gm.GroupId == gp.GroupId && gm.UserId == userId))
                .ToListAsync(ct);

            foreach (var gp in groupPermissions)
            {
                if (!user.HasPermission(gp.Permission))
                {
                    user.Permissions.Add(UserPermissionMap.Grant(
                        userId, gp.Permission, gp.GrantedById, gp.GrantedAt));
                }
            }
        }

        _cached = user;
```

Wait — `AsNoTracking` returns a detached entity, so `user.Permissions` is an `IList<UserPermissionMap>` that was materialized. But `UserPermissionMap.Grant` creates a real entity. Since the user is detached (AsNoTracking), adding to the list won't track anything in EF — this is purely in-memory for the `HasPermission` check. This works.

Actually, the list is initialized as `[]` on User, and `Include` populates it. Since it's AsNoTracking, adding to the list is safe and won't affect the DB.

- [ ] **Step 2: Update /api/me to include group-sourced scoped permissions**

In `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs`, update the `/api/me` handler (lines 54-64). Replace:

```csharp
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
```

With:

```csharp
                    var userGroupIds = db.GroupMembers
                        .Where(gm => gm.UserId == user.Id)
                        .Select(gm => gm.GroupId);

                    var databaseRolePermissions = await db.UserDatabaseRoles
                        .AsNoTracking()
                        .Where(r => r.UserId == user.Id)
                        .Select(r => r.Permission)
                        .Union(
                            db.GroupDatabaseRoles
                                .Where(gr => userGroupIds.Contains(gr.GroupId))
                                .Select(gr => gr.Permission))
                        .Distinct()
                        .ToListAsync(ct);

                    var allPermissions = user.Permissions.Select(p => p.Permission)
                        .Concat(databaseRolePermissions)
                        .Distinct()
                        .ToArray();
```

Note: `user.Permissions` already includes group-sourced global permissions from the updated `CurrentUserAccessor`, so only the scoped permissions need the union here.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Auth/CurrentUserAccessor.cs src/SluiceBase.Api/Endpoints/AuthEndpoints.cs
git commit -m "$(cat <<'EOF'
Include group-sourced permissions in resolution and /api/me
EOF
)"
```

---

### Task 7: Update Scoped Permission Checks in Query and Update Endpoints

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`

- [ ] **Step 1: Update QueryEndpoints role check**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, replace the role check (lines 50-55):

```csharp
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == database.Id, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
        }
```

With:

```csharp
        var userGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == user!.Id)
            .Select(gm => gm.GroupId);

        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == database.Id, ct)
            || await db.GroupDatabaseRoles.AnyAsync(
                r => userGroupIds.Contains(r.GroupId) && r.Permission == Permissions.QueryExecute && r.DatabaseId == database.Id, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
        }
```

- [ ] **Step 2: Update QueryEndpoints bypass check**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, replace the bypass check (lines 79-83):

```csharp
                var bypassedIds = await db.UserColumnBypasses
                    .AsNoTracking()
                    .Where(b => b.UserId == user!.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
                    .Select(b => b.SensitiveColumnId)
                    .ToListAsync(ct);
```

With:

```csharp
                var bypassedIds = await db.UserColumnBypasses
                    .AsNoTracking()
                    .Where(b => b.UserId == user!.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
                    .Select(b => b.SensitiveColumnId)
                    .Union(
                        db.GroupColumnBypasses
                            .Where(gb => userGroupIds.Contains(gb.GroupId) && sensitiveColumnIds.Contains(gb.SensitiveColumnId))
                            .Select(gb => gb.SensitiveColumnId))
                    .ToListAsync(ct);
```

Note: `userGroupIds` is defined earlier in the method. Move the `userGroupIds` definition to the top of the method (just after loading the user) so it's available for both checks.

- [ ] **Step 3: Update QueryEndpoints history — audit database check**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, update the `GetHistory` method (lines 183-193). Replace:

```csharp
        var auditDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id && r.Permission == Permissions.QueryAudit)
            .Select(r => r.DatabaseId)
            .ToListAsync(ct);

        var anyRoleDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id)
            .Select(r => r.DatabaseId)
            .Distinct()
            .ToListAsync(ct);
```

With:

```csharp
        var userGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == user!.Id)
            .Select(gm => gm.GroupId);

        var auditDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id && r.Permission == Permissions.QueryAudit)
            .Select(r => r.DatabaseId)
            .Union(
                db.GroupDatabaseRoles
                    .Where(gr => userGroupIds.Contains(gr.GroupId) && gr.Permission == Permissions.QueryAudit)
                    .Select(gr => gr.DatabaseId))
            .ToListAsync(ct);

        var anyRoleDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id)
            .Select(r => r.DatabaseId)
            .Union(
                db.GroupDatabaseRoles
                    .Where(gr => userGroupIds.Contains(gr.GroupId))
                    .Select(gr => gr.DatabaseId))
            .Distinct()
            .ToListAsync(ct);
```

- [ ] **Step 4: Update UpdateEndpoints — all permission checks**

In `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`, apply the same pattern to every `db.UserDatabaseRoles.AnyAsync(...)` check. There are 5 occurrences:

1. **Submit** (line 54-55): Add group check for `UpdateSubmit`
2. **List** (lines 98-105): Add group union for `allowedDatabaseIds`
3. **Get** (lines 160-164): Add group check for the combined OR permission check
4. **Approve/Reject** (lines 198-199, 243-244): Add group check for `UpdateApprove`
5. **Cancel** (lines 288-289): Add group check for `UpdateSubmit`
6. **Execute** (lines 356-357): Add group check for `UpdateExecute`

For each, define `userGroupIds` at the top of the method and use the same `|| await db.GroupDatabaseRoles.AnyAsync(...)` pattern.

For the **List** method, change the `allowedDatabaseIds` query:

```csharp
        var userGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == user!.Id)
            .Select(gm => gm.GroupId);

        var allowedDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id &&
                        (r.Permission == Permissions.UpdateSubmit ||
                         r.Permission == Permissions.UpdateApprove ||
                         r.Permission == Permissions.UpdateExecute))
            .Select(r => r.DatabaseId)
            .Union(
                db.GroupDatabaseRoles
                    .Where(gr => userGroupIds.Contains(gr.GroupId) &&
                                 (gr.Permission == Permissions.UpdateSubmit ||
                                  gr.Permission == Permissions.UpdateApprove ||
                                  gr.Permission == Permissions.UpdateExecute))
                    .Select(gr => gr.DatabaseId))
            .Distinct()
            .ToListAsync(ct);
```

For the **Get** method:

```csharp
            var hasRole = await db.UserDatabaseRoles.AnyAsync(
                r => r.UserId == user!.Id && r.DatabaseId == request.DatabaseId &&
                     (r.Permission == Permissions.UpdateSubmit ||
                      r.Permission == Permissions.UpdateApprove ||
                      r.Permission == Permissions.UpdateExecute), ct)
                || await db.GroupDatabaseRoles.AnyAsync(
                    r => userGroupIds.Contains(r.GroupId) && r.DatabaseId == request.DatabaseId &&
                         (r.Permission == Permissions.UpdateSubmit ||
                          r.Permission == Permissions.UpdateApprove ||
                          r.Permission == Permissions.UpdateExecute), ct);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/QueryEndpoints.cs src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs
git commit -m "$(cat <<'EOF'
Expand scoped permission checks to include group grants
EOF
)"
```

---

### Task 8: Restructure Database Role DELETE Routes and Add Group Support

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs`

- [ ] **Step 1: Update route registration and add group routes**

Replace the route registration block (lines 13-31) with:

```csharp
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/server", ListServers).WithName("AdminListServers");

        admin.MapGet("/database/{databaseId}/role", ListByDatabase)
            .WithName("ListDatabaseRoles");
        admin.MapPost("/database/{databaseId}/role", AssignByDatabase)
            .WithName("AssignDatabaseRole");
        admin.MapDelete("/database/{databaseId}/role/user/{userId}/{permission}", RemoveUserRole)
            .WithName("RemoveUserDatabaseRole");
        admin.MapDelete("/database/{databaseId}/role/group/{groupId}/{permission}", RemoveGroupRole)
            .WithName("RemoveGroupDatabaseRole")
            .RequireAuthorization(Permissions.GroupManage);

        admin.MapGet("/user/{userId}/role", ListByUser)
            .WithName("ListUserRoles");
        admin.MapPost("/user/{userId}/role", AssignByUser)
            .WithName("AssignUserRole");
    }
```

- [ ] **Step 2: Rename RemoveRole to RemoveUserRole**

Rename the existing `RemoveRole` method (line 120) to `RemoveUserRole`. Update the parameter names to be explicit:

```csharp
    private static async Task<NoContent> RemoveUserRole(
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
```

- [ ] **Step 3: Add RemoveGroupRole method**

Add after `RemoveUserRole`:

```csharp
    private static async Task<NoContent> RemoveGroupRole(
        DatabaseId databaseId,
        GroupId groupId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var role = await db.GroupDatabaseRoles.SingleOrDefaultAsync(
            r => r.DatabaseId == databaseId && r.GroupId == groupId && r.Permission == permission, ct);
        if (role is not null)
        {
            db.GroupDatabaseRoles.Remove(role);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }
```

- [ ] **Step 4: Update ListByDatabase to include group entries**

Replace the `ListByDatabase` method with a version that returns both user and group entries:

```csharp
    private static async Task<Ok<DatabaseRoleListResponse>> ListByDatabase(
        DatabaseId databaseId, AppDbContext db, CancellationToken ct)
    {
        var userRoles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.DatabaseId == databaseId)
            .Join(db.ExternalLogins,
                r => r.UserId,
                l => l.UserId,
                (r, l) => new DatabaseRoleItem("user", r.UserId, null, l.Email, l.Name, r.Permission, r.GrantedAt, r.GrantedById))
            .ToListAsync(ct);

        var groupRoles = await db.GroupDatabaseRoles
            .AsNoTracking()
            .Where(r => r.DatabaseId == databaseId)
            .Join(db.Groups,
                r => r.GroupId,
                g => g.Id,
                (r, g) => new DatabaseRoleItem("group", null, r.GroupId, g.Name, null, r.Permission, r.GrantedAt, r.GrantedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new DatabaseRoleListResponse([.. userRoles, .. groupRoles]));
    }
```

- [ ] **Step 5: Update response records**

Replace the `DatabaseRoleItem` record with a new shape that supports both user and group entries:

```csharp
    public sealed record DatabaseRoleItem(
        string Type,
        UserId? UserId,
        GroupId? GroupId,
        string? DisplayName,
        string? SecondaryName,
        string Permission,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);
```

Note: `DisplayName` is email for users, group name for groups. `SecondaryName` is user name (nullable) for user entries, null for group entries.

- [ ] **Step 6: Update authorization on shared endpoints**

The route group currently applies `RequireAuthorization(Permissions.PermissionManage)` to all routes. The GET list and POST assign endpoints need to be accessible to users with either `permission:manage` OR `group:manage`. 

Change the route group to use `RequireAuthorization()` (just require authentication), then apply specific authorization per route:

- `ListByDatabase` and `ListServers`: add `.RequireAuthorization(new AnyPermissionRequirement([Permissions.PermissionManage, Permissions.GroupManage]))` — users with either permission can view the list
- `AssignByDatabase`: keep `.RequireAuthorization()` (authentication only) and check the authorization in the handler: if `req.UserId` is provided, require `permission:manage`; if `req.GroupId` is provided, require `group:manage`
- `RemoveUserRole`: `.RequireAuthorization(Permissions.PermissionManage)`
- `RemoveGroupRole`: `.RequireAuthorization(Permissions.GroupManage)`
- `ListByUser`, `AssignByUser`: `.RequireAuthorization(Permissions.PermissionManage)`

Update `AssignByDatabase` to accept either `userId` or `groupId`:

```csharp
    public sealed record AssignDatabaseRoleRequest(UserId? UserId, GroupId? GroupId, string Permission);
```

Add validation in `AssignByDatabase` that exactly one of `UserId`/`GroupId` is provided, and create either a `UserDatabaseRole` or `GroupDatabaseRole` accordingly. Check the appropriate permission (`permission:manage` for user target, `group:manage` for group target) using `user.HasPermission()`.

- [ ] **Step 7: Update ListByUser to include source indicator**

The spec requires `GET /api/admin/user/{userId}/role` to indicate whether each role is `"direct"` or from a `"group"` (with group name).

Update `UserRoleItem` to include source:

```csharp
    public sealed record UserRoleItem(
        DatabaseId DatabaseId,
        string DatabaseDisplayName,
        string ServerName,
        string Permission,
        DateTimeOffset GrantedAt,
        string Source,
        string? GroupName);
```

Update `ListByUser` to query both `UserDatabaseRoles` and `GroupDatabaseRoles` (via group membership), marking each entry with `Source = "direct"` or `Source = "group"` respectively:

```csharp
    private static async Task<Ok<UserRoleListResponse>> ListByUser(
        UserId userId, AppDbContext db, CancellationToken ct)
    {
        var directRoles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new UserRoleItem(
                r.DatabaseId,
                db.Databases.Where(d => d.Id == r.DatabaseId).Select(d => d.DisplayName).FirstOrDefault() ?? "",
                db.Databases.Where(d => d.Id == r.DatabaseId)
                    .Select(d => db.Servers.Where(s => s.Id == d.ServerId).Select(s => s.Name).FirstOrDefault())
                    .FirstOrDefault() ?? "",
                r.Permission,
                r.GrantedAt,
                "direct",
                null))
            .ToListAsync(ct);

        var userGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == userId)
            .Select(gm => gm.GroupId);

        var groupRoles = await db.GroupDatabaseRoles
            .AsNoTracking()
            .Where(r => userGroupIds.Contains(r.GroupId))
            .Join(db.Groups, r => r.GroupId, g => g.Id, (r, g) => new { r, g.Name })
            .Select(x => new UserRoleItem(
                x.r.DatabaseId,
                db.Databases.Where(d => d.Id == x.r.DatabaseId).Select(d => d.DisplayName).FirstOrDefault() ?? "",
                db.Databases.Where(d => d.Id == x.r.DatabaseId)
                    .Select(d => db.Servers.Where(s => s.Id == d.ServerId).Select(s => s.Name).FirstOrDefault())
                    .FirstOrDefault() ?? "",
                x.r.Permission,
                x.r.GrantedAt,
                "group",
                x.Name))
            .ToListAsync(ct);

        return TypedResults.Ok(new UserRoleListResponse([.. directRoles, .. groupRoles]));
    }
```

Remove the `Id` field from `UserRoleItem` since it's no longer meaningful when mixing sources.

- [ ] **Step 8: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs
git commit -m "$(cat <<'EOF'
Restructure database role DELETE routes and add group support
EOF
)"
```

---

### Task 9: Restructure Sensitive Column Bypass Routes and Add Group Support

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs`

- [ ] **Step 1: Update route registration**

Replace the bypass route registrations (lines 25-28) with:

```csharp
        admin.MapPost("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass", GrantBypass)
            .WithName("GrantColumnBypass");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/user/{userId}", RevokeUserBypass)
            .WithName("RevokeUserColumnBypass");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/group/{groupId}", RevokeGroupBypass)
            .WithName("RevokeGroupColumnBypass")
            .RequireAuthorization(Permissions.GroupManage);
```

- [ ] **Step 2: Update GrantBypass to support group target**

Replace the `GrantBypassRequest` record and `GrantBypass` method to accept either userId or groupId:

```csharp
    public sealed record GrantBypassRequest(UserId? UserId, GroupId? GroupId);
```

Update `GrantBypass` to handle both cases:

```csharp
    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> GrantBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        GrantBypassRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if ((req.UserId is null && req.GroupId is null) || (req.UserId is not null && req.GroupId is not null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Exactly one of 'userId' or 'groupId' must be provided."]
            });
        }

        var columnExists = await db.SensitiveColumns.AnyAsync(
            c => c.Id == sensitiveColumnId && c.DatabaseId == databaseId, ct);
        if (!columnExists)
        {
            return TypedResults.NotFound();
        }

        var actor = await currentUser.GetAsync(ct);

        if (req.UserId is { } userId)
        {
            var userExists = await db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists)
            {
                return TypedResults.NotFound();
            }

            var existing = await db.UserColumnBypasses.AnyAsync(
                b => b.UserId == userId && b.SensitiveColumnId == sensitiveColumnId, ct);
            if (existing)
            {
                return TypedResults.Ok();
            }

            db.UserColumnBypasses.Add(UserColumnBypass.Grant(
                userId, sensitiveColumnId, actor?.Id, clock.GetUtcNow()));
        }
        else
        {
            var groupId = req.GroupId!.Value;
            var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
            if (!groupExists)
            {
                return TypedResults.NotFound();
            }

            var existing = await db.GroupColumnBypasses.AnyAsync(
                b => b.GroupId == groupId && b.SensitiveColumnId == sensitiveColumnId, ct);
            if (existing)
            {
                return TypedResults.Ok();
            }

            db.GroupColumnBypasses.Add(GroupColumnBypass.Grant(
                groupId, sensitiveColumnId, actor?.Id, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created(
            $"/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass");
    }
```

- [ ] **Step 3: Rename RevokeBypass to RevokeUserBypass and add RevokeGroupBypass**

Rename existing `RevokeBypass` to `RevokeUserBypass`:

```csharp
    private static async Task<NoContent> RevokeUserBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        UserId userId,
        AppDbContext db,
        CancellationToken ct)
    {
        var bypass = await db.UserColumnBypasses.SingleOrDefaultAsync(
            b => b.SensitiveColumnId == sensitiveColumnId && b.UserId == userId, ct);
        if (bypass is not null)
        {
            db.UserColumnBypasses.Remove(bypass);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }
```

Add `RevokeGroupBypass`:

```csharp
    private static async Task<NoContent> RevokeGroupBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        GroupId groupId,
        AppDbContext db,
        CancellationToken ct)
    {
        var bypass = await db.GroupColumnBypasses.SingleOrDefaultAsync(
            b => b.SensitiveColumnId == sensitiveColumnId && b.GroupId == groupId, ct);
        if (bypass is not null)
        {
            db.GroupColumnBypasses.Remove(bypass);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }
```

- [ ] **Step 4: Update ListByDatabase to include group bypasses**

Update the `ListByDatabase` method's bypass loading to include group bypasses. Add a `GroupBypassItem` record and update `SensitiveColumnItem` to include group bypasses:

Add new records:

```csharp
    public sealed record GroupBypassItem(
        GroupColumnBypassId Id,
        GroupId GroupId,
        string? GroupName,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);
```

Update `SensitiveColumnItem` to include group bypasses:

```csharp
    public sealed record SensitiveColumnItem(
        SensitiveColumnId Id,
        string SchemaName,
        string TableName,
        string ColumnName,
        DateTimeOffset MarkedAt,
        UserId? MarkedById,
        IReadOnlyList<BypassItem> Bypasses,
        IReadOnlyList<GroupBypassItem> GroupBypasses);
```

In the `ListByDatabase` method, add group bypass loading after the user bypass loading:

```csharp
        var rawGroupBypasses = await db.GroupColumnBypasses
            .AsNoTracking()
            .Where(b => columnIds.Contains(b.SensitiveColumnId))
            .Join(db.Groups,
                b => b.GroupId, g => g.Id,
                (b, g) => new { b.Id, b.GroupId, g.Name, b.GrantedAt, b.GrantedById, b.SensitiveColumnId })
            .ToListAsync(ct);

        var groupBypassesBySensitiveColumnId = rawGroupBypasses
            .GroupBy(b => b.SensitiveColumnId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<GroupBypassItem>)g.Select(b =>
                    new GroupBypassItem(b.Id, b.GroupId, b.Name, b.GrantedAt, b.GrantedById)).ToList());
```

Update the column mapping to include group bypasses:

```csharp
        var items = columns.Select(c => new SensitiveColumnItem(
            c.Id, c.SchemaName, c.TableName, c.ColumnName, c.MarkedAt, c.MarkedById,
            bypassesBySensitiveColumnId.TryGetValue(c.Id, out var bs) ? bs : [],
            groupBypassesBySensitiveColumnId.TryGetValue(c.Id, out var gbs) ? gbs : []));
```

- [ ] **Step 5: Update authorization on shared endpoints**

Similar to Task 8, update the route group authorization. The `ListByDatabase` and `GrantBypass` endpoints need to accept `permission:manage` OR `group:manage`. Apply per-route authorization:

- `ListByDatabase`: `.RequireAuthorization(new AnyPermissionRequirement([Permissions.PermissionManage, Permissions.GroupManage]))`
- `GrantBypass`: check authorization in handler based on body (`userId` → `permission:manage`, `groupId` → `group:manage`)
- `MarkColumn`, `UnmarkColumn`: `.RequireAuthorization(Permissions.PermissionManage)`
- `RevokeUserBypass`: `.RequireAuthorization(Permissions.PermissionManage)`
- `RevokeGroupBypass`: `.RequireAuthorization(Permissions.GroupManage)`

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs
git commit -m "$(cat <<'EOF'
Restructure bypass DELETE routes and add group bypass support
EOF
)"
```

---

### Task 10: Update ListUsers to Include Group Memberships

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs`

- [ ] **Step 1: Update UserSummaryResponse and ListUsers**

Add group info to `UserSummaryResponse`:

```csharp
internal sealed record UserGroupMembership(GroupId GroupId, string GroupName);

internal sealed record UserSummaryResponse(
    UserId Id,
    string? Email,
    string? Name,
    DateTimeOffset? LastLoginAt,
    string[] Permissions,
    IReadOnlyList<UserGroupMembership> Groups);
```

Update `ListUsers` to load group memberships:

```csharp
    private static async Task<Ok<ListUsersResponse>> ListUsers(AppDbContext db, CancellationToken ct)
    {
        var users = await db.ExternalLogins
            .Include(u => u.User)
            .ThenInclude(u => u.Permissions)
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.UserId,
                u.Email,
                u.Name,
                u.LastLoginAt,
                Permissions = u.User.Permissions.Select(p => p.Permission).ToArray(),
            })
            .ToListAsync(ct);

        var allMemberships = await db.GroupMembers
            .AsNoTracking()
            .Join(db.Groups,
                m => m.GroupId,
                g => g.Id,
                (m, g) => new { m.UserId, g.Id, g.Name })
            .ToListAsync(ct);

        var membershipsByUser = allMemberships
            .GroupBy(m => m.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<UserGroupMembership>)g
                    .Select(m => new UserGroupMembership(GroupId.From(m.Id.Value), m.Name))
                    .ToList());

        var response = users.Select(u => new UserSummaryResponse(
            u.UserId, u.Email, u.Name, u.LastLoginAt, u.Permissions,
            membershipsByUser.TryGetValue(u.UserId, out var groups) ? groups : []));

        return TypedResults.Ok(new ListUsersResponse([.. response]));
    }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs
git commit -m "$(cat <<'EOF'
Include group memberships in admin user list response
EOF
)"
```

---

### Task 11: Update Integration Test URLs for Breaking Route Changes

**Files:**
- Modify: `tests/IntegrationTests/Supports/DatabaseRoleTestHelper.cs`
- Modify: `tests/IntegrationTests/Supports/PermissionTestHelper.cs`
- Modify: `tests/IntegrationTests/DatabaseRoleEndpointTests.cs`
- Modify: `tests/IntegrationTests/SensitiveColumnEndpointTests.cs`

- [ ] **Step 1: Update DatabaseRoleTestHelper**

In `tests/IntegrationTests/Supports/DatabaseRoleTestHelper.cs`, update `RemoveRoleAsync` (line 31-32):

Change:
```csharp
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/{userId}/{permission}");
```
To:
```csharp
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/user/{userId}/{permission}");
```

- [ ] **Step 2: Update PermissionTestHelper**

In `tests/IntegrationTests/Supports/PermissionTestHelper.cs`, update `RevokeAllDatabaseRolesAsync` (line 64-66):

Change:
```csharp
                $"/api/admin/database/{role.DatabaseId}/role/{user.Id}/{role.Permission}",
```
To:
```csharp
                $"/api/admin/database/{role.DatabaseId}/role/user/{user.Id}/{role.Permission}",
```

- [ ] **Step 3: Update DatabaseRoleEndpointTests**

In `tests/IntegrationTests/DatabaseRoleEndpointTests.cs`, update all DELETE URLs:

Line 208 — `RemoveRole_HappyPath`:
```csharp
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/user/{aliceUser.Id}/{Permissions.UpdateSubmit}", xsrf);
```

Line 226 — `RemoveRole_Idempotent`:
```csharp
            $"/api/admin/database/{Guid.NewGuid()}/role/user/{Guid.NewGuid()}/{Permissions.QueryExecute}",
```

- [ ] **Step 4: Update SensitiveColumnEndpointTests**

Find all DELETE bypass URLs in `tests/IntegrationTests/SensitiveColumnEndpointTests.cs` and add the `/user/` segment. For example, any URL like:
```
/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/{userId}
```
becomes:
```
/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/user/{userId}
```

- [ ] **Step 5: Build tests to verify**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add tests/IntegrationTests/
git commit -m "$(cat <<'EOF'
Update test URLs for restructured DELETE routes
EOF
)"
```

---

### Task 12: Add Integration Tests for Group Endpoints

**Files:**
- Create: `tests/IntegrationTests/Supports/GroupTestHelper.cs`
- Create: `tests/IntegrationTests/GroupEndpointTests.cs`

- [ ] **Step 1: Create GroupTestHelper**

`tests/IntegrationTests/Supports/GroupTestHelper.cs`:
```csharp
using System.Net.Http.Json;

namespace IntegrationTests.Supports;

internal static class GroupTestHelper
{
    public static async Task<string> CreateGroupAsync(
        AuthenticatedSession adminSession,
        string name,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/group");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { name, description = $"Test group: {name}" });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<GroupBody>(ct);
        return body!.Id;
    }

    public static async Task DeleteGroupAsync(
        AuthenticatedSession adminSession,
        string groupId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/group/{groupId}");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        await adminSession.Client.SendAsync(req, ct);
    }

    public static async Task AddMemberAsync(
        AuthenticatedSession adminSession,
        string groupId,
        string userId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/group/{groupId}/member");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { userId });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public static async Task GrantGroupPermissionAsync(
        AuthenticatedSession adminSession,
        string groupId,
        string permission,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/group/{groupId}/permission");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { permission });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record GroupBody(string Id, string Name);
}
```

- [ ] **Step 2: Create GroupEndpointTests**

`tests/IntegrationTests/GroupEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class GroupEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    private async Task<(AuthenticatedSession Session, string Xsrf)>
        AliceWithGroupManageAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantReq = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.GroupManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();

        // Re-login to pick up new permission
        session.Dispose();
        session = await LoginHelper.SignInAsync("alice", "dev", ct);
        xsrf = await session.FetchXsrfTokenAsync(ct);

        return (session, xsrf);
    }

    // ── anonymous / unauthorized ─────────────────────────────────────────────

    [Fact]
    public async Task ListGroups_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/admin/group", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListGroups_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync("/api/admin/group", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGroup_HappyPath_Returns201()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        var groupName = $"test-{Guid.NewGuid():N}"[..20];
        using var req = MutationRequest(HttpMethod.Post, "/api/admin/group", xsrf,
            new { name = groupName, description = "Integration test group" });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<GroupBody>(ct);
        Assert.Equal(groupName, body!.Name);

        await GroupTestHelper.DeleteGroupAsync(alice, body.Id, xsrf, ct);
    }

    [Fact]
    public async Task CreateGroup_DuplicateName_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        var groupName = $"dup-{Guid.NewGuid():N}"[..20];
        var groupId = await GroupTestHelper.CreateGroupAsync(alice, groupName, xsrf, ct);

        using var req = MutationRequest(HttpMethod.Post, "/api/admin/group", xsrf,
            new { name = groupName });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    [Fact]
    public async Task DeleteGroup_HappyPath_Returns204()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        var groupId = await GroupTestHelper.CreateGroupAsync(alice, $"del-{Guid.NewGuid():N}"[..20], xsrf, ct);

        using var req = MutationRequest(HttpMethod.Delete, $"/api/admin/group/{groupId}", xsrf);
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // ── membership ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_HappyPath_Returns201AndAppearsInList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        var groupId = await GroupTestHelper.CreateGroupAsync(alice, $"mem-{Guid.NewGuid():N}"[..20], xsrf, ct);

        await GroupTestHelper.AddMemberAsync(alice, groupId, bobUser.Id, xsrf, ct);

        var members = await alice.Client.GetFromJsonAsync<MemberListBody>(
            $"/api/admin/group/{groupId}/member", ct);
        Assert.Contains(members!.Members, m => m.UserId == bobUser.Id);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    // ── group permission grants flow through to /api/me ──────────────────────

    [Fact]
    public async Task GroupPermission_FlowsThroughToMe()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        var groupId = await GroupTestHelper.CreateGroupAsync(alice, $"perm-{Guid.NewGuid():N}"[..20], xsrf, ct);
        await GroupTestHelper.AddMemberAsync(alice, groupId, bobUser.Id, xsrf, ct);
        await GroupTestHelper.GrantGroupPermissionAsync(alice, groupId, Permissions.ServerManage, xsrf, ct);

        // Bob should now have server:manage via group
        using var bob2 = await LoginHelper.SignInAsync("bob", "dev", ct);
        var me = await bob2.Client.GetFromJsonAsync<MeBody>("/api/me", ct);
        Assert.Contains(Permissions.ServerManage, me!.Permissions);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    // ── response records ─────────────────────────────────────────────────────

    private sealed record MeBody(string Id, string Email, string? Name, string[] Permissions);
    private sealed record GroupBody(string Id, string Name);
    private sealed record GroupListBody(GroupBody[] Groups);
    private sealed record MemberItem(string UserId, string? UserEmail);
    private sealed record MemberListBody(MemberItem[] Members);
    private sealed record UserRow(string Id, string Email);
    private sealed record ListUserBody(UserRow[] Users);
}
```

- [ ] **Step 3: Build tests to verify compilation**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add tests/IntegrationTests/Supports/GroupTestHelper.cs tests/IntegrationTests/GroupEndpointTests.cs
git commit -m "$(cat <<'EOF'
Add integration tests for group endpoints
EOF
)"
```

---

### Task 13: Run All Tests

- [ ] **Step 1: Run integration tests**

Run: `dotnet test tests/IntegrationTests/IntegrationTests.csproj --verbosity normal`
Expected: All tests pass

- [ ] **Step 2: Fix any failures**

If tests fail due to route changes or response shape changes, fix and re-run.

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Fix test issues from group permission changes
EOF
)"
```

---

### Task 14: Frontend — Add `group:manage` Permission and Group API Hooks

**Files:**
- Modify: `src/frontend/src/auth/permission.ts`
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Add group:manage to Permission type**

In `src/frontend/src/auth/permission.ts`, add `"group:manage"` to the union (after `"server:manage"`):

```typescript
export type Permission =
  | "permission:manage"
  | "server:manage"
  | "group:manage"
  | "query:execute"
  | "query:audit"
  | "update:submit"
  | "update:approve"
  | "update:execute";
```

- [ ] **Step 2: Update existing DELETE URLs with /user/ segment**

In `src/frontend/src/api/hooks.ts`:

Update `useRemoveDatabaseRole` (line 643):
```typescript
      apiRequest(`/api/admin/database/${databaseId}/role/user/${userId}/${permission}`, {
```

Update `useRevokeColumnBypass` (line 813-814):
```typescript
      apiRequest<void, void>(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}/bypass/user/${userId}`,
```

- [ ] **Step 3: Add group API hooks**

Add to `src/frontend/src/api/hooks.ts` after the sensitive columns section:

```typescript
// ── Groups ───────────────────────────────────────────────────────────────

export interface GroupItem {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  memberCount: number;
}

export interface GroupMemberItem {
  id: string;
  userId: string;
  userEmail: string | null;
  userName: string | null;
  addedAt: string;
  addedById: string | null;
}

export interface GroupPermissionItem {
  id: string;
  permission: string;
  grantedAt: string;
  grantedById: string | null;
}

export function useGroups() {
  return useQuery({
    queryKey: ["admin", "group"] as const,
    queryFn: () => apiRequest<void, { groups: Array<GroupItem> }>("/api/admin/group"),
  });
}

export function useGroupMembers(groupId: string | null) {
  return useQuery({
    queryKey: ["admin", "group", groupId, "member"] as const,
    enabled: groupId !== null,
    queryFn: () =>
      apiRequest<void, { members: Array<GroupMemberItem> }>(
        `/api/admin/group/${groupId}/member`,
      ),
  });
}

export function useGroupPermissions(groupId: string | null) {
  return useQuery({
    queryKey: ["admin", "group", groupId, "permission"] as const,
    enabled: groupId !== null,
    queryFn: () =>
      apiRequest<void, { permissions: Array<GroupPermissionItem> }>(
        `/api/admin/group/${groupId}/permission`,
      ),
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, description }: { name: string; description?: string }) =>
      apiRequest<{ name: string; description?: string }, GroupItem>("/api/admin/group", {
        method: "POST",
        body: { name, description },
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "group"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Create group failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useUpdateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      groupId,
      name,
      description,
    }: {
      groupId: string;
      name: string;
      description?: string;
    }) =>
      apiRequest<{ name: string; description?: string }, GroupItem>(
        `/api/admin/group/${groupId}`,
        { method: "PUT", body: { name, description } },
      ),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "group"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Update group failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useDeleteGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId }: { groupId: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}`, { method: "DELETE" }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "group"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Delete group failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useAddGroupMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      apiRequest<{ userId: string }, void>(`/api/admin/group/${groupId}/member`, {
        method: "POST",
        body: { userId },
      }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId, "member"] });
      void qc.invalidateQueries({ queryKey: ["admin", "group"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Add member failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveGroupMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}/member/${userId}`, {
        method: "DELETE",
      }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId, "member"] });
      void qc.invalidateQueries({ queryKey: ["admin", "group"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove member failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useGrantGroupPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, permission }: { groupId: string; permission: string }) =>
      apiRequest<{ permission: string }, void>(`/api/admin/group/${groupId}/permission`, {
        method: "POST",
        body: { permission },
      }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId, "permission"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Grant permission failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRevokeGroupPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, permission }: { groupId: string; permission: string }) =>
      apiRequest<void, void>(`/api/admin/group/${groupId}/permission/${permission}`, {
        method: "DELETE",
      }),
    onSuccess: (_data, { groupId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "group", groupId, "permission"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Revoke permission failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRemoveGroupDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      groupId,
      permission,
    }: {
      databaseId: string;
      groupId: string;
      permission: string;
    }) =>
      apiRequest(
        `/api/admin/database/${databaseId}/role/group/${groupId}/${permission}`,
        { method: "DELETE" },
      ),
    onSuccess: (_data, { databaseId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove group role failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRevokeGroupColumnBypass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
      groupId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
      groupId: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}/bypass/group/${groupId}`,
        { method: "DELETE" },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}
```

- [ ] **Step 4: Build frontend to verify**

Run: `cd src/frontend && npx tsc --noEmit`
Expected: No type errors

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/auth/permission.ts src/frontend/src/api/hooks.ts
git commit -m "$(cat <<'EOF'
Add group:manage permission type and group API hooks
EOF
)"
```

---

### Task 15: Frontend — Create Group Admin Page

**Files:**
- Create: `src/frontend/src/routes/_authed/group.tsx`

- [ ] **Step 1: Create the group admin page**

This page follows the same patterns as `/permission` and `/access`. It needs:
- Route guard checking `group:manage` permission
- Group list with create/edit/delete
- Group detail with tabs for Members, Permissions, Database Access, Sensitive Columns

Build the page following the existing patterns in `permission.tsx` and `access.tsx`. The page should use:
- `useGroups()` for the group list
- `useGroupMembers(groupId)` for the members tab
- `useGroupPermissions(groupId)` for the permissions tab
- `useDatabaseRoles(null)` / `useAdminServers()` for the database access tab
- `useCreateGroup()`, `useUpdateGroup()`, `useDeleteGroup()` for CRUD
- `useAddGroupMember()`, `useRemoveGroupMember()` for membership
- `useGrantGroupPermission()`, `useRevokeGroupPermission()` for global permissions
- `useAssignDatabaseRole()` (with `groupId` instead of `userId`) for database roles
- `useRemoveGroupDatabaseRole()` for removing group database roles

The route file should use `createFileRoute("/_authed/group")` with a `beforeLoad` guard:

```typescript
import { createFileRoute, redirect } from "@tanstack/react-router";
import { meQueryOptions } from "@/api/hooks";

export const Route = createFileRoute("/_authed/group")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("group:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: GroupPage,
});
```

The implementation of `GroupPage` should follow the existing UI patterns (Mantine components, sidebar + detail panel layout, etc.). This is a substantial UI component — implement the full page with all four tabs.

- [ ] **Step 2: Build frontend to verify**

Run: `cd src/frontend && npx tsc --noEmit`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/routes/_authed/group.tsx
git commit -m "$(cat <<'EOF'
Add group admin page with CRUD, members, permissions, and database access tabs
EOF
)"
```

---

### Task 16: Frontend — Update Access and Permission Pages for Group Support

**Files:**
- Modify: `src/frontend/src/routes/_authed/access.tsx`
- Modify: `src/frontend/src/routes/_authed/permission.tsx`

- [ ] **Step 1: Update access page — DatabaseRolePanel to show group entries**

The `DatabaseRolePanel` in `access.tsx` currently shows only user role assignments. Update it to also display group entries from the updated API response (which now includes items with `type: "group"`). Group entries should be displayed with the group name and a visual indicator that they're group-sourced. Group entries should use `useRemoveGroupDatabaseRole()` for removal.

- [ ] **Step 2: Update access page — UserRolePanel to show group-sourced roles**

The `UserRolePanel` shows a user's database access matrix. Add visual indicators for roles that come from group membership (read-only, since they can't be removed from the user view).

- [ ] **Step 3: Update access page — SensitiveColumnsPanel to show group bypasses**

The bypass list for each sensitive column currently shows user bypasses. Update it to also show group bypasses from the `groupBypasses` field in the API response. Add group bypass assignment support (using `groupId` in the grant request body).

- [ ] **Step 4: Update permission page**

The global permission grid on `/permission` currently shows only users. Add a section or tab for groups showing a similar toggle grid (group × global permissions).

- [ ] **Step 5: Build and lint frontend**

Run: `cd src/frontend && npx tsc --noEmit && npx eslint src/`
Expected: No errors

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/routes/_authed/access.tsx src/frontend/src/routes/_authed/permission.tsx
git commit -m "$(cat <<'EOF'
Update access and permission pages to show group entries
EOF
)"
```

---

### Task 17: Generate OpenAPI Schema

**Files:**
- Modify: `src/frontend/src/api/schema.ts` (auto-generated)

- [ ] **Step 1: Regenerate the OpenAPI schema**

The project uses an auto-generated OpenAPI schema that the frontend types depend on. Regenerate it after all backend endpoint changes:

Check how the schema is generated (look for a script in `package.json` or a `dotnet` command that generates `schema.ts`):

```bash
cd src/frontend && cat package.json | grep -A2 "openapi\|schema\|generate"
```

Run the appropriate generation command. If it's `npm run generate` or similar, run it.

- [ ] **Step 2: Build frontend to verify types align**

Run: `cd src/frontend && npx tsc --noEmit`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/api/schema.ts
git commit -m "$(cat <<'EOF'
Regenerate OpenAPI schema for group endpoints
EOF
)"
```

---

### Task 18: Final Verification

- [ ] **Step 1: Run full backend build**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 2: Run full test suite**

Run: `dotnet test --verbosity normal`
Expected: All tests pass

- [ ] **Step 3: Start dev server and manually verify**

Start the application and verify in a browser:
1. Login as admin
2. Grant yourself `group:manage`
3. Navigate to `/group`
4. Create a group, add members, assign permissions
5. Verify group permissions flow through to `/api/me` for members
6. Verify `/access` page shows group entries
7. Verify `/permission` page shows group section

- [ ] **Step 4: Commit any remaining fixes**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Final fixes from manual verification
EOF
)"
```
