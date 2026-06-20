# Group-Based Access — Design

**Date:** 2026-06-19
**Status:** Approved for planning

## Overview

Today every access grant in SluiceBase is per-user. Global permissions
(`permission:manage`, `server:manage`) live in `user_permission`; scopeable
per-database permissions (`query:execute`, `query:audit`,
`update:submit/approve/execute`) live in `user_database_role`. Granting a common
access pattern to several people means repeating the same per-user toggles, and
keeping that pattern in sync over time is manual.

This design adds **access groups**: a named, admin-managed set of users that
*carries* access. A group can grant both global and per-database permissions. A
user's effective access becomes the union of their direct grants and the grants
of every group they belong to. Membership changes propagate live — no
re-stamping, no copied rows.

## Goals

- Let an admin define an access pattern once (as a group) and apply it by adding
  members.
- Support both global and per-database grants on a group.
- Make a user's **effective** access and its **provenance** (direct vs. which
  group) visible in the admin UI.
- Keep all existing per-user grants and the existing UI working unchanged;
  groups are an additive second source.

## Non-Goals (v1)

- No "deny"/negative overrides. Access is strictly additive — a group only ever
  adds access. A user's effective set is a pure union.
- No syncing membership from OIDC/IdP group claims. Membership is managed
  entirely in-app. (The resolver seam leaves room to add this later.)
- No nested groups (groups within groups).
- No reusable permission bundles referenced by multiple groups (the group *is*
  the bundle).

## Decisions

These were settled during brainstorming:

- **Group shape:** a user group that directly carries access (not a copy-once
  template, not a two-layer group→bundle→permission RBAC).
- **Membership source:** admin-managed in-app only.
- **Group access:** both global and per-database permissions.
- **Resolution:** centralized behind a single access-resolver seam (not inlined
  at each check site, not materialized into per-user rows).
- **Composition:** additive union of direct + group grants.

## Data Model

Three (logically four) new tables under `SluiceBase.Core/Permissions/`,
following the existing `UserDatabaseRole` conventions: private constructor,
Vogen `[ValueObject<Guid>]` strongly-typed ids with
`Customizations.AddFactoryMethodForGuids`, static `Create`/`Grant` factories,
snake_case `ToTable` via an `IEntityTypeConfiguration`. The two grant tables
deliberately mirror `user_permission` and `user_database_role`, keyed by
`GroupId` instead of `UserId`.

### `access_group`

| Column      | Type              | Notes                          |
|-------------|-------------------|--------------------------------|
| Id          | `AccessGroupId`   | PK                             |
| Name        | `string` (≤128)   | Unique                         |
| Description | `string?`         | Optional                       |
| CreatedAt   | `DateTimeOffset`  |                                |
| CreatedById | `UserId?`         | FK → user, `SetNull`           |

### `access_group_member`

| Column   | Type             | Notes                              |
|----------|------------------|------------------------------------|
| Id       | `AccessGroupMemberId` | PK                            |
| GroupId  | `AccessGroupId`  | FK → access_group, `Cascade`       |
| UserId   | `UserId`         | FK → user, `Cascade`               |
| AddedAt  | `DateTimeOffset` |                                    |
| AddedById| `UserId?`        | FK → user, `SetNull`               |

Unique index `(GroupId, UserId)`.

### `access_group_permission` (global grants)

| Column      | Type                       | Notes                         |
|-------------|----------------------------|-------------------------------|
| Id          | `AccessGroupPermissionId`  | PK                            |
| GroupId     | `AccessGroupId`            | FK → access_group, `Cascade`  |
| Permission  | `string` (≤64)             | Validated vs `Permissions.Global` |
| GrantedAt   | `DateTimeOffset`           |                               |
| GrantedById | `UserId?`                  | FK → user, `SetNull`          |

Unique index `(GroupId, Permission)`.

### `access_group_database_role` (per-database grants)

| Column      | Type                          | Notes                            |
|-------------|-------------------------------|----------------------------------|
| Id          | `AccessGroupDatabaseRoleId`   | PK                               |
| GroupId     | `AccessGroupId`               | FK → access_group, `Cascade`     |
| Permission  | `string` (≤64)                | Validated vs `Permissions.Scopeable` |
| DatabaseId  | `DatabaseId`                  | FK → database, `Restrict`        |
| GrantedAt   | `DateTimeOffset`              |                                  |
| GrantedById | `UserId?`                     | FK → user, `SetNull`             |

Unique index `(GroupId, Permission, DatabaseId)`.

**Deletion:** deleting a group cascades its members and both grant tables.
Removing a member or a group-grant row changes effective access immediately;
there is nothing materialized to clean up.

