# PgWire Proxy — Design Spec

## Problem

SluiceBase users can only query databases through the web UI. Users want to use their preferred native tools (psql, DBeaver, DataGrip, Claude Code via psql) while retaining all of SluiceBase's protections: read-only enforcement, sensitive column blocking, per-database permissions, session-gated access, and audit logging.

## Solution

An in-process PostgreSQL wire protocol (PgWire) proxy running as a `BackgroundService` inside `SluiceBase.Api`. It listens on a dedicated TCP port, speaks PgWire to clients, and routes queries through the same shared service layer used by the HTTP API.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   SluiceBase.Api                    │
│                                                     │
│  ┌───────────┐         ┌──────────────────────┐     │
│  │ HTTP API  │         │   PgWire Proxy        │     │
│  │ (Kestrel) │         │   (TCP :6432)         │     │
│  │           │         │                        │     │
│  │ POST      │         │  1. PgWire handshake   │     │
│  │ /api/query│         │  2. Auth (credential +  │     │
│  │           │         │     session validation) │     │
│  └─────┬─────┘         │  3. Query interception  │     │
│        │               │  4. Protection pipeline │     │
│        │               │  5. PgWire result       │     │
│        │               └──────────┬─────────────┘     │
│        │                          │                    │
│  ┌─────▼──────────────────────────▼─────────────┐     │
│  │          Shared Service Layer                 │     │
│  │  - ITargetEngine (query execution)            │     │
│  │  - SqlColumnChecker (sensitive col blocking)  │     │
│  │  - Permission checks (query:execute)          │     │
│  │  - IServerConnectionFactory (connections)     │     │
│  │  - QueryLog (audit trail)                     │     │
│  │  - Session validation (SSO check)             │     │
│  └──────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────┘
```

Both entry points converge on the same shared service layer — same permission checks, same sensitive column blocking, same audit logging, same query execution engine.

The proxy is designed with protocol-agnostic internals so that adding MySQL wire protocol support later requires only a new listener and message codec, not architectural changes.

## Authentication & Session Validation

### Credential Lifecycle

1. User logs into SluiceBase via OIDC (existing flow).
2. User navigates to a database's detail page and clicks "Generate Connection String."
3. SluiceBase generates a 32-byte random opaque token, stores a SHA-256 hash in the database linked to the user ID and database ID.
4. The UI displays the connection string exactly once:
   ```
   psql "host=<configured-host> port=6432 user=<credential-id> password=<token> dbname=<db-slug>"
   ```
5. The raw token is never stored or shown again.

The proxy's externally-reachable host and port are driven by configuration (`Proxy__ExternalHost`, `Proxy__ExternalPort`) so the generated connection string is correct for the deployment environment.

### Connection-Time Validation

Three checks, all must pass:

| Step | Check | Failure |
|------|-------|---------|
| 1 | **Credential lookup** — hash the provided password, find a matching `ProxyCredential` record that is not revoked | `ErrorResponse`: "invalid credentials" |
| 2 | **Session gate** — verify the user has an active SSO session in SluiceBase | `ErrorResponse`: "no active session — please log into SluiceBase" |
| 3 | **Permission check** — verify the user has `query:execute` on the target database | `ErrorResponse`: "access denied to database" |

### Credential Revocation

- **Self-service:** Users revoke their own credentials from the UI.
- **Admin:** Users with `permission:manage` can revoke any user's credentials.
- **Effect:** Revoking a credential immediately terminates all active proxy connections using it.
- **Session expiry:** When an SSO session expires, all proxy connections derived from that session are terminated immediately — no grace period.

### Data Model

```
ProxyCredential
├── Id (Guid, PK)
├── UserId (FK → Users)
├── DatabaseId (FK → Databases)
├── CredentialId (string, unique — used as PgWire username)
├── TokenHash (string, SHA-256 of raw token)
├── CreatedAt (DateTime)
├── LastUsedAt (DateTime?)
└── RevokedAt (DateTime?)
```

## PgWire Protocol Scope

The proxy implements the **simple query** subset of the PostgreSQL wire protocol:

| Message | Direction | Purpose |
|---------|-----------|---------|
| `StartupMessage` | Client → Proxy | Connection initiation with user/dbname params |
| `SSLRequest` | Client → Proxy | TLS upgrade negotiation |
| `AuthenticationCleartextPassword` | Proxy → Client | Request password |
| `PasswordMessage` | Client → Proxy | Client sends password |
| `AuthenticationOk` | Proxy → Client | Auth succeeded |
| `ReadyForQuery` | Proxy → Client | Proxy ready to accept queries |
| `Query` | Client → Proxy | SQL text from user |
| `RowDescription` | Proxy → Client | Column metadata |
| `DataRow` | Proxy → Client | One result row |
| `CommandComplete` | Proxy → Client | Query finished |
| `ErrorResponse` | Proxy → Client | Error with SQLSTATE code + message |
| `Terminate` | Client → Proxy | Client disconnects |

The extended query protocol (Parse/Bind/Describe/Execute) is out of scope initially. Simple query covers psql interactive use, CLI scripts, and most basic tool integrations.

**SSL:** The proxy supports `SSLRequest` → TLS upgrade so credentials are not sent in cleartext.

### What the Proxy Does NOT Allow

- Write operations — enforced by `SET TRANSACTION READ ONLY` in `ITargetEngine`
- `SET` commands that alter session state on the target database
- `COPY` operations
- Prepared statements (initially)

### Meta-Commands

psql meta-commands (e.g., `\dt`, `\d tablename`) translate to catalog queries. These are treated like any other query — they go through the full pipeline (permissions, sensitive column checks, audit logging). They naturally pass since they're read-only and don't reference user data columns.

## Query Execution Pipeline

```
Client sends Query(sql)
         │
         ▼
    Permission check — user has query:execute on this database?
         │
         ▼
    SqlColumnChecker — references sensitive columns? user has bypass?
         │
         ▼
    ITargetEngine.ExecuteQueryAsync() — SET TRANSACTION READ ONLY, execute on target DB
         │
         ▼
    QueryLog — audit: user, SQL, duration, row count, status
         │
         ▼
    Format as PgWire response — RowDescription + DataRow + CommandComplete
         │
         ▼
    Send to client
