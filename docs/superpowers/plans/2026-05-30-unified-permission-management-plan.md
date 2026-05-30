# Unified Permission Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the foundation for the unified Access surface from the design doc: merge `group:manage` into `permission:manage`, add the effective-permissions endpoint with provenance, and restructure the existing `/access` page so the full spec UI can be built on top.

**Scope of THIS plan (be honest):** This plan delivers the **backend changes + page scaffolding**, not the finished spec UI. Specifically it does *not* yet render effective badges, the three-state checkbox model, the By-Resource principal×permission matrix for groups, the members combobox, or the edit-user modal — those are explicitly deferred (see Summary). After executing this plan the spec's success criteria are **not** fully met; a follow-up plan flesh-out is required. Adjust expectations accordingly before starting.

**Architecture:** Backend: remove the `group:manage` permission constant (and clean up stale rows), add `GET /api/admin/user/{userId}/effective` with provenance, add `PATCH /api/admin/user/{userId}`. Frontend: **extend the existing ~574-line `/access` page in place** (it already has database/user/sensitive tabs and the role/bypass/group hooks wired) — make tabs search-driven, rename them, and add a `ByPrincipalTab`. Routing: `/permission` and `/group` redirect into `/access` via typed `search`.

**Tech Stack:** .NET 10 + EF Core (backend), React/TypeScript + Mantine + TanStack Query (frontend). Backend tests are **Aspire-stack integration tests** in `tests/IntegrationTests` (xUnit `Assert`, Keycloak login); frontend uses Vitest + Playwright.

---

## File Structure

**Backend (C#):**
- `src/SluiceBase.Core/Permissions/Permissions.cs` — Remove `GroupManage` constant
- `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs` — Add new `GetEffectiveUserPermissions` handler, update guards
- `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs` — Update guards to use `PermissionManage` only
- `src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs` — Update guards to use `PermissionManage` only
- `src/SluiceBase.Api/Endpoints/GroupEndpoints.cs` — Update guards to use `PermissionManage` only

**Frontend (TypeScript):**
- `src/frontend/src/auth/permission.ts` — Remove `group:manage` from `Permission` union and `PERMISSION_LABELS`
- `src/frontend/src/routes/_authed/_authed.tsx` — Drop `isGroupAdmin`, update nav to show single Access item
- `src/frontend/src/routes/_authed/access.tsx` — **Existing file (~574 lines), extended in place.** Already has `AccessPage` with three tabs (`ByDatabaseTab`, `ByUserTab`, `SensitiveColumnsTab`) plus `UserRolePanel`/`DatabaseRolePanel`/`SensitiveColumnsPanel`, already wired to the role/bypass/group hooks. **Do not `cat >` over it** — that destroys working functionality. Refactor: add `validateSearch` + controlled tabs, rename tabs to By Principal/By Resource/Sensitive Columns, and upgrade `ByUserTab` → By Principal (users+groups, global perms, memberships, effective+provenance).
- `src/frontend/src/api/hooks.ts` — Add only `useEffectiveUserPermissions()` and `useUpdateUser()`. Reuse existing `useMarkSensitiveColumn`/`useUnmarkSensitiveColumn`, `useAssignDatabaseRole`, `useRemoveDatabaseRole`, `useGrantColumnBypass`, `useRevokeColumnBypass`, `useGroups`, `useGroupMembers`, `useGroupPermissions`, `useAddGroupMember`, `useRemoveGroupMember`, `useGrantGroupPermission`, `useRevokeGroupPermission`, `useRemoveGroupDatabaseRole`, `useRevokeGroupColumnBypass`. **Do not add `useMarkColumnSensitive`/`useUnmarkColumnSensitive`** (wrong names, duplicates of existing hooks).

**Tests:**
- `tests/IntegrationTests/EffectivePermissionsTests.cs` — **New file** in the existing `IntegrationTests` project (there is no `SluiceBase.Api.Tests` project). Aspire-stack integration tests using `SluiceBaseStackFactory`, `KeycloakLoginHelper`, and `Supports/*Helper` seeders; xUnit `Assert.*` (no FluentAssertions, no `ApiTestBase`/`UsingDbAsync`).
- `src/frontend/src/routes/_authed/__tests__/access-matrix.test.tsx` (existing) + a new sibling test for By-Principal effective rendering.

**Routing:** (tab/segment via typed `search`, not query strings in `to`)
- Add `validateSearch` to `/access` for `{ tab?, segment? }` and control `<Tabs value>` from it.
- Update existing `/permission` and `/group` routes to `redirect({ to: "/access", search: { ... } })`.

**Backend domain method needed:**
- `src/SluiceBase.Core/Users/User.cs` — add an `UpdateProfile(string? name, string? email, DateTimeOffset at)` method. `User` has private setters, so the PATCH handler cannot assign `user.Name`/`user.Email` directly.

---

## Phase 1: Backend Permission Merge

### Task 1: Remove `group:manage` permission constant

**Files:**
- Modify: `src/SluiceBase.Core/Permissions/Permissions.cs`

- [ ] **Step 1: Open the Permissions file and review the current state**

```bash
cd /Users/voltendron/Projects/sluice-base && cat src/SluiceBase.Core/Permissions/Permissions.cs
```

Expected output: Shows `GroupManage` constant and both constants in `Global` set.

- [ ] **Step 2: Remove the `GroupManage` constant**

Edit `src/SluiceBase.Core/Permissions/Permissions.cs`:
- Delete the line: `public const string GroupManage = "group:manage";`
- Remove `GroupManage,` from the `Global` HashSet initialization (currently around line 19)

Result: File should have only `PermissionManage`, `ServerManage`, `QueryExecute`, etc., and `Global` set omits `GroupManage`.

- [ ] **Step 3: Build to verify no immediate compile errors**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds or errors point to call sites (which you'll fix in the next tasks).

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add -A && git commit -m "Remove group:manage permission constant"
```

---

### Task 2: Update endpoint guards in PermissionEndpoints.cs

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs:15-26`

- [ ] **Step 1: Review the current guards**

```bash
grep -n "RequireAuthorization\|GroupManage" src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs
```

Expected: Line 20 shows `RequireAuthorization(Permissions.PermissionManage)` (already correct).

- [ ] **Step 2: Verify no `GroupManage` references exist in this file**

```bash
grep "GroupManage" src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs || echo "No GroupManage found (good)"
```

Expected: "No GroupManage found (good)"

- [ ] **Step 3: Build to confirm this file has no breaking changes**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds or next error points to another file.

---

### Task 3: Update endpoint guards in DatabaseRoleEndpoints.cs

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs:44, 76, 135`

- [ ] **Step 1: Find all `GroupManage` references**

```bash
grep -n "GroupManage\|PermissionManage.*GroupManage" src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs
```

Expected: Shows lines with `!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage)` and/or `req.UserId is not null ? Permissions.PermissionManage : Permissions.GroupManage`.

- [ ] **Step 2: Update line ~44 (ListServers guard)**

Current:
```csharp
if (user is null || (!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage)))
{
    return TypedResults.Forbid();
}
```

Change to:
```csharp
if (user is null || !user.HasPermission(Permissions.PermissionManage))
{
    return TypedResults.Forbid();
}
```

- [ ] **Step 3: Update line ~76 (ListByDatabase guard)**

Same pattern, change `(!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage))` to `!user.HasPermission(Permissions.PermissionManage)`.

- [ ] **Step 4: Update line ~135 (AssignByDatabase guard)**

Current:
```csharp
if (actor is null || !actor.HasPermission(req.UserId is not null ? Permissions.PermissionManage : Permissions.GroupManage))
{
    return TypedResults.Forbid();
}
```

Change to:
```csharp
if (actor is null || !actor.HasPermission(Permissions.PermissionManage))
{
    return TypedResults.Forbid();
}
```

(No more ternary; both user and group operations require the same permission.)

- [ ] **Step 5: Build and verify**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds or points to next file.

- [ ] **Step 6: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/SluiceBase.Api/Endpoints/DatabaseRoleEndpoints.cs && git commit -m "Update DatabaseRoleEndpoints guards: use permission:manage only"
```