**Migration:** one EF Core migration adds the four tables. Per project
conventions, the migration file is not hand-edited, and analyzer warnings are
suppressed via the `[**/Migrations/**.{cs,vb}]` section in `.editorconfig`. No
data backfill. If this branch later needs further schema changes, regenerate the
branch's own migration rather than stacking a second one.

## Access Resolution

All authorization currently reads the grant tables directly: `User.HasPermission`
for global perms, and ~10 scattered `AnyAsync`/list queries against
`UserDatabaseRoles` across Query/Update/Schema/Catalog/Auth code. To make group
inheritance correct everywhere with a single implementation, introduce one seam.

### `IAccessResolver`

Lives in `SluiceBase.Api/Auth/`, registered scoped like `CurrentUserAccessor`.

```csharp
internal interface IAccessResolver
{
    // Global perms: union of user_permission + access_group_permission (via membership)
    Task<bool> HasGlobalPermissionAsync(UserId user, string permission, CancellationToken ct);

    // Per-database perms: union of user_database_role + access_group_database_role
    Task<bool> HasDatabasePermissionAsync(UserId user, DatabaseId db, string permission, CancellationToken ct);

    // For list-building endpoints (history, catalog, allowed-db filters)
    Task<IReadOnlySet<DatabaseId>> DatabasesWithPermissionAsync(UserId user, string permission, CancellationToken ct);

    // For /api/me — distinct effective permission strings (global + scopeable)
    Task<IReadOnlySet<string>> EffectivePermissionsAsync(UserId user, CancellationToken ct);
}
```

Each method runs a query that unions the direct grant table with the group grant
table joined through `access_group_member`. The membership join is the only new
clause; it is written and tested once.

### Call sites moved onto the resolver

