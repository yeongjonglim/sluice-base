# Database Access Scoping Design

**Date:** 2026-05-15
**Status:** Approved

## Overview

Currently SluiceBase controls *what* users can do via permissions, but not *where* they can do it. Any user with `query:execute` can query any database; any user with `update:submit` can target any database. This feature adds database-level access scoping so that admins can control which specific databases each user can operate against, per permission.

## Decisions

- **Scoping granularity:** Database-level only. Every operation already targets a specific database; server-level scoping adds ambiguous "all future databases" semantics.
- **Scopeable permissions:** The 5 operational permissions — `query:execute`, `query:audit`, `update:submit`, `update:approve`, `update:execute`. The administrative permissions `permission:manage` and `server:manage` are global and remain unscoped.
- **Default access:** Closed by default. No role assignment = no access. A migration seeds full access for all existing users.
- **Mental model:** Resource-first assignment (Azure IAM style). Admins assign a role to a database for a user — granting the database role IS the grant, with no separate "enable permission globally" step.
- **Existing permissions page:** Unchanged for now; it continues to manage `permission:manage` and `server:manage` only. The 5 scopeable permissions are removed from that page.

## Data Model

### New table: `user_database_role`

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | |
| `user_id` | UUID FK → `user` | CASCADE delete |
| `permission` | varchar(64) | one of the 5 scopeable permissions |
| `database_id` | UUID FK → `server_database` | no cascade (soft-delete pattern) |
| `granted_at` | timestamptz | |
| `granted_by_id` | UUID FK → `user` | SET NULL on delete |

**Unique constraint:** `(user_id, permission, database_id)`

### Changes to `user_permission`

The 5 scopeable permissions are removed from `user_permission` entirely — both existing rows (cleaned up in migration) and from the permission catalog. `user_permission` retains only `permission:manage` and `server:manage`.

## Backend

### Enforcement

The existing `PermissionAuthorizationHandler` continues to handle `server:manage` and `permission:manage`. For the 5 scopeable permissions, enforcement happens inline at each endpoint by checking `user_database_role` for the specific `database_id` in the request or on the targeted resource. Returns 403 if no matching row exists.

**Affected endpoints:**

| Endpoint | Check |
|---|---|
| `POST /api/query` | `query:execute` on request `database_id` |
| `GET /api/query/history` | Filter to databases where user has `query:audit`; users without `query:audit` see only their own queries on databases where they currently have any role (revoked access hides history) |
| `POST /api/update` | `update:submit` on request `database_id` |
| `GET /api/update` | Filter results to databases where user has at least one of `update:submit`, `update:approve`, or `update:execute` |
| `GET /api/update/{id}` | Check user has at least one of `update:submit`, `update:approve`, or `update:execute` on the update's database |
| `POST /api/update/{id}/approve`, `/reject` | `update:approve` on the update's database |
| `POST /api/update/{id}/execute` | `update:execute` on the update's database |
| `POST /api/update/{id}/cancel` | `update:submit` on the update's database |

### Catalog

The virtual `catalog:read` policy is updated: access is granted if the user has `server:manage` OR has at least one `user_database_role` entry. The catalog response filters the database list to only those the user has a role on — users with `server:manage` continue to see all databases.

### New API Endpoints

All protected by `permission:manage`.

| Method | Path | Body | Description |
|---|---|---|---|
| `GET` | `/api/admin/database/{databaseId}/role` | — | All assignments on a database |
| `POST` | `/api/admin/database/{databaseId}/role` | `{ userId, permission }` | Create assignment (database-first) |
| `DELETE` | `/api/admin/database/{databaseId}/role/{userId}/{permission}` | — | Remove assignment |
| `GET` | `/api/admin/user/{userId}/role` | — | All assignments for a user |
| `POST` | `/api/admin/user/{userId}/role` | `{ databaseId, permission }` | Create assignment (user-first) |

The two POST shapes are equivalent under the hood — same insert, two URL shapes for the dual-view UX.

## Frontend

### New Route: `/access`

Visible only to users with `permission:manage`. Two tabs: **By Database** and **By User**.

**By Database tab:**
- Left panel: tree of servers → databases
- Right panel: on selecting a database, shows a table of `(User, Permission)` assignments with a remove action per row
- "Add Assignment" button opens a modal with user picker + permission picker (filtered to the 5 scopeable permissions)
- Disabled/soft-deleted databases are shown greyed out and non-interactive

**By User tab:**
- List of all users
- Expanding a user shows their `(Database, Permission)` assignments with a remove action per row
- "Add Assignment" button opens a modal with database picker (grouped by server) + permission picker
- Both tabs share the same underlying API

### Existing Permissions Page

Unchanged. The 5 scopeable permissions are removed from the permission catalog returned to that page, so they no longer appear as grantable options there.

## Migration

A single EF migration with two steps:

1. **Create `user_database_role`** table with the schema above.

2. **Seed compatibility data:**
   - Cross-join `user_permission` (rows where `permission` is one of the 5 scopeable values) with `server_database` (where `deleted_at IS NULL`) and insert one `user_database_role` row per combination, recording `granted_at = now()` and `granted_by_id = NULL`.
   - Delete all rows from `user_permission` where `permission` is one of the 5 scopeable values.

For a fresh install with no data this is a no-op. For existing installs it ensures no access regressions.

## Out of Scope

- Promoting the existing permissions page into a unified permission+scope management view (future work).
- Server-level scoping.
- Role grouping (assigning multiple permissions as a named role).
