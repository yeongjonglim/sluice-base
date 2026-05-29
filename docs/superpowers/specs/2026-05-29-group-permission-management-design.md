# Group/Role-Based Permission Management

**Date:** 2026-05-29  
**Status:** Draft

## Problem

Permissions are assigned directly to individual users — global permissions via `user_permission`, scoped database permissions via `user_database_role`, and sensitive column bypasses via `user_column_bypass`. There is no way to express organizational structure ("the Analytics team can query these databases") or manage permissions in bulk. Onboarding a new team member requires manually granting every permission they need, and changing a team's access requires updating every member individually.

## Goal

Introduce a group abstraction that models organizational structure and reduces repetitive admin work. A group bundles a set of permissions (global, database-scoped, and sensitive column bypasses) that all its members inherit. Users can belong to multiple groups, and their effective permissions are the union of all group grants plus any individual grants.

## Scope

Full-stack: new database tables and EF migration, backend API endpoints, permission resolution changes, and a new frontend admin page with updates to existing admin pages.

## Design

### Data Model

Five new tables, all following the existing audit pattern (who performed the action, when):

**`group`**

| Column | Type | Constraints |
|---|---|---|
| `id` | UUID | PK |
| `name` | varchar(100) | unique, not null |
| `description` | varchar(500) | nullable |
| `created_at` | timestamptz | not null |
| `created_by_id` | UUID | FK → `user`, not null |

**`group_member`**

| Column | Type | Constraints |
|---|---|---|
| `id` | UUID | PK |
| `group_id` | UUID | FK → `group`, not null |
| `user_id` | UUID | FK → `user`, not null |
| `added_at` | timestamptz | not null |
| `added_by_id` | UUID | FK → `user`, not null |

Unique constraint: (`group_id`, `user_id`)

**`group_permission_map`** (mirrors `user_permission`)

| Column | Type | Constraints |
|---|---|---|
| `id` | UUID | PK |
| `group_id` | UUID | FK → `group`, not null |
| `permission` | varchar(64) | not null |
| `granted_at` | timestamptz | not null |
| `granted_by_id` | UUID | FK → `user`, not null |

Unique constraint: (`group_id`, `permission`)

**`group_database_role`** (mirrors `user_database_role`)

| Column | Type | Constraints |
|---|---|---|
| `id` | UUID | PK |
| `group_id` | UUID | FK → `group`, not null |
| `permission` | varchar(64) | not null |
| `database_id` | UUID | FK → `server_database`, not null |
| `granted_at` | timestamptz | not null |
| `granted_by_id` | UUID | FK → `user`, not null |

Unique constraint: (`group_id`, `permission`, `database_id`)

**`group_column_bypass`** (mirrors `user_column_bypass`)

| Column | Type | Constraints |
|---|---|---|
| `id` | UUID | PK |
| `group_id` | UUID | FK → `group`, not null |
| `sensitive_column_id` | UUID | FK → `sensitive_column`, not null |
| `granted_at` | timestamptz | not null |
| `granted_by_id` | UUID | FK → `user`, not null |

Unique constraint: (`group_id`, `sensitive_column_id`)

C# entity names: `Group`, `GroupMember`, `GroupPermissionMap`, `GroupDatabaseRole`, `GroupColumnBypass`.

### New Global Permission

`group:manage` is added to `Permissions.Global`. This permission allows creating, editing, and deleting groups and their assignments. It is separate from `permission:manage` — bootstrap admins do not automatically receive it; it must be granted through the permission UI.

### Permission Resolution

Effective permissions for a user are the union of individual grants and all group grants. This is purely additive — there is no deny/override mechanism.

**Global permissions** — `CurrentUserAccessor` changes to load the union:
- `user_permission WHERE user_id = @id`
- `group_permission_map gpm JOIN group_member gm ON gpm.group_id = gm.group_id WHERE gm.user_id = @id`

The `User.HasPermission()` method continues to work as-is since it checks an in-memory list that is now populated from the union.

**Scoped database permissions** — each endpoint handler's `AnyAsync` check expands to OR across both sources:
- `user_database_role WHERE user_id = @id AND permission = @p AND database_id = @dbId`
- `group_database_role gdr JOIN group_member gm ON gdr.group_id = gm.group_id WHERE gm.user_id = @id AND gdr.permission = @p AND gdr.database_id = @dbId`

**Sensitive column bypasses** — same union pattern:
- `user_column_bypass WHERE user_id = @id AND sensitive_column_id = @scId`
- `group_column_bypass gcb JOIN group_member gm ON gcb.group_id = gm.group_id WHERE gm.user_id = @id AND gcb.sensitive_column_id = @scId`

**`/api/me`** — continues to return a flat array of permission strings (union of all sources). No group membership information is exposed here; the frontend does not need to know which groups the user belongs to.

**Caching** — no changes. `CurrentUserAccessor` already caches per-request. At this scale (small user base, few groups), querying group membership from DB on each request is fine.

### API Endpoints