- `PermissionAuthorizationHandler` and `AnyPermissionAuthorizationHandler` —
  today call `user.HasPermission(perm)` (direct only) → `HasGlobalPermissionAsync`
  so group-granted global perms work. (These handlers currently rely on
  `CurrentUserAccessor`'s eager-loaded `Permissions`; they now take the resolver.)
- `QueryService.ExecuteAsync` — per-DB `query:execute` check →
  `HasDatabasePermissionAsync`.
- `UpdateEndpoints` — the submit/approve/execute `AnyAsync` checks →
  `HasDatabasePermissionAsync`; the `allowedDatabaseIds` list →
  `DatabasesWithPermissionAsync`.
- `SchemaEndpoint` — `hasRole` → `HasDatabasePermissionAsync`.
- `QueryEndpoints.GetHistory` — `auditDatabaseIds` / `anyRoleDatabaseIds` →
  `DatabasesWithPermissionAsync` (plus an any-scopeable variant).
- `CatalogService` — `allowedIds` → `DatabasesWithPermissionAsync`.
- `AuthEndpoints./api/me` — replace the direct-only permission concat with
  `EffectivePermissionsAsync`.

`User.HasPermission` remains as a pure domain helper but is no longer the
authorization path. Group-derived grants are treated identically to direct
grants for every authorization purpose (including `query:audit` and history
visibility) — that consistency is the point.

## Management & Effective-Access API

All endpoints live under the existing `/api/admin` group
(`RequireAuthorization(Permissions.PermissionManage)`), so they inherit current
gateway routing. **Verify** the AppHost YARP allowlist covers `/api/admin/group`
(per the gateway-route rule); the existing `/api/admin/...` prefix routing
should already cover it, but confirm during implementation.

### Group CRUD & contents

- `GET    /api/admin/group` — list groups with member count + grant summary
- `POST   /api/admin/group` — create `{ name, description? }`
- `GET    /api/admin/group/{groupId}` — detail: members, global perms, per-db roles
- `PATCH  /api/admin/group/{groupId}` — rename / edit description
- `DELETE /api/admin/group/{groupId}` — delete (cascades members + grants)
- `POST` / `DELETE /api/admin/group/{groupId}/member/{userId}`
- `POST` / `DELETE /api/admin/group/{groupId}/permission/{permission}` — global,
  validated vs `Permissions.Global`
- `POST` / `DELETE /api/admin/group/{groupId}/database/{databaseId}/role/{permission}`
  — per-db, validated vs `Permissions.Scopeable`

These mirror the request/response shapes already in `PermissionEndpoints` and
`DatabaseRoleEndpoints`, keyed by group.

### Effective access with provenance (enrich existing endpoints — no new read endpoint)

Rather than a parallel `effective-access` endpoint, enrich the three per-user /
per-database read endpoints the admin pages already call, so effective access
and its source flow through the same payload that drives the editable matrix. A
single shared read helper computes provenance (union direct + group tables,
joined to `access_group` for names).

- `GET /api/admin/user` (`ListUsers`, Permission page) — each user's global
  permissions change from `string[]` to
  `EffectivePermission(Permission, FromDirect, FromGroups: GroupRef[])[]`.
- `GET /api/admin/user/{userId}/role` (`ListUserRoles`, Access › By User) —
  return **effective** per-database rows:
  `(DatabaseId, Permission, FromDirect, FromGroups: GroupRef[])`.
- `GET /api/admin/database/{databaseId}/role` (`ListByDatabase`, Access › By
  Database) — same per-user-row enrichment.

`GroupRef(GroupId, Name)`. **Write paths are unchanged:** toggling a checkbox
still assigns/removes a *direct* grant via the existing endpoints. `FromDirect`
drives the editable checkbox; `FromGroups` is a read-only overlay.

OpenAPI/`schema.ts` regeneration is required (CI gates `openapi.json` and
`schema.ts`).

## UI

The Access page (`src/frontend/src/routes/_authed/access.tsx`) keeps its
left-list / right-panel layout and gains one tab. The Permission page gains
provenance display. New TanStack hooks/types go in `api/hooks.ts`.

### New "Groups" tab

A fourth tab alongside By Database / By User / Sensitive Columns:

- **Left:** list of groups (with member count) + "Create group" (name, optional
  description).
- **Right (selected group):**
  - **Members** — add (user `Select`) / remove, like the sensitive-column bypass
    UI.
  - **Global permissions** — checkboxes over `Permissions.Global`.
  - **Per-database roles** — the same server-grouped checkbox matrix used in
    `UserRolePanel`, toggling the group's grants.
  - Rename/delete via a header menu.

### Provenance overlay (the effective/source view)

A single **effective-state marker** per matrix cell encodes four states:

- **None** — empty checkbox.
- **Direct** — solid interactive checkbox (primary color). Toggling edits the
  direct grant.
- **Inherited-only** — a muted, non-interactive checkbox carrying a small
  *people* glyph, with a "via «group»" tooltip. Cannot be toggled here; revoke
  by removing the user from the group.
- **Both** — interactive checkbox with a small corner dot in the inherited hue,
  signalling the direct grant is redundant (also covered by a group).

The inherited hue is one deliberately chosen accent, distinct from the primary
(direct) and success colors already in the palette, and contrast-checked.

Applied to:

- **Access › By User** — databases as rows; cells show direct/inherited/both.
- **Access › By Database** — users as rows for one database; same markers.
- **Permission page** — global perms use the same marker system, plus a Source
  column spelling out the origin ("Server Manage via Data Platform").

### Hooks/types

Add `useGroups`, `useGroup`, `useCreateGroup`, `useUpdateGroup`,
`useDeleteGroup`, `useAddGroupMember`/`useRemoveGroupMember`,
`useAssignGroupPermission`/`useRemoveGroupPermission`,
`useAssignGroupDatabaseRole`/`useRemoveGroupDatabaseRole`. Regenerate
OpenAPI-derived types so the enriched `EffectivePermission` shape flows through.

## Testing

- **Core unit tests** — group entity factories and invariants.
- **Resolver tests** — the union logic in isolation: direct-only, group-only,
  both, multiple groups, membership removed, group deleted, global vs per-db.
  This is the critical correctness surface.
- **API tests** (`SluiceBase.Api.Tests`) — group CRUD, membership, group grants,
  validation (unknown permission, non-scopeable/non-global misuse), and the
  enriched read endpoints' provenance fields.
- **Integration tests** — an end-to-end path: create group, grant per-db role,
  add member, confirm the member can execute a query on that database purely via
  the group; remove membership and confirm access is gone. (Integration tests
  need a healthy Aspire stack; rely on CI as needed.)
- **Frontend tests** — provenance rendering (the four cell states) and the
  Groups tab interactions, alongside the existing access-matrix tests.

## Risks & Considerations

- **Missed check site = security bug.** Centralizing on `IAccessResolver` is the
  mitigation: one correct implementation, audited call-site migration, no inline
  duplication. The call-site list above is the audit checklist.
- **Query cost.** Each resolver call adds a membership join. Acceptable at
  expected scale; the unique indexes support the lookups. Revisit with caching
  only if profiling shows a need (YAGNI for now).
- **Privileged global perms via groups.** A group can grant `permission:manage` /
  `server:manage`. This is intended but high-blast-radius; the UI should make
  granting global perms on a group visually distinct/deliberate.