---

### Task 4: Update endpoint guards in SensitiveColumnEndpoints.cs

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs:42, 103, 140, 180, 185, 251, 275`

- [ ] **Step 1: Find all `GroupManage` references**

```bash
grep -n "GroupManage" src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs
```

Expected: Shows ~7 lines with `GroupManage` guards.

- [ ] **Step 2: Update each occurrence**

Review the file and change every guard pattern:
- `!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage)` → `!user.HasPermission(Permissions.PermissionManage)`
- `!user.HasPermission(Permissions.GroupManage)` (standalone) → `!user.HasPermission(Permissions.PermissionManage)`
- `req.GroupId is not null && !user.HasPermission(Permissions.GroupManage)` → `!user.HasPermission(Permissions.PermissionManage)` (in the context of group operations)

Focus on lines mentioned by the grep output. Exact line numbers may vary, so use grep to confirm each location.

- [ ] **Step 3: Build and verify**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds or points to next file.

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs && git commit -m "Update SensitiveColumnEndpoints guards: use permission:manage only"
```

---

### Task 5: Update endpoint guards in GroupEndpoints.cs

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/GroupEndpoints.cs:15`

- [ ] **Step 1: Review the route group declaration**

```bash
grep -A5 "MapGroup" src/SluiceBase.Api/Endpoints/GroupEndpoints.cs | head -20
```

Expected: Line ~15 shows `.RequireAuthorization(Permissions.GroupManage)`.

- [ ] **Step 2: Change to use `PermissionManage`**

Current:
```csharp
var admin = app.MapGroup("/api/admin/group")
    .RequireAuthorization(Permissions.GroupManage);
```

Change to:
```csharp
var admin = app.MapGroup("/api/admin/group")
    .RequireAuthorization(Permissions.PermissionManage);
```

- [ ] **Step 3: Build and verify**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/SluiceBase.Api/Endpoints/GroupEndpoints.cs && git commit -m "Update GroupEndpoints guard: use permission:manage only"
```

---

### Task 6: Run backend tests to confirm no regressions

**Files:**
- Test: `tests/IntegrationTests/` (existing tests — e.g. `GroupEndpointTests`, `DatabaseRoleEndpointTests`, `SensitiveColumnEndpointTests`, `AdminPermissionTests`)

- [ ] **Step 1: Run all backend tests**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet test tests/IntegrationTests --logger=console
```

Expected: All tests pass. If any fail, they should be permission-guard related. Several existing tests grant `Permissions.GroupManage` (e.g. `GroupEndpointTests.AliceWithGroupManageAsync`) — those will break once the constant is removed and must be updated to grant `Permissions.PermissionManage`.

- [ ] **Step 2: If tests fail, investigate and fix**

Expected breakage: `GroupEndpointTests` (its `AliceWithGroupManageAsync` helper grants `group:manage`) and any other test seeding `group:manage`. Update them to grant `permission:manage`. Grep first: `grep -rn "GroupManage" tests/IntegrationTests`.

---

## Phase 2: New Backend Endpoint (`/api/admin/user/{userId}/effective`)

### Task 7: Add effective-permissions response records to PermissionEndpoints.cs

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs:125+` (after existing records)

- [ ] **Step 1: Open the file and review existing record definitions**

```bash
tail -50 src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs
```

Expected: Shows existing records like `ListUsersResponse`, `UserSummaryResponse`, etc.

- [ ] **Step 2: Add new records at the end of the file, before closing brace**

Add these records (exact location: after existing records, before the final `}`):

```csharp
// One consistent source shape: { "direct": bool, "group": GroupInfo | null }.
// Defaults let call sites stay terse: new(Direct: true) or new(Group: g).
internal sealed record EffectivePermissionSource(bool Direct = false, GroupInfo? Group = null);

internal sealed record GroupInfo(GroupId Id, string Name);

internal sealed record EffectivePermissionItem(
    string Permission,
    IReadOnlyList<EffectivePermissionSource> Sources);

internal sealed record EffectiveDbRoleItem(
    DatabaseId DatabaseId,
    string DatabaseName,
    string ServerName,
    string Permission,
    IReadOnlyList<EffectivePermissionSource> Sources);

internal sealed record EffectiveColumnBypassItem(
    DatabaseId DatabaseId,
    string DatabaseName,
    string SensitiveColumnId,
    string Schema,
    string Table,
    string Column,
    IReadOnlyList<EffectivePermissionSource> Sources);

internal sealed record UserGroupMembershipInfo(GroupId GroupId, string GroupName);

internal sealed record EffectiveUserPermissionsResponse(
    IReadOnlyList<EffectivePermissionItem> Global,
    IReadOnlyList<EffectiveDbRoleItem> DatabaseRoles,
    IReadOnlyList<EffectiveColumnBypassItem> ColumnBypasses,
    IReadOnlyList<UserGroupMembershipInfo> Memberships);
```

