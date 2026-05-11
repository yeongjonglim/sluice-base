# Server / Credential / Database Schema Redesign

**Date:** 2026-05-10  
**Status:** Approved

## Problem

The current `Server` entity conflates three distinct concepts: the physical host (`host`, `port`, `kind`), the target database (`Database`), and credentials (`ReadUsername`/`EncryptedReadPassword`, optional write pair). This prevents:

- Multiple databases on the same physical server without duplicating host/port/kind.
- Sharing a single credential across multiple databases on the same server.
- Unambiguous audit references — `QueryLog.ServerId` and `UpdateRequest.ServerId` point to a combined host+database+credential record rather than the specific database the query targeted.

## Domain Model

`Server` is the aggregate root. `Credential` and `Database` are its children. Their full lifecycle — including soft-delete — is controlled through `Server`.

All three entities support soft-delete only. Hard-delete is never exposed. Soft-deleting a `Server` cascades `DeletedAt` to all its `Credential` and `Database` children via `Server.SoftDelete()` — not a DB-level cascade trigger.

`IsDisabled` replaces the former `IsEnabled` on `Server` and `Database`. The default is `false` (active). An explicit `IsDisabled = true` flag must be set to disable. This avoids the confusing double-negative of setting `IsEnabled = false` to turn something off.

### Server (`SluiceBase.Core.Servers.Server`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `ServerId` | |
| `Name` | `string` | Unique among non-deleted servers |
| `Kind` | `string` | e.g. `"postgres"`, `"mysql"` |
| `Host` | `string` | |
| `Port` | `int` | |
| `IsDisabled` | `bool` | Default `false`. Disabling a server makes all its databases unreachable. |
| `DeletedAt` | `DateTimeOffset?` | null = active |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

Removes: `Database`, `ReadUsername`, `EncryptedReadPassword`, `WriteUsername`, `EncryptedWritePassword`, `IsEnabled`.

### Credential (`SluiceBase.Core.Servers.Credential`)

A named username/password pair scoped to a server. An individual credential soft-delete is rejected at the application layer if any active (non-soft-deleted) `Database` on the same server still references it. This constraint does not apply during a server-level cascade, where all children are soft-deleted simultaneously.

| Field | Type | Notes |
|---|---|---|
| `Id` | `CredentialId` | |
| `ServerId` | `ServerId` | FK to `Server` |
| `Label` | `string` | Human name, e.g. `"Read-only role"` |
| `Username` | `string` | |
| `EncryptedPassword` | `string` | |
| `DeletedAt` | `DateTimeOffset?` | null = active |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

### Database (`SluiceBase.Core.Servers.Database`)

The queryable target — what `QueryLog` and `UpdateRequest` reference.

| Field | Type | Notes |
|---|---|---|
| `Id` | `DatabaseId` | |
| `ServerId` | `ServerId` | FK to `Server` |
| `DisplayName` | `string` | Shown in UI, e.g. `"Production Reports"` |
| `DatabaseName` | `string` | Passed to driver, e.g. `"reporting_db"` |
| `ReadCredentialId` | `CredentialId` | FK to `Credential` |
| `WriteCredentialId` | `CredentialId?` | null = read-only target |
| `IsDisabled` | `bool` | Default `false` |
| `DeletedAt` | `DateTimeOffset?` | null = active |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

`CanWrite => WriteCredentialId.HasValue`

## Disabled Entity Behaviour

The connection factory checks `Server.IsDisabled` and `Database.IsDisabled` before building a connection string. If either is `true`, it throws `InvalidOperationException`. The query and update endpoints surface this as `400 Bad Request`. This prevents queries against intentionally disabled targets at the application layer. Requesting `CredentialKind.Write` when `Database.WriteCredentialId` is null also throws `InvalidOperationException`, identical to the current `HasWriteCredential` guard.

## Audit Trail

`QueryLog` and `UpdateRequest` replace `ServerId` with `DatabaseId` (nullable FK to `server_database`).

`DatabaseId` records the connection context — the specific database SluiceBase opened the connection against. Cross-database access within that connection (e.g. MySQL `db1.t JOIN db2.t`, SQL Server linked servers) is visible in the logged SQL text. No schema change is needed for cross-database queries.

