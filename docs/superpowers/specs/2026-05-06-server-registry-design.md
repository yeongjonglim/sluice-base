# SluiceBase Server Registry — Design

**Date:** 2026-05-06
**Status:** Proposed
**Sub-project:** 3 of 6 (Server Registry)
**Predecessor:** 2. Permissions (`2026-05-04-permissions-design.md`)
**Successor sub-projects:** 4. Schema browser, 5. Query workspace, 6. Approval workflow.

## 1. Purpose & scope

Permissions established who can do what, but there is nothing to do yet. The Server Registry is the first product-visible feature: admins with `server:manage` can register database servers (connection details + dual credentials), and those server records become the targets for schema browsing and query execution in sub-projects 4 and 5.

### In scope

- `Server` domain model in `Core` with dual credentials (read-only required, read-write optional).
- Decomposed connection fields: host, port, database, username, password — no raw connection string stored.
- Passwords encrypted at rest via ASP.NET DataProtection; never returned to any client.
- `IServerConnectionFactory` — the only place in the codebase that decrypts a password, producing a live connection string for internal use.
- CRUD API endpoints gated on `server:manage`.
- "Test connection" action per server (tests read and write credentials independently, returning pass/fail + error).
- `Kind` field + UI dropdown wired from day one (single value "postgres" for now — extensible without a migration).
- Mantine server management page at `/server`.
- Dev seeding via an Aspire custom resource command on the `metadata` resource — calls the dev-only encrypt endpoint to get proper ciphertext, then inserts Blue and Green dev servers directly into the metadata DB.
- A dev-only `POST /api/internal/dev/encrypt` endpoint: accepts plaintext, returns DataProtection ciphertext, excluded from OpenAPI, localhost-only, no auth.

### Out of scope (deferred)

- Per-server permission scoping — global grants remain through v1 (`server:manage` is global).
- Schema introspection, table/column browser (Sub-project 4).
- Query execution, results rendering, query log (Sub-project 5).
- A second engine kind (MySQL, SQL Server) — `Kind` column and dropdown exist but have one option.
- Connection string pooling or connection lifetime management.
- Server enable/disable soft-delete (field exists on the entity; no UI toggle in v1).
- CI configuration.

### Success criteria

After implementation, with Aspire running:

1. Alice (has `server:manage`) navigates to `/server` and sees the server table.
2. Alice can create a server with read-only credentials, and the table shows `HasReadPassword: true` — no password text is ever visible anywhere in the UI.
3. Alice can optionally add write credentials; the "⚠ no write" badge disappears.
4. The "Test" button fires for a saved server and shows per-credential pass/fail badges.
5. The Aspire dashboard "Seed Server Registry" command on `metadata-pg` is available; clicking it inserts Blue and Green server records idempotently.
6. Bob (no `server:manage`) cannot access `/server` (redirected to `/`) or any `/api/server` endpoint (403).
7. `dotnet test` and `npm run test` pass. `npm run test:e2e` passes the new `admin-server.spec.ts`.

## 2. Architectural decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Decomposed fields, not a raw connection string | Granular UI; validates individual fields; clear separation of which part is secret. |
| 2 | Dual credentials: read (required) + write (optional) | Enforces least-privilege at the DB level; servers with no write use case aren't forced to carry write credentials. |
| 3 | Passwords encrypted via ASP.NET DataProtection | Framework already wired (B1); key ring persisted to metadata DB; automatic key rotation; no custom crypto. |
| 4 | Passwords are write-only to users | Admins set or replace passwords but never read them back. `ServerResponse` carries `HasReadPassword: bool` / `HasWritePassword: bool`, never ciphertext or plaintext. |
| 5 | `IServerConnectionFactory` is the sole decryption point | One place to audit; one place to change if the encryption scheme evolves; sub-projects 4 and 5 inject this interface, never `IDataProtector` directly. |
| 6 | `Kind` field wired now, Postgres only | Extension point costs a column and a dropdown with one item. Avoids a migration when a second engine arrives in a future sub-project. |
| 7 | Test connection is a separate button, not coupled to save | A server may be temporarily unreachable; blocking save on reachability would prevent registering valid servers. |
| 8 | Dev seeding via Aspire custom resource command | Runs from the AppHost process; visible and repeatable via the Aspire dashboard; idempotent. |
| 9 | Dev-only encrypt endpoint for seed passwords | AppHost has no DataProtection infrastructure. A `POST /api/internal/dev/encrypt` endpoint exposes the protector to the seed command — dev-only, localhost-only, hidden from OpenAPI. No special handling leaks into `ServerConnectionFactory` or Core. |
| 10 | Per-server permission scoping deferred | Global `server:manage` is correct for v1; when per-server scoping lands, the migration is a nullable `ServerId` FK on `user_permission`. |
| 11 | Single PR | Feature is self-contained; no reason to split. |