Note: a direct grant serializes as `{ "direct": true, "group": null }`; an inherited grant as `{ "direct": false, "group": { "id": ..., "name": ... } }`. No custom converter needed. Update the handler in Task 8 so each `EffectivePermissionSource` is constructed as `new EffectivePermissionSource(Direct: true, Group: null)` for direct and `new EffectivePermissionSource(Direct: false, Group: new GroupInfo(...))` for group sources. Also include the column's `SchemaName` (the `SensitiveColumn` entity exposes `SchemaName`, `TableName`, `ColumnName` separately).

- [ ] **Step 3: Build and verify**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds.

---

### Task 8: Implement the `/api/admin/user/{userId}/effective` endpoint

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs:26+` (add route in Map method)

- [ ] **Step 1: Add the endpoint route declaration**

In the `Map` method (after existing routes, around line 26), add:

```csharp
admin.MapGet("/user/{userId}/effective", GetEffectiveUserPermissions)
    .WithName("GetEffectiveUserPermissions");
```

- [ ] **Step 2: Implement the handler function**

Add this handler function before the closing brace of the class (after `RevokePermission`):

```csharp
private static async Task<Results<NotFound, Ok<EffectiveUserPermissionsResponse>>> GetEffectiveUserPermissions(
    UserId userId,
    AppDbContext db,
    CancellationToken ct)
{
    var user = await db.Users
        .AsNoTracking()
        .SingleOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null)
    {
        return TypedResults.NotFound();
    }

    // Direct global permissions
    var directGlobalPerms = await db.UserPermissions
        .AsNoTracking()
        .Where(p => p.UserId == userId)
        .Select(p => p.Permission)
        .ToListAsync(ct);

    // Group memberships
    var groupMemberships = await db.GroupMembers
        .AsNoTracking()
        .Where(gm => gm.UserId == userId)
        .Join(db.Groups,
            gm => gm.GroupId,
            g => g.Id,
            (gm, g) => new { gm.GroupId, g.Name })
        .ToListAsync(ct);

    // Group global permissions
    var groupGlobalPerms = await db.GroupPermissions
        .AsNoTracking()
        .Where(gp => groupMemberships.Select(g => g.GroupId).Contains(gp.GroupId))
        .Select(gp => new { gp.Permission, gp.GroupId })
        .Join(db.Groups,
            gp => gp.GroupId,
            g => g.Id,
            (gp, g) => new { gp.Permission, GroupId = gp.GroupId, g.Name })
        .ToListAsync(ct);

    // Merge global: direct + group-inherited, with sources
    var globalPermissions = new List<EffectivePermissionItem>();
    var allGlobalPerms = directGlobalPerms.Concat(groupGlobalPerms.Select(g => g.Permission)).Distinct();

    foreach (var perm in allGlobalPerms)
    {
        var sources = new List<EffectivePermissionSource>();
        if (directGlobalPerms.Contains(perm))
        {
            sources.Add(new EffectivePermissionSource(Direct: true));
        }
        foreach (var groupPerm in groupGlobalPerms.Where(g => g.Permission == perm).Distinct())
        {
            sources.Add(new EffectivePermissionSource(Group: new GroupInfo(groupPerm.GroupId, groupPerm.Name)));
        }
        globalPermissions.Add(new EffectivePermissionItem(perm, sources));
    }

    // Database-scoped roles: user direct + via groups
    var userDbRoles = await db.UserDatabaseRoles
        .AsNoTracking()
        .Where(r => r.UserId == userId)
        .Join(db.Databases,
            r => r.DatabaseId,
            d => d.Id,
            (r, d) => new { r.DatabaseId, r.Permission, d.DisplayName, d.ServerId })
        .Join(db.Servers,
            x => x.ServerId,
            s => s.Id,
            (x, s) => new { x.DatabaseId, x.Permission, DatabaseName = x.DisplayName, ServerName = s.Name, Source = "direct" as string, GroupId = (GroupId?)null })
        .ToListAsync(ct);

    var groupDbRoles = await db.GroupDatabaseRoles
        .AsNoTracking()
        .Where(r => groupMemberships.Select(g => g.GroupId).Contains(r.GroupId))
        .Join(db.Databases,
            r => r.DatabaseId,
            d => d.Id,
            (r, d) => new { r.DatabaseId, r.Permission, r.GroupId, d.DisplayName, d.ServerId })
        .Join(db.Servers,
            x => x.ServerId,
            s => s.Id,
            (x, s) => new { x.DatabaseId, x.Permission, DatabaseName = x.DisplayName, ServerName = s.Name, x.GroupId, Source = "group" as string })
        .Join(db.Groups,
            x => x.GroupId,
            g => g.Id,
            (x, g) => new { x.DatabaseId, x.Permission, x.DatabaseName, x.ServerName, x.GroupId, g.Name, x.Source })
        .ToListAsync(ct);

    var databaseRoles = new List<EffectiveDbRoleItem>();
    var allDbRoleKeys = userDbRoles.Select(r => (r.DatabaseId, r.Permission))
        .Concat(groupDbRoles.Select(r => (r.DatabaseId, r.Permission)))
        .Distinct();

    foreach (var (dbId, perm) in allDbRoleKeys)
    {
        var sources = new List<EffectivePermissionSource>();
        var userRole = userDbRoles.FirstOrDefault(r => r.DatabaseId == dbId && r.Permission == perm);
        if (userRole is not null)
        {
            sources.Add(new EffectivePermissionSource(Direct: true));
        }
        foreach (var groupRole in groupDbRoles.Where(r => r.DatabaseId == dbId && r.Permission == perm).Distinct())
        {
            sources.Add(new EffectivePermissionSource(Group: new GroupInfo(groupRole.GroupId!.Value, groupRole.Name)));
        }

        var dbInfo = userDbRoles.FirstOrDefault(r => r.DatabaseId == dbId) ?? groupDbRoles.First(r => r.DatabaseId == dbId);
        databaseRoles.Add(new EffectiveDbRoleItem(
            DatabaseId: dbId,
            DatabaseName: dbInfo.DatabaseName,
            ServerName: dbInfo.ServerName,
            Permission: perm,
            Sources: sources));
    }

    // Column bypasses: user direct + via groups.
    // NOTE: carry sc.SchemaName through every projection below (alongside ColumnName/TableName)
    // so `bypassInfo.SchemaName` is available when building EffectiveColumnBypassItem.
    var userColumnBypasses = await db.UserColumnBypasses
        .AsNoTracking()
        .Where(b => b.UserId == userId)
        .Join(db.SensitiveColumns,
            b => b.SensitiveColumnId,
            sc => sc.Id,
            (b, sc) => new { b.SensitiveColumnId, sc.DatabaseId, sc.ColumnName, sc.TableName })
        .Join(db.Databases,
            x => x.DatabaseId,
            d => d.Id,
            (x, d) => new { x.SensitiveColumnId, x.DatabaseId, x.ColumnName, x.TableName, d.DisplayName, d.ServerId })
        .Join(db.Servers,
            x => x.ServerId,
            s => s.Id,
            (x, s) => new { x.SensitiveColumnId, x.DatabaseId, x.ColumnName, x.TableName, DatabaseName = x.DisplayName, ServerName = s.Name, Source = "direct" as string, GroupId = (GroupId?)null })
        .ToListAsync(ct);

    var groupColumnBypasses = await db.GroupColumnBypasses
        .AsNoTracking()
        .Where(b => groupMemberships.Select(g => g.GroupId).Contains(b.GroupId))
        .Join(db.SensitiveColumns,
            b => b.SensitiveColumnId,
            sc => sc.Id,
            (b, sc) => new { b.SensitiveColumnId, b.GroupId, sc.DatabaseId, sc.ColumnName, sc.TableName })
        .Join(db.Databases,
            x => x.DatabaseId,
            d => d.Id,
            (x, d) => new { x.SensitiveColumnId, x.GroupId, x.DatabaseId, x.ColumnName, x.TableName, d.DisplayName, d.ServerId })
        .Join(db.Servers,
            x => x.ServerId,
            s => s.Id,
            (x, s) => new { x.SensitiveColumnId, x.DatabaseId, x.ColumnName, x.TableName, DatabaseName = x.DisplayName, ServerName = s.Name, x.GroupId, Source = "group" as string })
        .Join(db.Groups,
            x => x.GroupId,
            g => g.Id,
            (x, g) => new { x.SensitiveColumnId, x.DatabaseId, x.ColumnName, x.TableName, x.DatabaseName, x.ServerName, x.GroupId, g.Name, x.Source })
        .ToListAsync(ct);

    var columnBypasses = new List<EffectiveColumnBypassItem>();
    var allColumnBypassKeys = userColumnBypasses.Select(b => b.SensitiveColumnId)
        .Concat(groupColumnBypasses.Select(b => b.SensitiveColumnId))
        .Distinct();

    foreach (var colId in allColumnBypassKeys)
    {
        var sources = new List<EffectivePermissionSource>();
        var userBypass = userColumnBypasses.FirstOrDefault(b => b.SensitiveColumnId == colId);
        if (userBypass is not null)
        {
            sources.Add(new EffectivePermissionSource(Direct: true));
        }
        foreach (var groupBypass in groupColumnBypasses.Where(b => b.SensitiveColumnId == colId).Distinct())
        {
            sources.Add(new EffectivePermissionSource(Group: new GroupInfo(groupBypass.GroupId!.Value, groupBypass.Name)));
        }

        var bypassInfo = userBypass ?? groupColumnBypasses.First(b => b.SensitiveColumnId == colId);
        columnBypasses.Add(new EffectiveColumnBypassItem(
            DatabaseId: bypassInfo.DatabaseId,
            DatabaseName: bypassInfo.DatabaseName,
            SensitiveColumnId: colId,
            Schema: bypassInfo.SchemaName,
            Table: bypassInfo.TableName,
            Column: bypassInfo.ColumnName,
            Sources: sources));
    }

    // Memberships
    var memberships = groupMemberships
        .Select(g => new UserGroupMembershipInfo(g.GroupId, g.Name))
        .ToList();

    return TypedResults.Ok(new EffectiveUserPermissionsResponse(
        Global: globalPermissions,
        DatabaseRoles: databaseRoles,
        ColumnBypasses: columnBypasses,
        Memberships: memberships));
}
```

- [ ] **Step 3: Build and verify**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs && git commit -m "Add GetEffectiveUserPermissions endpoint with provenance"
```