#### Group CRUD and Membership (require `group:manage`)

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/admin/group` | List all groups (with member count) |
| `POST` | `/api/admin/group` | Create group (name, description) |
| `PUT` | `/api/admin/group/{groupId}` | Update group name/description |
| `DELETE` | `/api/admin/group/{groupId}` | Delete group (cascades memberships and all permission assignments) |
| `GET` | `/api/admin/group/{groupId}/member` | List members of a group |
| `POST` | `/api/admin/group/{groupId}/member` | Add user to group |
| `DELETE` | `/api/admin/group/{groupId}/member/{userId}` | Remove user from group |

#### Group Global Permissions (require `group:manage`)

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/admin/group/{groupId}/permission` | List group's global permissions |
| `POST` | `/api/admin/group/{groupId}/permission` | Grant global permission to group |
| `DELETE` | `/api/admin/group/{groupId}/permission/{permission}` | Revoke global permission from group |

#### Database Roles — Extended (existing endpoints updated)

The existing database role endpoints are restructured to support both user and group targets with symmetric path segments.

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/admin/database/{databaseId}/role` | `permission:manage` OR `group:manage` | List all role assignments (returns both user and group entries with a `type` discriminator) |
| `POST` | `/api/admin/database/{databaseId}/role` | `permission:manage` for `userId`, `group:manage` for `groupId` | Assign role — body contains exactly one of `{userId, permission}` or `{groupId, permission}` |
| `DELETE` | `/api/admin/database/{databaseId}/role/user/{userId}/{permission}` | `permission:manage` | Remove user role assignment |
| `DELETE` | `/api/admin/database/{databaseId}/role/group/{groupId}/{permission}` | `group:manage` | Remove group role assignment |

The existing `DELETE /api/admin/database/{databaseId}/role/{userId}/{permission}` route changes to `DELETE /api/admin/database/{databaseId}/role/user/{userId}/{permission}` (breaking change).

#### Sensitive Column Bypasses — Extended (existing endpoints updated)

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass` | `permission:manage` OR `group:manage` | List all bypasses (returns both user and group entries with a `type` discriminator) |
| `POST` | `/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass` | `permission:manage` for `userId`, `group:manage` for `groupId` | Grant bypass — body contains exactly one of `{userId}` or `{groupId}` |
| `DELETE` | `/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/user/{userId}` | `permission:manage` | Remove user bypass |
| `DELETE` | `/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/group/{groupId}` | `group:manage` | Remove group bypass |

The existing `DELETE .../bypass/{userId}` route changes to `DELETE .../bypass/user/{userId}` (breaking change).

#### User Roles — Existing Endpoint Updated

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/admin/user/{userId}/role` | `permission:manage` | List all role assignments for a user (includes source: `"direct"` or `"group"` with group name) |

#### Admin User List — Existing Endpoint Updated

`GET /api/admin/user` response adds group memberships per user.

### Frontend

#### New Page: `/group` (requires `group:manage`)

**Group list view** — table of all groups showing name, description, and member count. Actions: create, edit, delete.

**Group detail view** — when clicking a group, shows tabbed interface:

- **Members tab** — list of group members with add/remove actions
- **Permissions tab** — toggle grid for global permissions (same pattern as `/permission` page)
- **Database access tab** — matrix for database role assignments (same pattern as "By User" tab in `/access`)
- **Sensitive columns tab** — manage column bypasses for the group

#### Updated: `/access` page

- **By Database tab** — role assignment list shows both user and group entries with a type indicator. The assign form supports picking either a user or a group as the target.
- **By User tab** — when viewing a user's roles, indicates which assignments come from group membership vs direct assignment (read-only indicator for group-sourced ones, since they can't be removed from the user view).

#### Updated: `/access` Sensitive Columns tab

Bypass list shows both user and group entries. The grant form supports either a user or group target.

#### Updated: `/permission` page

Add a section or tab for group global permissions alongside the existing user permission grid.

#### Updated: Admin user list

Show group memberships per user.

#### `permission.ts`

Add `group:manage` to the `Permission` union type.

#### `/api/me` response

No changes — continues to return a flat permission string array.

### Authorization Policy Registration

In `AuthSetup.cs`, register a new policy for `group:manage`:
```
options.AddPolicy(Permissions.GroupManage, p => p.AddRequirements(new PermissionRequirement(Permissions.GroupManage)));
```

Bootstrap admin configuration is unchanged — only `permission:manage` is granted automatically on first OIDC login.

## Breaking Changes

Two existing DELETE routes are restructured with explicit `/user/` path segments:
- `DELETE /api/admin/database/{databaseId}/role/{userId}/{permission}` → `.../role/user/{userId}/{permission}`
- `DELETE /api/admin/database/{databaseId}/sensitive-column/{id}/bypass/{userId}` → `.../bypass/user/{userId}`

Frontend API client hooks must be updated to match.

## Out of Scope

- Nested/hierarchical groups
- Deny/override permissions
- OIDC claim-based automatic group assignment
- Group-to-group relationships
- Bulk import/export of groups
- Changes to bootstrap admin configuration