## 3. Data model & schema

### 3.1 `ServerId` value object (in `Core`)

```csharp
// SluiceBase.Core/Servers/ServerId.cs
[ValueObject<Guid>]
public readonly partial struct ServerId;
```

Project-level Vogen defaults (already set in `AssemblyAttributes.cs`):
`Conversions.SystemTextJson | Conversions.TypeConverter`.

### 3.2 `Server` domain model (in `Core`)

```csharp
// SluiceBase.Core/Servers/Server.cs
public sealed class Server
{
    private Server() { }                                    // EF hydration

    public ServerId Id { get; private set; }
    public string Name { get; private set; } = "";         // display name, unique
    public string Kind { get; private set; } = "";         // "postgres" — extensible
    public string Host { get; private set; } = "";
    public int Port { get; private set; }
    public string Database { get; private set; } = "";

    // Read credential (required)
    public string ReadUsername { get; private set; } = "";
    public string EncryptedReadPassword { get; private set; } = ""; // opaque; never returned to clients

    // Write credential (optional — null = write queries disabled for this server)
    public string? WriteUsername { get; private set; }
    public string? EncryptedWritePassword { get; private set; }     // opaque; never returned to clients

    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool HasWriteCredential => WriteUsername is not null;

    public static Server Create(
        ServerId id, string name, string kind,
        string host, int port, string database,
        string readUsername, string encryptedReadPassword,
        string? writeUsername, string? encryptedWritePassword,
        DateTimeOffset at) => new()
    {
        Id = id, Name = name, Kind = kind,
        Host = host, Port = port, Database = database,
        ReadUsername = readUsername, EncryptedReadPassword = encryptedReadPassword,
        WriteUsername = writeUsername, EncryptedWritePassword = encryptedWritePassword,
        IsEnabled = true, CreatedAt = at, UpdatedAt = at,
    };

    public void Update(string name, string host, int port,
                       string database, string readUsername, DateTimeOffset at)
    {
        Name = name; Host = host; Port = port;
        Database = database; ReadUsername = readUsername; UpdatedAt = at;
    }

    public void ReplaceReadPassword(string encryptedPassword, DateTimeOffset at)
    {
        EncryptedReadPassword = encryptedPassword;
        UpdatedAt = at;
    }

    public void SetWriteCredential(string username, string encryptedPassword, DateTimeOffset at)
    {
        WriteUsername = username;
        EncryptedWritePassword = encryptedPassword;
        UpdatedAt = at;
    }

    public void ClearWriteCredential(DateTimeOffset at)
    {
        WriteUsername = null;
        EncryptedWritePassword = null;
        UpdatedAt = at;
    }

    public void SetEnabled(bool enabled, DateTimeOffset at)
    {
        IsEnabled = enabled;
        UpdatedAt = at;
    }
}
```

`EncryptedReadPassword` / `EncryptedWritePassword` are opaque strings to the domain model. No decryption method exists on the entity.

### 3.3 EF entity configuration (in `Api`)

```csharp
// SluiceBase.Api/Data/Configurations/ServerConfiguration.cs
internal sealed class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> b)
    {
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasVogenConversion();
        b.Property(s => s.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(s => s.Name).IsUnique();
        b.Property(s => s.Kind).HasMaxLength(32).IsRequired();
        b.Property(s => s.Host).HasMaxLength(255).IsRequired();
        b.Property(s => s.Database).HasMaxLength(255).IsRequired();
        b.Property(s => s.ReadUsername).HasMaxLength(128).IsRequired();
        b.Property(s => s.EncryptedReadPassword).HasMaxLength(4096).IsRequired();
        b.Property(s => s.WriteUsername).HasMaxLength(128);
        b.Property(s => s.EncryptedWritePassword).HasMaxLength(4096);
    }
}
```

### 3.4 `AppDbContext` addition

```csharp
public DbSet<Server> Servers => Set<Server>();
```