EF FK configuration uses `OnDelete(DeleteBehavior.Restrict)` for the `database_id` FK on both `query_log` and `update_request`. Since databases are never hard-deleted, the constraint is never at risk; `Restrict` acts as a safeguard against accidental hard deletes.

## Connection Factory

```csharp
// Before
GetConnectionStringAsync(ServerId, CredentialKind, CancellationToken)

// After
GetConnectionStringAsync(DatabaseId, CredentialKind, CancellationToken)
```

The factory loads the `Database` row, joins `Server` and the relevant `Credential` (`ReadCredentialId` or `WriteCredentialId` based on `CredentialKind`), checks neither `Server.IsDisabled` nor `Database.IsDisabled`, decrypts the password, and builds the driver-specific connection string from `Server.Host`, `Server.Port`, `Database.DatabaseName`, `Credential.Username`, and the decrypted password. `Server.Kind` drives which driver/builder is used.

All call sites that currently pass `ServerId` — `QueryEndpoints`, `UpdateEndpoints`, `SchemaEndpoint`, `ServerEndpoints` (test connection) — switch to `DatabaseId`.

## API Endpoints

### Server endpoints (`/api/server`)

| Method | Path | Change |
|---|---|---|
| `GET` | `/api/server` | Response now includes nested `credentials[]` and `databases[]` per server |
| `POST` | `/api/server` | Creates server only (no credential/database fields). Returns `ServerResponse`. |
| `PUT` | `/api/server/{id}` | Updates server-level fields: `name`, `host`, `port`, `kind`, `isDisabled`. Removes credential/database fields. |
| `DELETE` | `/api/server/{id}` | Soft-deletes server and all its credentials and databases. Returns `204`. |

### Credential endpoints (`/api/server/{serverId}/credential`)

New resource, all require `server:manage`.

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/server/{serverId}/credential` | Add credential (`label`, `username`, `password`) |
| `PUT` | `/api/server/{serverId}/credential/{credentialId}` | Update `label`, `username`, and/or `password` |
| `DELETE` | `/api/server/{serverId}/credential/{credentialId}` | Soft-delete. Returns `409` if any active database still references it. |

### Database endpoints (`/api/server/{serverId}/database`)

New resource, all require `server:manage`.

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/server/{serverId}/database` | Add database (`displayName`, `databaseName`, `readCredentialId`, `writeCredentialId?`) |
| `PUT` | `/api/server/{serverId}/database/{databaseId}` | Update any database fields including `isDisabled` |
| `DELETE` | `/api/server/{serverId}/database/{databaseId}` | Soft-delete |
| `POST` | `/api/server/{serverId}/database/{databaseId}/test` | Test connection (moved from server-level). Returns `read` and `write` connectivity results. |

### Query and update endpoints

`POST /api/query` body field: `serverId` → `databaseId`  
`GET /api/schema/{serverId}` path param: `serverId` → `databaseId`  
`POST /api/update` body field: `serverId` → `databaseId`

## DB Table Names

| Concept | Table |
|---|---|
| Server | `server` |
| Credential | `server_credential` |
| Database | `server_database` |

`database` is a reserved word in most SQL dialects; `server_database` avoids ambiguity.

Unique index on `server.name` becomes a **partial index** excluding soft-deleted rows (`WHERE deleted_at IS NULL`), allowing name reuse after soft-delete.

## Permissions

`server:manage` covers all three entities — admins who can manage servers can manage credentials and databases on those servers. No new permission is introduced.

## Frontend Changes

### Server management page (`routes/_authed/server.tsx`)

The flat single-form UX becomes a hierarchical view:

- **Server table**: each row shows `name`, `kind`, `host:port`, `isDisabled` badge, and actions (edit, soft-delete). Expand a row to reveal its credentials and databases sub-tables.
- **Credentials sub-table**: columns `label`, `username`, actions (edit, soft-delete). Soft-delete is disabled (greyed out) if any active database references the credential. Add button opens a form: `label`, `username`, `password`.
- **Databases sub-table**: columns `display name`, `database name`, `read credential`, `write credential`, `isDisabled`, actions (toggle disabled, soft-delete, test connection). Add/edit form: `displayName`, `databaseName`, `readCredentialId` (select from server's active credentials), `writeCredentialId` (optional select), `isDisabled`.
- **Test connection** action on each database row (not on the server row). Shows `read: OK/Failed` and `write: OK/Failed/N/A`.
- **Add server** form is simplified: `name`, `kind`, `host`, `port`. Credentials and databases are added in the expanded sub-tables after the server is created.
- The delete action becomes a soft-delete: calls `DELETE /api/server/{id}`, same 204 response, but server disappears from list. No UI change needed beyond removing any confirmation that says "permanently delete".

### Query page (`routes/_authed/query.tsx`)

- `selectedServerId` → `selectedDatabaseId`.
- Server dropdown replaced by database dropdown. Each option shows `database.displayName`; the option value is `database.id`.
- Only databases where `!server.isDisabled && !database.isDisabled` appear in the dropdown.
- Schema sidebar loads via `GET /api/schema/{databaseId}`.
- `executeQuery.mutate({ databaseId, sql })` (was `serverId`).
- Placeholder text: "Select a database" (was "Select a server").

### Update new page (`routes/_authed/update/new.tsx`)

- `serverId` state → `databaseId`.
- Database dropdown filters to databases where `database.canWrite && !server.isDisabled && !database.isDisabled`.
- `submit.mutate({ databaseId, sqlText, reason })` (was `serverId`).
- Label: "Database" (was "Server").

### API client (`api/schema.ts` and `api/hooks.ts`)

`schema.ts` is auto-generated — regenerate from the updated OpenAPI spec after backend changes are complete.

New hooks needed in `hooks.ts`:
- `useServerDatabases(serverId)` — fetches databases for a server (or use the nested response from `useServers`)
- `useCreateCredential`, `useUpdateCredential`, `useDeleteCredential`
- `useCreateDatabase`, `useUpdateDatabase`, `useDeleteDatabase`
- `useTestDatabaseConnection`

Existing hooks to update:
- `useServers` — return type widens to include `credentials[]` and `databases[]` nested in each server item
- `useDeleteServer` — semantics unchanged (soft-delete is transparent at the HTTP level)
- `useExecuteQuery` — body sends `databaseId`
- `useSchema` — path param is `databaseId`
- `useSubmitUpdate` — body sends `databaseId`

## Testing

### Backend integration tests

All existing tests in `ServerEndpointTests.cs`, `QueryEndpointTest.cs`, `SchemaEndpointTests.cs`, and `UpdateEndpointTests.cs` must pass with the new API shape. Key changes:

- `CreateServerAsync` helper splits into three steps: create server → create credential(s) → create database. The returned reference passed to query/update/schema tests is a `DatabaseId`, not `ServerId`.
- `DeleteServer_RemovesFromList` becomes `SoftDeleteServer_RemovesFromList` and additionally asserts that the server's credentials and databases also no longer appear.
- `UpdateServer_*` tests update server-level fields only; credential and database mutations get their own test methods.

New test cases required:

| Test | Expectation |
|---|---|
| `DeleteCredential_ReferencedByActiveDatabase_Returns409` | Soft-delete blocked while a live database still references it |
| `DeleteCredential_AfterDatabaseSoftDeleted_Succeeds` | Soft-delete allowed once no active database references it |
| `SoftDeleteServer_CascadesToCredentialsAndDatabases` | Credentials and databases for that server all have `deleted_at` set; they do not appear in list responses |
| `Query_DisabledDatabase_Returns400` | Setting `isDisabled = true` on a database blocks query execution |
| `Query_DisabledServer_Returns400` | Setting `isDisabled = true` on a server blocks query execution against any of its databases |
| `TestConnection_MovedToDatabaseLevel` | `POST /api/server/{sid}/database/{did}/test` returns connectivity results |
| `CreateDatabase_WithSharedCredential_Succeeds` | Two databases on same server share one credential |

### Frontend unit tests (`api/__tests__/server-hooks.test.ts`)

Existing `useServers` and `useCreateServer` tests must be updated to the new response shape. New test stubs needed for `useCreateCredential`, `useDeleteCredential`, `useCreateDatabase`, `useDeleteDatabase`.

## Migration

Project is not live. All existing EF migrations are dropped and recreated from scratch against the new schema. `DevServerSeed.cs` is updated to: create one `Server`, create one `server_credential` (read) and one `server_credential` (write) per seeded server, then create one `server_database` referencing those credentials per seeded server.