---

### Task 9: Write integration tests for the effective-permissions endpoint

**Files:**
- Create: `tests/IntegrationTests/EffectivePermissionsTests.cs`

**Convention reminder (verified against the repo):**
- The only backend test project is `tests/IntegrationTests` (there is **no** `SluiceBase.Api.Tests`).
- Tests run against the real Aspire stack via `SluiceBaseStackFactory` and authenticate through Keycloak (`KeycloakLoginHelper`). They use **xUnit `Assert.*`**, not FluentAssertions; there is no `ApiTestBase`/`UsingDbAsync`/`ReadAsAsync` and no in-memory DbContext.
- Seed via the `Supports/*Helper` classes (`GroupTestHelper`, `PermissionTestHelper`, `DatabaseRoleTestHelper`, `SensitiveColumnTestHelper`).
- Entities have private constructors — never use object initializers. Construct via factories (`User.Create`, `Group.Create`, `GroupMember.Add`, `*.Grant`) — but in these tests you generally create state over HTTP (grant permission, create group, add member) rather than touching the DbContext directly, mirroring `GroupEndpointTests`.

- [ ] **Step 1: Read the existing pattern before writing anything**

```bash
sed -n '1,60p' tests/IntegrationTests/GroupEndpointTests.cs
ls tests/IntegrationTests/Supports
sed -n '1,80p' tests/IntegrationTests/Supports/PermissionTestHelper.cs
```

Expected: confirms the `SluiceBaseStackFactory` ctor injection, `KeycloakLoginHelper.SignInAsync`, `MutationRequest` + `X-XSRF-TOKEN` flow, and the helper method signatures you'll call.

- [ ] **Step 2: Write the test class modeled on `GroupEndpointTests`**

Create `tests/IntegrationTests/EffectivePermissionsTests.cs`. Skeleton (adapt helper calls to the real signatures you read in Step 1):