### 3.5 Migration

One new migration `AddServer`. Creates the `server` table with snake-case column names (existing `EFCore.NamingConventions`). No existing tables touched.

## 4. `IServerConnectionFactory`

The only place `IDataProtector.Unprotect` is called. Sub-projects 4 and 5 inject this interface — they never touch `IDataProtector` directly.

```csharp
// SluiceBase.Api/Servers/IServerConnectionFactory.cs
public enum CredentialKind { Read, Write }

public interface IServerConnectionFactory
{
    Task<string> GetConnectionStringAsync(ServerId serverId, CredentialKind kind, CancellationToken ct);
}
```

```csharp
// SluiceBase.Api/Servers/ServerConnectionFactory.cs
internal sealed class ServerConnectionFactory(
    AppDbContext db,
    IDataProtectionProvider dataProtection) : IServerConnectionFactory
{
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector("SluiceBase.ServerPassword");

    public async Task<string> GetConnectionStringAsync(
        ServerId serverId, CredentialKind kind, CancellationToken ct)
    {
        var server = await db.Servers
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == serverId, ct)
            ?? throw new InvalidOperationException($"Server {serverId} not found.");

        if (kind == CredentialKind.Write && !server.HasWriteCredential)
            throw new InvalidOperationException(
                $"Server '{server.Name}' has no write credential configured.");

        var encryptedPassword = kind == CredentialKind.Read
            ? server.EncryptedReadPassword
            : server.EncryptedWritePassword!;

        var username = kind == CredentialKind.Read
            ? server.ReadUsername
            : server.WriteUsername!;

        var password = _protector.Unprotect(encryptedPassword);

        return new NpgsqlConnectionStringBuilder
        {
            Host = server.Host,
            Port = server.Port,
            Database = server.Database,
            Username = username,
            Password = password,
        }.ConnectionString;
    }
}
```

Registered scoped: `services.AddScoped<IServerConnectionFactory, ServerConnectionFactory>()`. No special dev-mode branching — the factory always decrypts via DataProtection.

## 5. API endpoints

New file `Api/Endpoints/ServerEndpoints.cs`, added to `EndpointMapper`.

### 5.1 Endpoint table

| Endpoint | Method | Auth | Antiforgery | Notes |
|---|---|---|---|---|
| `/api/server` | GET | `server:manage` | No | |
| `/api/server` | POST | `server:manage` | Yes | |
| `/api/server/{id}` | PUT | `server:manage` | Yes | |
| `/api/server/{id}` | DELETE | `server:manage` | Yes | |
| `/api/server/{id}/test` | POST | `server:manage` | Yes | |
| `/api/internal/dev/encrypt` | POST | None | No | Dev-only; see §5.4 |

### 5.2 Request / response shapes

```csharp
// Never includes any password field
internal sealed record ServerResponse(
    ServerId Id,
    string Name,
    string Kind,
    string Host,
    int Port,
    string Database,
    string ReadUsername,
    bool HasReadPassword,
    string? WriteUsername,
    bool HasWritePassword,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record CreateServerRequest(
    string Name,
    string Kind,
    string Host,
    int Port,
    string Database,
    string ReadUsername,
    string ReadPassword,
    string? WriteUsername,
    string? WritePassword);  // both write fields required together or both omitted

internal sealed record UpdateServerRequest(
    string Name,
    string Host,
    int Port,
    string Database,
    string ReadUsername,
    string? ReadPassword,      // null = keep existing encrypted value
    string? WriteUsername,     // null = keep existing; "" = clear
    string? WritePassword,     // null = keep existing; "" = clear
    bool IsEnabled);

internal sealed record TestConnectionResponse(
    ConnectivityResult Read,
    ConnectivityResult? Write); // null if write credential not configured
```

### 5.3 Dev-only encrypt endpoint

Defined in a new `Api/Endpoints/DevEndpoints.cs`, registered in `Program.cs` only when `app.Environment.IsDevelopment()` — never added to `EndpointMapper`.

```csharp
// registered conditionally in Program.cs, not via EndpointMapper
if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/internal/dev/encrypt", (
        EncryptRequest req,
        IDataProtectionProvider dataProtection) =>
    {
        var protector = dataProtection.CreateProtector("SluiceBase.ServerPassword");
        var ciphertext = protector.Protect(req.Plaintext);
        return Results.Ok(new EncryptResponse(ciphertext));
    })
    .RequireHost("localhost")        // localhost-only; rejects non-local callers
    .ExcludeFromDescription();       // hidden from OpenAPI / openapi.json
}

internal sealed record EncryptRequest(string Plaintext);
internal sealed record EncryptResponse(string Ciphertext);
```

