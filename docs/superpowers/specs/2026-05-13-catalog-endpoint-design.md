# Catalog Endpoint — Design Spec

**Date:** 2026-05-13
**Status:** Approved

## Problem

Users without `server:manage` cannot list servers, blocking query and update workflows. The entire `/api/server` route group is guarded by `server:manage`, including the `GET /` list endpoint that the frontend needs to populate the database selector.

## Decision

Add a new `GET /api/catalog/server` endpoint in a separate route group. It returns a trimmed server/database list to any user with an operational permission. The existing `/api/server` group is untouched.

## Authorization

A new virtual policy `catalog:read` (not user-assignable) is satisfied by any of:
- `query:execute`
- `update:submit`
- `update:approve`
- `update:execute`
- `server:manage`

Uses the existing `AnyPermissionRequirement` handler already wired up in `AuthSetup.cs`.

## Backend

### `Permissions.cs`
Add:
```csharp
public const string CatalogRead = "catalog:read";
```
Not added to `Permissions.All` (virtual policy only).

### `AuthSetup.cs`
Register the new policy alongside `UpdateAny`:
```csharp
options.AddPolicy(Permissions.CatalogRead, policy =>
    policy.Requirements.Add(new AnyPermissionRequirement([
        Permissions.QueryExecute,
        Permissions.UpdateSubmit,
        Permissions.UpdateApprove,
        Permissions.UpdateExecute,
        Permissions.ServerManage,
    ])));
```

### `CatalogEndpoints.cs` (new)
- Route prefix: `/api/catalog`, single endpoint `GET /server`
- Auth: `.RequireAuthorization(Permissions.CatalogRead)`
- Query: non-deleted, non-disabled servers; non-deleted, non-disabled databases; ordered by server name
- Response shape:

```csharp
record CatalogServersResponse(IReadOnlyList<CatalogServerItem> Servers);

record CatalogServerItem(
    ServerId Id,
    string Name,
    IReadOnlyList<CatalogDatabaseItem> Databases);

record CatalogDatabaseItem(
    DatabaseId Id,
    string DisplayName,
    bool CanWrite);
```

No credentials, host, port, connection strings, or timestamps in the response.

### `Program.cs`
Call `CatalogEndpoints.Map(app)` alongside existing endpoint registrations.

## Frontend

### `schema.ts`
Add path `/api/catalog/server` with GET 200 response referencing new component schemas `CatalogServersResponse`, `CatalogServerItem`, `CatalogDatabaseItem`. No changes to existing `/api/server` path schemas.

### `hooks.ts`
Add `useCatalogServer()` hook calling `GET /api/catalog/server`. Existing `useServers()` is unchanged.

### `query/index.tsx`, `query/history.tsx`, `update/new.tsx`
Switch from `useServers()` to `useCatalogServers()`. Update type references to use the trimmed schema (remove references to `credentials`, `host`, `port`, `kind`, etc.).

## Testing

### `CatalogEndpointTests.cs` (new)
| Scenario | Expected |
|---|---|
| User with `query:execute` only | 200, trimmed response |
| User with `update:submit` only | 200 |
| User with `server:manage` | 200 |
| Unauthenticated | 401 |
| Authenticated, no relevant permission | 403 |
| Disabled server excluded | not in response |
| Disabled database excluded | not in server's databases list |
| No credentials/host/port/timestamps in response | verified |

### `QueryEndpointTest.cs`, `UpdateEndpointTests.cs`
Remove the `server:manage` grant that was added as a workaround. Tests should pass with only the operational permission.

## Out of Scope

- Per-server or per-database authorization (no server-level ACLs in v1)
- Pagination or filtering on the catalog endpoint
- Changes to the `/api/server` management endpoints