```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class EffectivePermissionsTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private sealed record GroupInfoBody(string Id, string Name);
    private sealed record SourceBody(bool Direct, GroupInfoBody? Group);
    private sealed record GlobalItemBody(string Permission, SourceBody[] Sources);
    private sealed record MembershipBody(string GroupId, string GroupName);
    private sealed record EffectiveBody(
        GlobalItemBody[] Global,
        object[] DatabaseRoles,
        object[] ColumnBypasses,
        MembershipBody[] Memberships);

    // Sign in as the seeded admin (alice) who can be granted permission:manage,
    // following the AliceWith… helper pattern in GroupEndpointTests.

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        var users = await /* admin */ /* GET /api/admin/user */ Task.FromResult<HttpResponseMessage?>(null);
        // bob lacks permission:manage → GET /api/admin/user/{anyId}/effective must be 403
        var resp = await bob.Client.GetAsync($"/api/admin/user/{Guid.NewGuid()}/effective", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task DirectGrant_ReturnsDirectSource() { /* grant query:execute to a user over HTTP, assert Sources has Direct == true */ }

    [Fact]
    public async Task GroupGrant_ReturnsGroupSource() { /* create group, grant query:audit to group, add user, assert Sources has matching Group */ }

    [Fact]
    public async Task DirectAndGroup_ReturnsBothSources() { /* assert two sources: one Direct==true, one with Group */ }

    [Fact]
    public async Task NonexistentUser_Returns404() { /* admin GET /effective for random id → 404 */ }

    [Fact]
    public async Task IncludesMemberships() { /* add user to group, assert Memberships contains it */ }
}
```

Deserialize with `await resp.Content.ReadFromJsonAsync<EffectiveBody>(ct)` and assert with `Assert.*` (e.g. `Assert.Contains(item.Sources, s => s.Direct)` and `Assert.Contains(item.Sources, s => s.Group?.Name == "Engineering")`).

- [ ] **Step 3: Run the integration tests**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet test tests/IntegrationTests --filter "FullyQualifiedName~EffectivePermissions" 2>&1 | tail -40
```

Expected: tests compile and pass against the running stack. (Integration tests spin up the Aspire app; they are slower than unit tests.)

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add tests/IntegrationTests/EffectivePermissionsTests.cs && git commit -m "Add integration tests for GetEffectiveUserPermissions endpoint"
```

---

## Phase 3: Frontend Permission Type Updates

### Task 10: Remove `group:manage` from frontend Permission type and labels

**Files:**
- Modify: `src/frontend/src/auth/permission.ts`

- [ ] **Step 1: Review current file**

```bash
cat src/frontend/src/auth/permission.ts
```

Expected: Shows `Permission` union with `group:manage` and `PERMISSION_LABELS` with the corresponding entry.

- [ ] **Step 2: Remove `group:manage` from the `Permission` type union**

Current:
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

Change to:
```typescript
export type Permission =
  | "permission:manage"
  | "server:manage"
  | "query:execute"
  | "query:audit"
  | "update:submit"
  | "update:approve"
  | "update:execute";
```

- [ ] **Step 3: Remove `group:manage` from `PERMISSION_LABELS`**

Current (example):
```typescript
export const PERMISSION_LABELS: Record<Permission, { short: string; full: string }> = {
  "group:manage": { short: "Group", full: "Manage groups" },
  "permission:manage": { short: "Permission", full: "Manage permissions" },
  ...
};
```

Change to remove the `"group:manage"` entry entirely.

- [ ] **Step 4: Run type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit 2>&1 | head -50
```

Expected: TypeScript finds no errors related to permission types.

- [ ] **Step 5: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/auth/permission.ts && git commit -m "Remove group:manage from Permission type and labels"
```

---

### Task 11: Update nav to remove `isGroupAdmin` check and add single Access item

**Files:**
- Modify: `src/frontend/src/routes/_authed/_authed.tsx:46-51, 188-197`

- [ ] **Step 1: Review current nav setup**

```bash
grep -n "isGroupAdmin\|isAdmin" src/frontend/src/routes/_authed/_authed.tsx
```

Expected: Shows `const isAdmin = useHasPermission("permission:manage");` and `const isGroupAdmin = useHasPermission("group:manage");` and two separate NavLinks.

- [ ] **Step 2: Remove the `isGroupAdmin` line**

Delete the line:
```typescript
const isGroupAdmin = useHasPermission("group:manage");
```

- [ ] **Step 3: Replace the two NavLinks (Permission and Groups) with a single Access NavLink**

Current (around lines 188-207):
```typescript
{isGroupAdmin && (
  <NavLink
    label="Groups"
    leftSection={<IconUsers size={16} />}
    component={Link}
    to="/group"
    active={location.pathname === "/group"}
    onClick={closeMobileNav}
  />
)}
{isAdmin && (
  <NavLink
    label="Permission"
    leftSection={<IconShieldLock size={16} />}
    component={Link}
    to="/permission"
    active={location.pathname === "/permission"}
    onClick={closeMobileNav}
  />
)}
```

Change to:
```typescript
{isAdmin && (
  <NavLink
    label="Access"
    leftSection={<IconKey size={16} />}
    component={Link}
    to="/access"
    active={location.pathname === "/access"}
    onClick={closeMobileNav}
  />
)}
```