**Constraints:**
- `.RequireHost("localhost")` rejects any request not originating from localhost — no auth token needed.
- `.ExcludeFromDescription()` keeps it out of `openapi.json` and the generated TypeScript schema.
- Only registered when `IsDevelopment()` — absent in staging/production entirely.
- No antiforgery (called by the AppHost seed command, not a browser).

### 5.4 Behaviour notes

- **Create validation**: `WriteUsername` and `WritePassword` must be both non-null/non-empty or both absent. Mismatch returns `ValidationProblemDetails`.
- **Update password semantics**: `ReadPassword: null` preserves existing ciphertext. `WriteUsername: ""` + `WritePassword: ""` calls `server.ClearWriteCredential(...)`. `WriteUsername: "user"` + `WritePassword: "pass"` calls `server.SetWriteCredential(...)`.
- **Test connection**: decrypts internally via `IDataProtector`, calls `ITargetEngine.TestConnectionAsync` for each credential. Never exposes plaintext. Returns `TestConnectionResponse` — `Write` is `null` if the server has no write credential.
- **`IDataProtector` purpose**: `"SluiceBase.ServerPassword"` — same string used by `ServerConnectionFactory`.
- **`EndpointMapper` addition**: `ServerEndpoints.Map(app);` added alongside existing calls.

## 6. Frontend

### 6.1 Route & permission guard

New route `routes/_authed/server.tsx` → URL `/server`. Follows the same `beforeLoad` guard pattern:

```tsx
export const Route = createFileRoute("/_authed/server")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("server:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: ServerPage,
});
```

Server-side enforcement is on every `/api/server` endpoint. The client-side guard is UX only.

### 6.2 Navbar

`_authed.tsx` gains a "Servers" `NavLink` (with `IconServer` icon) above the existing "Permissions" link, visible only to users with `server:manage`.

### 6.3 Page layout

- Title "Server management" + "Add server" button (top right).
- Mantine `Table` columns:

| Name | Kind | Host | Database | Read user | Write user | Status | Actions |
|---|---|---|---|---|---|---|---|
| Blue | PostgreSQL | localhost | appdb | app_read | app_write | Enabled | Test / Edit / Delete |
| Green | PostgreSQL | localhost | appdb | app_read | — ⚠ | Enabled | Test / Edit / Delete |

  - Write user column shows "—" with an amber `Badge` labeled "No write" when `HasWritePassword` is false.
  - Neither password column exists — only usernames are displayed.

### 6.4 Test connection

- "Test" button per row fires `POST /api/server/{id}/test`.
- Shows a spinner while in-flight.
- On response, renders two inline badges below the row:
  - `Read: Connected` (teal) / `Read: Failed` (red)
  - `Write: Connected` (teal) / `Write: Failed` (red) — omitted if `Write` is `null`
- Error text surfaces in a Mantine `Tooltip` on the failed badge.
- Badges clear when the row's Edit modal opens.

### 6.5 Create / Edit modal

Mantine `Modal`:
- **Fields**: Name, Kind (`Select`, single option `{ value: "postgres", label: "PostgreSQL" }`), Host, Port (number input, default 5432), Database.
- **Read credentials section**: Username, Password (`type="password"`, placeholder "Enter password" on create / "Leave blank to keep existing" on edit).
- **Write credentials section** (collapsible, labeled "Optional — required for mutating queries"):
  - Username, Password (same placeholder pattern).
  - "Clear write credentials" checkbox on edit (sets both fields to `""`).
- **Validation**: write username and password must be both filled or both blank.

### 6.6 Permission labels (in `PERMISSION_LABELS` dict, already in `PermissionsAdminPage`)

`"server:manage"` already has `{ short: "Server", full: "Manage servers" }` — no change needed.

### 6.7 TanStack Query hooks (added to `api/hooks.ts`)

```ts
export function useServers() { ... }                // queryKey: ["server"]
export function useCreateServer() { ... }           // invalidates ["server"] on success
export function useUpdateServer() { ... }           // invalidates ["server"] on success
export function useDeleteServer() { ... }           // invalidates ["server"] on success
export function useTestConnection() { ... }         // no cache invalidation — transient result
```

