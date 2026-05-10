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

### Server (`SluiceBase.Core.Servers.Server`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `ServerId` | |
| `Name` | `string` | Unique among non-deleted servers |
| `Kind` | `string` | e.g. `"postgres"`, `"mysql"` |
| `Host` | `string` | |
| `Port` | `int` | |
| `IsEnabled` | `bool` | |
| `DeletedAt` | `DateTimeOffset?` | null = active |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

Removes: `Database`, `ReadUsername`, `EncryptedReadPassword`, `WriteUsername`, `EncryptedWritePassword`.

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
| `IsEnabled` | `bool` | |
| `DeletedAt` | `DateTimeOffset?` | null = active |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

`CanWrite => WriteCredentialId.HasValue`

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

The factory loads the `Database` row, joins `Server` and the relevant `Credential` (`ReadCredentialId` or `WriteCredentialId` based on `CredentialKind`), decrypts the password, and builds the driver-specific connection string from `Server.Host`, `Server.Port`, `Database.DatabaseName`, `Credential.Username`, and the decrypted password. `Server.Kind` drives which driver/builder is used. Requesting `CredentialKind.Write` when `Database.WriteCredentialId` is null throws `InvalidOperationException`, identical to the current `HasWriteCredential` guard.

All call sites that currently pass `ServerId` — `QueryEndpoints`, `UpdateEndpoints`, `SchemaEndpoint`, `ServerEndpoints` (test connection) — switch to `DatabaseId`.

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

## Migration

Project is not live. All existing EF migrations are dropped and recreated from scratch against the new schema. `DevServerSeed.cs` is updated to create one `Server`, one or two `Credential` rows, and one `Database` row per seeded target.