(You may need to add `IconKey` import at the top; check if it's already imported.)

- [ ] **Step 4: Verify the import for IconKey**

```bash
grep "IconKey" src/frontend/src/routes/_authed/_authed.tsx || echo "Not imported yet"
```

If not imported, add it to the import block at the top (line ~14-27):
```typescript
import {
  ...
  IconKey,
  ...
} from "@tabler/icons-react";
```

- [ ] **Step 5: Run type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 6: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/routes/_authed/_authed.tsx && git commit -m "Replace Permission + Groups nav items with single Access item"
```

---

## Phase 4: New Frontend Hooks

### Task 12: Add `useEffectiveUserPermissions` and `useUpdateUser` hooks

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Review the file structure**

```bash
grep -n "export function use\|export const use" src/frontend/src/api/hooks.ts | tail -20
```

Expected: Shows the pattern of how hooks are exported (either as functions or const arrow functions).

- [ ] **Step 2: Add the `useEffectiveUserPermissions` hook**

At the end of the hooks.ts file (before the final export or closing), add:

```typescript
export function useEffectiveUserPermissions(userId: string) {
  return useQuery({
    queryKey: ["admin", "user", userId, "effective"],
    queryFn: async () => {
      const res = await fetch(`/api/admin/user/${userId}/effective`);
      if (!res.ok) throw new Error(`Failed to fetch effective permissions for user ${userId}`);
      return (await res.json()) as {
        global: Array<{ permission: string; sources: Array<{ direct?: string; group?: { id: string; name: string } }> }>;
        databaseRoles: Array<{ databaseId: string; databaseName: string; serverName: string; permission: string; sources: Array<any> }>;
        columnBypasses: Array<{ databaseId: string; databaseName: string; sensitiveColumnId: string; table: string; column: string; sources: Array<any> }>;
        memberships: Array<{ groupId: string; groupName: string }>;
      };
    },
  });
}
```

- [ ] **Step 3: Add the `useUpdateUser` hook**

Add (after `useEffectiveUserPermissions`):

```typescript
export function useUpdateUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: { userId: string; name: string; email: string }) => {
      const res = await fetch(`/api/admin/user/${req.userId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: req.name, email: req.email }),
      });
      if (!res.ok) throw new Error("Failed to update user");
      return res.json();
    },
    onSuccess: (_, req) => {
      queryClient.invalidateQueries({ queryKey: ["admin", "user"] });
      queryClient.invalidateQueries({ queryKey: ["admin", "user", req.userId, "effective"] });
    },
  });
}
```

- [ ] **Step 4: Do NOT add column-marking hooks — reuse the existing ones**

`useMarkSensitiveColumn` and `useUnmarkSensitiveColumn` already exist in `hooks.ts` (and are already consumed by `access.tsx`). Do not add `useMarkColumnSensitive`/`useUnmarkColumnSensitive` — they would be duplicate hooks under the wrong names pointing at invented endpoints. Confirm the canonical names:

```bash
grep -n "useMarkSensitiveColumn\|useUnmarkSensitiveColumn" src/frontend/src/api/hooks.ts
```

- [ ] **Step 5: Run type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit 2>&1 | head -50
```

Expected: No errors (or errors about unimplemented API endpoints, which are expected for now).

- [ ] **Step 6: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/api/hooks.ts && git commit -m "Add useEffectiveUserPermissions, useUpdateUser, and column-marking hooks"
```

---

## Phase 5: Create New Access Page & Components

### Task 13: Refactor the existing `/access` page (search-driven tabs)

**Files:**
- Modify: `src/frontend/src/routes/_authed/access.tsx` (existing ~574-line file — **edit in place, never `cat >` over it**)

The page already renders `<Tabs defaultValue="database">` with `ByDatabaseTab`, `ByUserTab`, and `SensitiveColumnsTab` plus their panels. This task makes the tab state search-driven and renames the tabs; the next tasks upgrade the panels.

- [ ] **Step 1: Read the current file end-to-end first**

```bash
sed -n '1,120p' src/frontend/src/routes/_authed/access.tsx
```

Note the existing route definition, the `beforeLoad` guard (`me?.permissions.includes("permission:manage")` — keep it), and the three existing tab components you will reuse/rename.

- [ ] **Step 2: Add `validateSearch` and control the tabs from search**

In the `createFileRoute("/_authed/access")` options, add:

```typescript
type AccessSearch = {
  tab?: "principal" | "resource" | "columns";
  segment?: "users" | "groups";
};

export const Route = createFileRoute("/_authed/access")({
  validateSearch: (search: Record<string, unknown>): AccessSearch => ({
    tab: (["principal", "resource", "columns"] as const).find((t) => t === search.tab),
    segment: (["users", "groups"] as const).find((s) => s === search.segment),
  }),
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(["me"]) as { permissions: Array<string> } | undefined;
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: AccessPage,
});
```

Then make `<Tabs>` controlled in `AccessPage`:

```typescript
const { tab } = Route.useSearch();
const navigate = Route.useNavigate();
// ...
<Tabs
  value={tab ?? "principal"}
  onChange={(value) => navigate({ search: (s) => ({ ...s, tab: value as AccessSearch["tab"] }) })}
>
```

- [ ] **Step 3: Rename the three tabs to By Principal / By Resource / Sensitive Columns**

Map the existing tab values: `database` → `resource`, `user` → `principal`, `sensitive` → `columns`. Reorder so **By Principal** is first/default. Keep mounting the existing `ByDatabaseTab` under the `resource` panel and `SensitiveColumnsTab` under the `columns` panel for now; mount the new `ByPrincipalTab` (Task 14) under the `principal` panel, replacing the old `ByUserTab`.

- [ ] **Step 4: Type-check and commit**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit 2>&1 | grep -E "access|error" | head -20
mkdir -p /Users/voltendron/Projects/sluice-base/src/frontend/src/routes/_authed/access
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/routes/_authed/access.tsx && git commit -m "Make Access tabs search-driven and rename to principal/resource/columns"
```

---

### Task 14: Create ByPrincipalTab component (left list + right detail stub)

**Files:**
- Create: `src/frontend/src/routes/_authed/access/ByPrincipalTab.tsx`

- [ ] **Step 1: Create the ByPrincipalTab component**

```bash
cat > src/frontend/src/routes/_authed/access/ByPrincipalTab.tsx << 'EOF'
import { Flex, Paper, ScrollArea, Stack, Text, TextInput, SegmentedControl, Badge, Avatar, Tooltip, ActionIcon, NavLink } from "@mantine/core";
import { IconTrash } from "@tabler/icons-react";
import { useState } from "react";
import { useUsers, useGroups } from "@/api/hooks";
import PrincipalDetail from "./PrincipalDetail";

type Principal = { type: "user"; id: string; email?: string; name?: string } | { type: "group"; id: string; name: string; description?: string };

export default function ByPrincipalTab() {
  const users = useUsers();
  const groups = useGroups();
  const [search, setSearch] = useState("");
  const [segment, setSegment] = useState<"users" | "groups">("users");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const userList: Principal[] = (users.data?.users ?? []).map((u) => ({
    type: "user" as const,
    id: u.id,
    email: u.email,
    name: u.name,
  }));

  const groupList: Principal[] = (groups.data?.groups ?? []).map((g) => ({
    type: "group" as const,
    id: g.id,
    name: g.name,
    description: g.description,
  }));

  const principals = segment === "users" ? userList : groupList;
  const filtered = principals.filter((p) => {
    const searchStr = search.toLowerCase();
    if (p.type === "user") {
      return (p.email?.toLowerCase().includes(searchStr) ?? false) || (p.name?.toLowerCase().includes(searchStr) ?? false);
    } else {
      return p.name.toLowerCase().includes(searchStr);
    }
  });

  const selected = filtered.find((p) => p.id === selectedId) ?? filtered.at(0) ?? null;

  return (
    <Flex gap="md" style={{ height: 600 }}>
      <Paper withBorder style={{ flex: "0 0 250px", display: "flex", flexDirection: "column" }}>
        <Stack gap="xs" p="xs" style={{ flex: 0 }}>
          <SegmentedControl
            value={segment}
            onChange={(val) => {
              setSegment(val as "users" | "groups");
              setSelectedId(null);
            }}
            data={[
              { label: "Users", value: "users" },
              { label: "Groups", value: "groups" },
            ]}
            fullWidth
            size="xs"
          />
          <TextInput
            placeholder="Search…"
            size="xs"
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
          />
        </Stack>
        <ScrollArea.Autosize mah={550}>
          {filtered.map((p) => (
            <NavLink
              key={p.id}
              label={p.type === "user" ? p.email : p.name}
              description={p.type === "user" ? p.name : p.description || "No description"}
              leftSection={
                <Avatar size="sm" name={p.type === "user" ? p.email || "?" : p.name} color="initials" />
              }
              rightSection={
                p.type === "user" ? (
                  <Tooltip label={`${p.id.length > 0 ? "1" : "0"} group(s)`} position="left" withArrow>
                    <Badge size="xs" variant="light">
                      {/* TODO: show actual group count */}
                      0
                    </Badge>
                  </Tooltip>
                ) : (
                  <Badge size="xs" variant="light">
                    {/* TODO: show actual member count */}
                    0
                  </Badge>
                )
              }
              active={selected?.id === p.id}
              onClick={() => setSelectedId(p.id)}
              p="8px"
            />
          ))}
        </ScrollArea.Autosize>
      </Paper>

      {selected ? (
        <PrincipalDetail principal={selected} style={{ flex: 1 }} />
      ) : (
        <Paper withBorder style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <Text c="dimmed">Select a principal to view details</Text>
        </Paper>
      )}
    </Flex>
  );
}
EOF
cat src/frontend/src/routes/_authed/access/ByPrincipalTab.tsx
```

- [ ] **Step 2: Run type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit 2>&1 | grep -E "ByPrincipalTab|error" | head -20
```

Expected: Errors about missing `PrincipalDetail` (which you'll create next).

- [ ] **Step 3: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/routes/_authed/access/ByPrincipalTab.tsx && git commit -m "Add ByPrincipalTab component with principal list"
```

---

### Task 15: Create PrincipalDetail component (stub with permission sections)

**Files:**
- Create: `src/frontend/src/routes/_authed/access/PrincipalDetail.tsx`

- [ ] **Step 1: Create PrincipalDetail stub**

```bash
cat > src/frontend/src/routes/_authed/access/PrincipalDetail.tsx << 'EOF'
import { Stack, Paper, Title, Text, Group, CSSProperties } from "@mantine/core";
import { useEffectiveUserPermissions } from "@/api/hooks";

type Principal = { type: "user"; id: string; email?: string; name?: string } | { type: "group"; id: string; name: string; description?: string };

export default function PrincipalDetail({
  principal,
  style,
}: {
  principal: Principal;
  style?: CSSProperties;
}) {
  const effectivePerms = principal.type === "user" ? useEffectiveUserPermissions(principal.id) : null;

  return (
    <Stack gap="md" style={style}>
      <Group>
        <div>
          <Title order={3}>{principal.type === "user" ? principal.email : principal.name}</Title>
          {principal.type === "user" && principal.name && <Text size="sm" c="dimmed">{principal.name}</Text>}
        </div>
      </Group>

      <Paper withBorder p="md">
        <Title order={4} mb="sm">Global Permissions</Title>
        {effectivePerms?.data?.global ? (
          <Text size="sm">{effectivePerms.data.global.length} permission(s)</Text>
        ) : (
          <Text size="sm" c="dimmed">Loading…</Text>
        )}
        {/* TODO: render global permissions with source badges */}
      </Paper>

      {principal.type === "user" && (
        <>
          <Paper withBorder p="md">
            <Title order={4} mb="sm">Group Memberships</Title>
            {effectivePerms?.data?.memberships ? (
              <Text size="sm">{effectivePerms.data.memberships.length} group(s)</Text>
            ) : (
              <Text size="sm" c="dimmed">Loading…</Text>
            )}
            {/* TODO: render memberships */}
          </Paper>

          <Paper withBorder p="md">
            <Title order={4} mb="sm">Database-Scoped Roles</Title>
            {/* TODO: render db roles table */}
            <Text size="sm" c="dimmed">TODO</Text>
          </Paper>

          <Paper withBorder p="md">
            <Title order={4} mb="sm">Column Bypasses</Title>
            {/* TODO: render column bypasses table */}
            <Text size="sm" c="dimmed">TODO</Text>
          </Paper>
        </>
      )}

      {principal.type === "group" && (
        <>
          <Paper withBorder p="md">
            <Title order={4} mb="sm">Members</Title>
            {/* TODO: render members + add member combobox */}
            <Text size="sm" c="dimmed">TODO</Text>
          </Paper>

          <Paper withBorder p="md">
            <Title order={4} mb="sm">Database-Scoped Roles</Title>
            {/* TODO: render db roles table */}
            <Text size="sm" c="dimmed">TODO</Text>
          </Paper>

          <Paper withBorder p="md">
            <Title order={4} mb="sm">Column Bypasses</Title>
            {/* TODO: render column bypasses table */}
            <Text size="sm" c="dimmed">TODO</Text>
          </Paper>
        </>
      )}
    </Stack>
  );
}
EOF
cat src/frontend/src/routes/_authed/access/PrincipalDetail.tsx
```

- [ ] **Step 2: Create stub files for the other two tabs (ByResourceTab and SensitiveColumnsTab)**

```bash
cat > src/frontend/src/routes/_authed/access/ByResourceTab.tsx << 'EOF'
import { Text } from "@mantine/core";

export default function ByResourceTab() {
  return <Text c="dimmed">By Resource view (TODO)</Text>;
}
EOF

cat > src/frontend/src/routes/_authed/access/SensitiveColumnsTab.tsx << 'EOF'
import { Text } from "@mantine/core";

export default function SensitiveColumnsTab() {
  return <Text c="dimmed">Sensitive Columns view (TODO)</Text>;
}
EOF
```

- [ ] **Step 3: Run type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit 2>&1 | grep error | head -20
```

Expected: No errors (or only TODO-related messages).

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/routes/_authed/access/ && git commit -m "Add PrincipalDetail stub and placeholder tabs"
```

---

### Task 16: Set up routing redirects for old pages

**Files:**
- Modify: `src/frontend/src/routes/_authed/permission.tsx` (replace page with redirect)
- Modify: `src/frontend/src/routes/_authed/group.tsx` (replace page with redirect)

Note: `/access` is the surface we extended in Task 13 — it stays as-is, no redirect. Pass tab/segment via the typed `search` object; **a query string baked into `to` (e.g. `to: "/access?tab=principal"`) does not type-check and is ignored by TanStack Router.**

- [ ] **Step 1: Update the old `/permission` route to redirect**

```bash
cat > src/frontend/src/routes/_authed/permission.tsx << 'EOF'
import { redirect, createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/_authed/permission")({
  beforeLoad: () => {
    throw redirect({ to: "/access", search: { tab: "principal" } });
  },
});
EOF
```

- [ ] **Step 2: Update the old `/group` route to redirect**

```bash
cat > src/frontend/src/routes/_authed/group.tsx << 'EOF'
import { redirect, createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/_authed/group")({
  beforeLoad: () => {
    throw redirect({ to: "/access", search: { tab: "principal", segment: "groups" } });
  },
});
EOF
```

- [ ] **Step 3: Run type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit 2>&1 | grep error | head -20
```

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/frontend/src/routes/_authed/permission.tsx src/frontend/src/routes/_authed/group.tsx && git commit -m "Add redirect routes for old permission and group pages"
```

---

## Phase 6: Backend Endpoint Update Endpoint (for useUpdateUser)

### Task 17: Add PATCH `/api/admin/user/{userId}` endpoint

**Files:**
- Modify: `src/SluiceBase.Core/Users/User.cs` (add `UpdateProfile` domain method)
- Modify: `src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs` (add route and handler)

> **Design note:** `Email` is the OIDC identity surfaced from the IdP and is refreshed on each login (see `ExternalLogin.RecordLogin`). Letting an admin edit it locally is dubious — a subsequent login may overwrite it, and it can desync from the IdP. Confirm with the product owner whether this endpoint should edit **name only**. The steps below support both but default to a single domain method.

- [ ] **Step 1: Add a domain method to `User`** (the entity has private setters — you cannot assign `user.Name`/`user.Email` from the handler)

In `src/SluiceBase.Core/Users/User.cs`, after `Create`:

```csharp
public void UpdateProfile(string? name, string? email)
{
    if (!string.IsNullOrWhiteSpace(name))
    {
        Name = name;
    }
    if (!string.IsNullOrWhiteSpace(email))
    {
        Email = email;
    }
}
```

- [ ] **Step 2: Add the route declaration**

In the `Map` method (in the admin group), add after the existing routes:

```csharp
admin.MapPatch("/user/{userId}", UpdateUser)
    .WithName("UpdateUser");
```

- [ ] **Step 3: Implement the UpdateUser handler**

Add this function before the closing brace of the class:

```csharp
private static async Task<Results<NotFound, Ok>> UpdateUser(
    UserId userId,
    UpdateUserRequest req,
    AppDbContext db,
    CancellationToken ct)
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null)
    {
        return TypedResults.NotFound();
    }

    user.UpdateProfile(req.Name, req.Email);

    await db.SaveChangesAsync(ct);
    return TypedResults.Ok();
}
```

- [ ] **Step 4: Add the UpdateUserRequest record**

Add after the existing records:

```csharp
internal sealed record UpdateUserRequest(string? Name, string? Email);
```

- [ ] **Step 5: Build and verify**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base && git add src/SluiceBase.Core/Users/User.cs src/SluiceBase.Api/Endpoints/PermissionEndpoints.cs && git commit -m "Add PATCH /api/admin/user/{userId} endpoint for updating user"
```