All mutations show a Mantine `notifications.show(...)` on success and on error.

## 7. Dev seeding via Aspire custom resource command

The existing `DevSeedHook.cs` remains unused. Seeding is surfaced as an Aspire dashboard command instead.

### 7.1 AppHost wiring

In `AppHost/Program.cs`, after defining `blueDb` and `greenDb`:

```csharp
metadata.WithCommand(
    name: "seed-servers",
    displayName: "Seed Server Registry",
    executeCommand: context => SeedServersAsync(context, metadata, blueDb, greenDb),
    commandOptions: new CommandOptions
    {
        UpdateState = ctx => ctx.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
            ? ResourceCommandState.Enabled
            : ResourceCommandState.Disabled,
        IconName = "DatabaseArrowDown",
        IconVariant = IconVariant.Filled,
        Description = "Inserts Blue and Green dev servers into the registry. Idempotent.",
        ConfirmationMessage = "Seed dev server records into the registry?",
    });
```

### 7.2 Seed logic (`SeedServersAsync`)

Defined as a static method in `AppHost/DevServerSeed.cs`:

1. Calls `metadata.Resource.GetConnectionStringAsync()` to get the metadata DB admin connection string (used only to open a direct Npgsql connection to insert rows).
2. Calls `blueDb.Resource.GetConnectionStringAsync()` and `greenDb.Resource.GetConnectionStringAsync()` — these return the Aspire postgres admin connection strings, from which **only the host and port** are extracted via `NpgsqlConnectionStringBuilder`. The application-level usernames (`app_read`, `app_write`) and their passwords are hardcoded in the seed, matching the values in `seed/blue/01-init.sql` and `seed/green/01-init.sql`. The database name is always `appdb`.
3. Resolves the API base URL from the `api` resource (via `context.ServiceProvider` or captured builder reference) and calls `POST /api/internal/dev/encrypt` once per plaintext password to obtain proper DataProtection ciphertext. These are localhost-to-localhost HTTP calls; no auth token required.
4. Opens a direct `NpgsqlConnection` to the metadata DB.
5. For each seed record, executes:
   ```sql
   INSERT INTO server (id, name, kind, host, port, database,
       read_username, encrypted_read_password,
       write_username, encrypted_write_password,
       is_enabled, created_at, updated_at)
   VALUES (@id, @name, 'postgres', @host, @port, @db,
       @readUser, @encReadPass,
       @writeUser, @encWritePass,
       true, now(), now())
   ON CONFLICT (name) DO NOTHING;
   ```
6. Returns `CommandResults.Success()` or `CommandResults.Failure(ex.Message)`.

### 7.3 Seed records

Host and port come from the Aspire-injected blue/green connection strings. Usernames and passwords are hardcoded to match `seed/blue/01-init.sql` / `seed/green/01-init.sql`.

| Name | Host:Port | Read user | Enc. read pass | Write user | Enc. write pass |
|---|---|---|---|---|---|
| Blue | (Aspire-injected) | `app_read` | DataProtection ciphertext (via `/api/internal/dev/encrypt`) | `app_write` | DataProtection ciphertext (via `/api/internal/dev/encrypt`) |
| Green | (Aspire-injected) | `app_read` | DataProtection ciphertext (via `/api/internal/dev/encrypt`) | `null` | `null` |

Green is intentionally seeded without write credentials so the "⚠ No write" badge is visible from day one.

## 8. Tests

### 8.1 Backend integration tests (`ServerEndpointTests.cs`)

All tests use the existing `[Collection("Aspire")]` fixture and `KeycloakLoginHelper`:

| Test | Asserts |
|---|---|
| `List_ReturnsServers_WithoutPasswords` | No password field in any response; `HasReadPassword` / `HasWritePassword` are booleans |
| `Create_Happy_StoresEncryptedPassword` | POST creates server; GET returns `HasReadPassword: true`; raw DB row stores ciphertext (not plaintext) |
| `Create_MismatchedWriteCredentials_Returns400` | WriteUsername without WritePassword → `ValidationProblemDetails` |
| `Update_NullPassword_PreservesExisting` | PUT with `ReadPassword: null` leaves existing ciphertext unchanged |
| `Update_ClearsWriteCredential` | PUT with empty-string write pair → `HasWritePassword: false` |
| `Delete_RemovesServer` | Server absent from list after DELETE |
| `TestConnection_Read_Succeeds` | Against seeded Blue server → `Read.Ok: true` |
| `TestConnection_Write_Succeeds` | Blue server → `Write.Ok: true` |
| `TestConnection_Write_NullForReadOnly` | Green server → `Write: null` |
| `TestConnection_BadHost_Returns_OkFalse` | Bad host → `Read.Ok: false`, non-null `Read.Error` |
| `Bob_NoServerManage_Returns403` | Bob gets 403 on all `/api/server` endpoints |
| `Anonymous_Returns401` | Unauthenticated requests return 401 |