```

This is the same pipeline as `POST /api/query`, with PgWire framing instead of JSON. Full protection parity.

## Connection Management & Lifecycle

### Connection State Machine

```
    Connect (TCP)
         │
         ▼
    ┌──────────┐    auth failed     ┌────────────┐
    │  STARTUP  │──────────────────▶│   CLOSED    │
    └────┬─────┘                    └────────────┘
         │ auth OK                        ▲
         ▼                                │
    ┌──────────┐   session expired /      │
    │  ACTIVE   │──revoked / terminate────┘
    └────┬─────┘
         │ client sends Query
         ▼
    ┌──────────┐   result / error
    │ QUERYING  │─────────────────▶ back to ACTIVE
    └──────────┘
```

### Session Heartbeat

A background timer checks session validity for all active proxy connections every 60 seconds. On session expiry or credential revocation, the proxy sends an `ErrorResponse` and closes the TCP connection immediately.

### Target Database Connections

The proxy does not maintain persistent connections to the target database. Each query opens a connection via `IServerConnectionFactory`, executes in a read-only transaction, and returns the connection to the pool. This matches the existing `POST /api/query` behavior. Npgsql's built-in connection pooling handles efficiency.

### Concurrency

- Each client connection runs on its own async task.
- Active connections tracked in `ConcurrentDictionary<Guid, ProxyConnection>` for revocation/expiry sweeps.
- Configurable connection limit: `Proxy__MaxConnections` (default 100).

### Aspire Integration

- Proxy port registered as an endpoint in the Aspire `AppHost` (visible in the dashboard).
- Health checks report proxy status: listening state, active connection count.

## UI — Credential Management

### Database Detail Page

A "Proxy Access" section on the existing database detail page:

- **Generate Connection String** button — disabled with tooltip if user lacks `query:execute`.
- Clicking it generates the credential and shows a modal with the connection string (copy button, shown once).
- Below the button: a list of the user's credentials for this database showing credential ID (truncated), created date, last used date, and a "Revoke" button.

### Admin View

Users with `permission:manage` see a global proxy credentials page listing all credentials across all users and databases, with the ability to revoke any credential.

### No Editing

Credentials can only be created or revoked — never modified. To change the target database, revoke the old credential and create a new one.

## Testing Strategy

### Aspire Integration Tests (Happy Path)

- Spin up the full Aspire stack (SluiceBase API + proxy + metadata Postgres + target Postgres).
- Create a user, add a server/database, grant `query:execute`.
- Generate a proxy credential via the API.
- Connect to the proxy with `Npgsql` using the generated connection string.
- Execute a query and verify results.
- Validates the full end-to-end flow in a realistic deployment topology.

### Testcontainers Tests (Edge Cases & Security)

- Auth rejection: invalid credentials → `ErrorResponse`.
- Session gate: valid credential, no active session → rejection.
- Permission denial: no `query:execute` → rejection.
- Sensitive column blocking: query a protected column → `ErrorResponse`.
- Read-only enforcement: `INSERT` through the proxy → rejection.
- Session expiry mid-connection: expire session → immediate disconnect.
- Credential revocation: revoke credential → active connection terminated.
- Concurrent connections and connection limit enforcement.

### Unit Tests

- PgWire message serialization/deserialization (each message type).
- Credential hashing and validation logic.
- Session heartbeat timer logic.

## Out of Scope

- Extended query protocol (Parse/Bind/Describe/Execute) — future enhancement.
- MySQL wire protocol — future enhancement, architecture supports it.
- MCP server for Claude Code — separate feature, can be layered on top.
- Credential rotation policies (max lifetime) — future enhancement.