---

## Phase 7: Verification & Final Steps

### Task 18: Run full build and basic smoke test

**Files:**
- (No files changed)

- [ ] **Step 1: Build backend**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet build src/SluiceBase.Api
```

Expected: Build succeeds.

- [ ] **Step 2: Build frontend**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npm run build 2>&1 | tail -30
```

Expected: Build succeeds.

- [ ] **Step 3: Run backend tests**

```bash
cd /Users/voltendron/Projects/sluice-base && dotnet test tests/IntegrationTests --logger=console 2>&1 | tail -30
```

Expected: Tests pass (or show expected failures for new endpoint tests before full implementation).

- [ ] **Step 4: Run frontend type check**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 5: Commit (if changes from test runs)**

```bash
cd /Users/voltendron/Projects/sluice-base && git status --short
```

If there are any changes, commit them:

```bash
git add -A && git commit -m "Verify builds and tests pass"
```

If no changes, skip the commit.

---

## Summary

This plan implements the unified permission management surface in bite-sized steps:

1. **Backend permission merge** (Tasks 1-6): Remove `group:manage`, update guards
2. **Effective-permissions endpoint** (Tasks 7-9): Add `/api/admin/user/{userId}/effective` with provenance
3. **Frontend permission type** (Tasks 10-11): Remove `group:manage` from types and nav
4. **New hooks** (Task 12): Add `useEffectiveUserPermissions`, `useUpdateUser`, column hooks
5. **New Access page** (Tasks 13-16): Create `/access` with three tabs, stub out components, set up redirects
6. **Update user endpoint** (Task 17): PATCH `/api/admin/user/{userId}` to support user editing
7. **Verification** (Task 18): Build, test, verify

**Next phase (out of scope for this plan, but documented for reference):**
- Flesh out ByPrincipalTab: render global permissions with source badges, memberships list, db-roles table, column-bypass table
- Flesh out ByResourceTab: left resource list, right principal × permission matrix with effective/edit-direct logic
- Flesh out SensitiveColumnsTab: left column list with sensitivity toggle, right bypass matrix
- Add tests for each component (interaction tests, snapshot tests)
- E2E: add user to group, verify permission propagates and shows in effective view