### 8.2 Frontend Vitest (`server-hooks.test.ts`)

- `useServers` renders list without any password fields in the response shape.
- `useCreateServer` mutation invalidates `["server"]` query key on success.
- `useUpdateServer` with `ReadPassword: null` sends `null` (not empty string) in request body.

### 8.3 Playwright E2E (`e2e/admin-server.spec.ts`)

Signed in as alice throughout:

1. Navigate to `/server` — expect page renders.
2. Open "Add server" modal, fill in connection details, submit.
3. Expect new row in table; `HasReadPassword` indicator visible; no password text in DOM.
4. Click "Test" — expect `Read: Connected` green badge.
5. Click "Edit" — change name, leave read password blank — expect name updated, `HasReadPassword` still true.
6. Delete the server — expect row gone.

### 8.4 Out of test scope

- Concurrent create/update races (single-instance v1).
- DataProtection key rotation (framework responsibility).
- The dev encrypt endpoint itself — it is a one-liner over `IDataProtector.Protect`; tested transitively by the seed command populating real ciphertext that `TestConnection` then successfully decrypts.

## 9. Packages, risks, acceptance

### 9.1 New packages

- No new NuGet packages (`Npgsql` already present; `Microsoft.AspNetCore.DataProtection` wired in B1).
- No new frontend packages.

### 9.2 DI registration changes

```csharp
// Program.cs
services.AddScoped<IServerConnectionFactory, ServerConnectionFactory>();
```

`ServerEndpoints.Map(app)` added to `EndpointMapper.MapAllEndpoints`.

### 9.3 Risks & open questions

- **`Kind` enum drift**: `Kind` is a plain `string` column, not a DB enum. If a second engine is added in a future sub-project, no migration is needed — only a new `ITargetEngine` registration and a new dropdown option.
- **Duplicate name conflict**: the unique index on `server.name` returns a DB exception on conflict. The create handler should catch this and return a `ValidationProblemDetails` with a clear message rather than a 500.
- **Antiforgery on test connection**: `POST /api/server/{id}/test` requires the antiforgery token. The frontend's `apiRequest` helper already sends `X-XSRF-TOKEN` on all non-GET requests.
- **`ServerConnectionFactory` in sub-projects 4/5**: the factory is scoped, loads the server from the DB on each call. If a handler calls it twice for the same server, that is two DB queries. A per-request cache (same pattern as `CurrentUserAccessor`) can be added when sub-projects land and measure the cost.

### 9.4 Acceptance criteria

- `dotnet build SluiceBase.slnx` clean (warnings-as-errors).
- `dotnet test SluiceBase.slnx` passes (all prior tests + new server tests).
- `npm run build` clean (TS strict + ESLint).
- `npm run test` passes (all prior tests + new server hook tests).
- `aspire run`:
  - Aspire dashboard shows "Seed Server Registry" command on `metadata-pg`.
  - Running the command twice produces two rows, not four (idempotent).
  - Alice navigates to `/server`, sees Blue and Green rows, Green shows "⚠ No write".
  - "Test" on Blue shows `Read: Connected` and `Write: Connected`.
  - "Test" on Green shows `Read: Connected` and no Write badge.
  - Bob navigating to `/server` is redirected to `/`.
- `npm run test:e2e` passes `admin-server.spec.ts`.

## 10. References

- Foundations design: `docs/superpowers/specs/2026-05-03-foundations-design.md`
- Permissions design: `docs/superpowers/specs/2026-05-04-permissions-design.md`
- `ITargetEngine`: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- AppHost: `src/AppHost/Program.cs`
- Existing `DevSeedHook.cs`: `src/AppHost/DevSeedHook.cs` (unused — keep on disk)
- Aspire custom resource commands: https://aspire.dev/fundamentals/custom-resource-commands/
