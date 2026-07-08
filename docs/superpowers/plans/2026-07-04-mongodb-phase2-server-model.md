# MongoDB Phase 2 — Server Model & Connection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an operator register a MongoDB server (Standard or SRV, with authSource / replicaSet / TLS) and test its connectivity — end to end from the add-server form to a live `ping`.

**Architecture:** Add a `MongoTargetEngine` (Kind `"mongodb"`) that builds `mongodb://` / `mongodb+srv://` connection strings and pings via the MongoDB .NET driver; grow `ConnectionParameters` and the `Server` entity with Mongo connection options; flow those options through `ServerConnectionFactory`, the server API, and the frontend form. Read/schema/write engine methods remain `NotSupportedException` until Phases 3–4.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core (Npgsql metadata store), MongoDB.Driver, Vogen, React/TS + Mantine + generated `schema.ts`, Aspire AppHost, xUnit v3.

## Global Constraints

- Develop on branch `feat/mongodb-phase2-server-model` (already created off the Phase 1 branch). Never commit to `main`.
- Commit messages: single imperative subject line, no `feat:` prefix, no body.
- This repo treats analyzer warnings (e.g. CA1859) as BUILD ERRORS. Verify with real builds; never use `--no-build` when confirming test results. Confirm `0 Warning(s), 0 Error(s)`.
- Unit tests run locally: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj` and `dotnet test tests/SluiceBase.Core.Tests/SluiceBase.Core.Tests.csproj`. Integration tests need an Aspire stack not available in automated sessions — verify those paths with `dotnet build` and rely on CI.
- Never manually edit EF Core migration files. Generate them with `dotnet ef`. Analyzer warnings in migrations are already suppressed via the `[**/Migrations/**.{cs,vb}]` section in `.editorconfig`.
- Abstract database-specific operations behind the engine interface — MongoDB.Driver types must not appear outside `MongoTargetEngine` (mirrors the Npgsql-confinement rule).
- Use `Array<T>` not `T[]` in TypeScript (ESLint `@typescript-eslint/array-type`).
- Preserve existing comments unless factually wrong.
- Backward compatibility: existing PostgreSQL servers must be unaffected. New `Server` fields and `ConnectionParameters` options default so that a `"postgres"` server behaves exactly as before.
- After any change to API request/response shapes, regenerate `src/SluiceBase.Api/openapi.json` and `src/frontend/src/api/schema.ts` (CI gates their consistency).

---

## File Structure

**Created:**
- `src/SluiceBase.Core/Servers/ConnectionMode.cs` — `Standard | Srv` enum.
- `src/SluiceBase.Api/Targets/MongoTargetEngine.cs` — the Mongo engine.
- `tests/SluiceBase.Api.Tests/MongoTargetEngineTests.cs` — connection-string + Kind + NotSupported tests.
- `tests/IntegrationTests/MongoTestConnectionTests.cs` — live `TestConnection` against a container (CI).
- One EF migration under `src/SluiceBase.Api/Data/Migrations/` (generated).

**Modified:**
- `src/SluiceBase.Core/Targets/ConnectionParameters.cs` — add Mongo option fields.
- `src/SluiceBase.Core/Servers/Server.cs` — add fields + grow `Create`/`Update`.
- `src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs` — map new fields.
- `src/SluiceBase.Api/Program.cs` — register `MongoTargetEngine`.
- `src/SluiceBase.Api/SluiceBase.Api.csproj` — add MongoDB.Driver.
- `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs` — pass Mongo options into `ConnectionParameters`.
- `src/SluiceBase.Api/Endpoints/ServerEndpoints.cs` — grow requests/response + handlers.
- `src/SluiceBase.Api/openapi.json` + `src/frontend/src/api/schema.ts` — regenerated.
- `src/frontend/src/routes/_authed/server.tsx` — kind selector + conditional Mongo fields.
- `src/AppHost/Program.cs` — add a MongoDB container resource.

---

## Task 1: MongoTargetEngine — connection strings & connectivity

**Files:**
- Create: `src/SluiceBase.Core/Servers/ConnectionMode.cs`
- Modify: `src/SluiceBase.Core/Targets/ConnectionParameters.cs`
- Create: `src/SluiceBase.Api/Targets/MongoTargetEngine.cs`
- Modify: `src/SluiceBase.Api/SluiceBase.Api.csproj` (add MongoDB.Driver)
- Modify: `src/SluiceBase.Api/Program.cs` (DI registration)
- Test: `tests/SluiceBase.Api.Tests/MongoTargetEngineTests.cs`

**Interfaces:**
- Consumes: `ITargetEngine` (Phase 1), `ITargetEngineRegistry` DI (Phase 1).
- Produces:
  - `enum ConnectionMode { Standard, Srv }` in `SluiceBase.Core.Servers`.
  - `ConnectionParameters(string Host, int Port, string Database, string Username, string Password, ConnectionMode Mode = ConnectionMode.Standard, string? AuthSource = null, string? ReplicaSet = null, bool UseTls = false)`.
  - `MongoTargetEngine : ITargetEngine` with `Kind => "mongodb"`, a `BuildConnectionString` that emits `mongodb://host:port/db?...` (Standard) or `mongodb+srv://host/db?...` (SRV), and a `TestConnectionAsync` that pings. `GetSchemaAsync`, `ExportSchemaDdlAsync`, `ExecuteQueryAsync`, `ExecuteUpdateAsync` throw `NotSupportedException`.

- [ ] **Step 1: Add the MongoDB.Driver package**

Run: `dotnet add src/SluiceBase.Api/SluiceBase.Api.csproj package MongoDB.Driver`
This pins the current stable (3.x) version. Confirm the added `<PackageReference Include="MongoDB.Driver" ... />` line appears in the csproj and record the resolved version in your report.

- [ ] **Step 2: Add the ConnectionMode enum**

Create `src/SluiceBase.Core/Servers/ConnectionMode.cs`:

```csharp
namespace SluiceBase.Core.Servers;

// How to reach a MongoDB deployment. Standard = a single host:port (or the default
// scheme). Srv = a mongodb+srv DNS seedlist name (Atlas / managed clusters); the port
// is not used in this mode.
public enum ConnectionMode
{
    Standard,
    Srv
}
```

- [ ] **Step 3: Grow ConnectionParameters (additively)**

Replace the body of `src/SluiceBase.Core/Targets/ConnectionParameters.cs` with:

```csharp
using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Targets;

// Engine-neutral inputs for building a connection string. The first five fields are
// common to all engines; the trailing options are Mongo-specific and default so that
// PostgreSQL callers are unaffected.
public sealed record ConnectionParameters(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    ConnectionMode Mode = ConnectionMode.Standard,
    string? AuthSource = null,
    string? ReplicaSet = null,
    bool UseTls = false);
```

- [ ] **Step 4: Write the failing tests**

Create `tests/SluiceBase.Api.Tests/MongoTargetEngineTests.cs`:

```csharp
using MongoDB.Driver;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

public class MongoTargetEngineTests
{
    private static readonly MongoTargetEngine Engine = new();

    [Fact]
    public void Kind_IsMongodb()
    {
        Assert.Equal("mongodb", Engine.Kind);
    }

    [Fact]
    public void BuildConnectionString_Standard_UsesHostAndPort()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "db.example.com", 27018, "shop", "reader", "s3cret",
            ConnectionMode.Standard));

        var url = MongoUrl.Create(cs);
        Assert.Equal(ConnectionStringScheme.MongoDB, url.Scheme);
        Assert.Equal("db.example.com", url.Server.Host);
        Assert.Equal(27018, url.Server.Port);
        Assert.Equal("shop", url.DatabaseName);
        Assert.Equal("reader", url.Username);
        Assert.Equal("s3cret", url.Password);
    }

    [Fact]
    public void BuildConnectionString_Srv_UsesSrvSchemeAndNoPort()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "cluster0.ab12.mongodb.net", 27017, "shop", "reader", "s3cret",
            ConnectionMode.Srv));

        Assert.StartsWith("mongodb+srv://", cs);
        var url = MongoUrl.Create(cs);
        Assert.Equal(ConnectionStringScheme.MongoDBPlusSrv, url.Scheme);
        Assert.Equal("cluster0.ab12.mongodb.net", url.Server.Host);
        Assert.Equal("shop", url.DatabaseName);
    }

    [Fact]
    public void BuildConnectionString_IncludesOptionsWhenSet()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "h", 27017, "shop", "u", "p",
            ConnectionMode.Standard, AuthSource: "admin", ReplicaSet: "rs0", UseTls: true));

        var url = MongoUrl.Create(cs);
        Assert.Equal("admin", url.AuthenticationSource);
        Assert.Equal("rs0", url.ReplicaSetName);
        Assert.True(url.UseTls);
    }

    [Fact]
    public void BuildConnectionString_EscapesCredentials()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "h", 27017, "shop", "user name", "p@ss:word/!",
            ConnectionMode.Standard));

        // MongoUrl.Create round-trips the percent-encoded credentials back to originals.
        var url = MongoUrl.Create(cs);
        Assert.Equal("user name", url.Username);
        Assert.Equal("p@ss:word/!", url.Password);
    }

    [Fact]
    public async Task GetSchemaAsync_Throws_NotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Engine.GetSchemaAsync("mongodb://h/db", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteQueryAsync_Throws_NotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Engine.ExecuteQueryAsync("mongodb://h/db", "{}", CancellationToken.None));
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: FAIL — compile error, `MongoTargetEngine` does not exist.

- [ ] **Step 6: Implement MongoTargetEngine**

Create `src/SluiceBase.Api/Targets/MongoTargetEngine.cs`:

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Targets;

internal sealed class MongoTargetEngine : ITargetEngine
{
    public string Kind => "mongodb";

    public string BuildConnectionString(ConnectionParameters p)
    {
        var scheme = p.Mode == ConnectionMode.Srv ? "mongodb+srv" : "mongodb";
        var user = Uri.EscapeDataString(p.Username);
        var pass = Uri.EscapeDataString(p.Password);
        // SRV derives the host list (and ports) from DNS, so the port is not emitted.
        var hostPart = p.Mode == ConnectionMode.Srv ? p.Host : $"{p.Host}:{p.Port}";
        var db = Uri.EscapeDataString(p.Database);

        var options = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.AuthSource))
        {
            options.Add($"authSource={Uri.EscapeDataString(p.AuthSource)}");
        }

        if (!string.IsNullOrWhiteSpace(p.ReplicaSet))
        {
            options.Add($"replicaSet={Uri.EscapeDataString(p.ReplicaSet)}");
        }

        if (p.UseTls)
        {
            options.Add("tls=true");
        }

        var query = options.Count > 0 ? "?" + string.Join("&", options) : string.Empty;
        return $"{scheme}://{user}:{pass}@{hostPart}/{db}{query}";
    }

    public async Task<ConnectivityResult> TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            var client = new MongoClient(settings);
            await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct)
                .ConfigureAwait(false);
            return new ConnectivityResult(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectivityResult(false, ex.Message);
        }
    }

    public Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct) =>
        throw new NotSupportedException("Schema introspection is not yet supported for MongoDB.");

    public Task<string> ExportSchemaDdlAsync(string connectionString, CancellationToken ct) =>
        throw new NotSupportedException("DDL export is not supported for MongoDB.");

    public Task<QueryData> ExecuteQueryAsync(string connectionString, string sql, CancellationToken ct) =>
        throw new NotSupportedException("Query execution is not yet supported for MongoDB.");

    public Task<int> ExecuteUpdateAsync(string connectionString, string sql, CancellationToken ct) =>
        throw new NotSupportedException("Writes are not supported for MongoDB.");
}
```

- [ ] **Step 7: Register the engine in DI**

In `src/SluiceBase.Api/Program.cs`, directly after the existing
`builder.Services.AddSingleton<ITargetEngine, PostgresTargetEngine>();` line add:

```csharp
builder.Services.AddSingleton<ITargetEngine, MongoTargetEngine>();
```

(The `TargetEngineRegistry` already consumes `IEnumerable<ITargetEngine>`, so it will now resolve both `"postgres"` and `"mongodb"`.)

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS (all Mongo engine tests plus the existing suite). Confirm build shows 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add src/SluiceBase.Core/Servers/ConnectionMode.cs \
        src/SluiceBase.Core/Targets/ConnectionParameters.cs \
        src/SluiceBase.Api/Targets/MongoTargetEngine.cs \
        src/SluiceBase.Api/SluiceBase.Api.csproj \
        src/SluiceBase.Api/Program.cs \
        tests/SluiceBase.Api.Tests/MongoTargetEngineTests.cs
git commit -m "Add MongoDB target engine with connection string building and ping"
```

---

## Task 2: Grow the Server entity, EF mapping, and migration

**Files:**
- Modify: `src/SluiceBase.Core/Servers/Server.cs`
- Modify: `src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs`
- Create: EF migration under `src/SluiceBase.Api/Data/Migrations/` (generated)
- Test: `tests/SluiceBase.Core.Tests/ServerTests.cs`

**Interfaces:**
- Consumes: `ConnectionMode` (Task 1).
- Produces:
  - `Server` gains `ConnectionMode ConnectionMode`, `string? AuthSource`, `string? ReplicaSet`, `bool UseTls` (all with private setters).
  - `Server.Create(string name, string kind, string host, int port, DateTimeOffset at, ConnectionMode connectionMode = ConnectionMode.Standard, string? authSource = null, string? replicaSet = null, bool useTls = false)`.
  - `Server.Update(string name, string host, int port, string kind, bool isDisabled, DateTimeOffset at, ConnectionMode connectionMode = ConnectionMode.Standard, string? authSource = null, string? replicaSet = null, bool useTls = false)`.

- [ ] **Step 1: Write the failing test**

Create `tests/SluiceBase.Core.Tests/ServerTests.cs`:

```csharp
using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Tests;

public class ServerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_DefaultsToStandardModeWithNoMongoOptions()
    {
        var server = Server.Create("pg", "postgres", "localhost", 5432, Now);

        Assert.Equal(ConnectionMode.Standard, server.ConnectionMode);
        Assert.Null(server.AuthSource);
        Assert.Null(server.ReplicaSet);
        Assert.False(server.UseTls);
    }

    [Fact]
    public void Create_SetsMongoOptionsWhenProvided()
    {
        var server = Server.Create("mongo", "mongodb", "cluster0.mongodb.net", 27017, Now,
            ConnectionMode.Srv, authSource: "admin", replicaSet: "rs0", useTls: true);

        Assert.Equal(ConnectionMode.Srv, server.ConnectionMode);
        Assert.Equal("admin", server.AuthSource);
        Assert.Equal("rs0", server.ReplicaSet);
        Assert.True(server.UseTls);
    }

    [Fact]
    public void Update_OverwritesMongoOptions()
    {
        var server = Server.Create("mongo", "mongodb", "h", 27017, Now,
            ConnectionMode.Srv, authSource: "admin");

        server.Update("mongo", "h2", 27017, "mongodb", isDisabled: false, Now,
            ConnectionMode.Standard, authSource: null, replicaSet: "rs1", useTls: true);

        Assert.Equal(ConnectionMode.Standard, server.ConnectionMode);
        Assert.Null(server.AuthSource);
        Assert.Equal("rs1", server.ReplicaSet);
        Assert.True(server.UseTls);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SluiceBase.Core.Tests/SluiceBase.Core.Tests.csproj`
Expected: FAIL — `Server` has no `ConnectionMode` property; `Create`/`Update` lack the new parameters.

- [ ] **Step 3: Add the properties to Server**

In `src/SluiceBase.Core/Servers/Server.cs`, add these properties directly after the existing `public int Port { get; private set; }` line:

```csharp
    public ConnectionMode ConnectionMode { get; private set; }
    public string? AuthSource { get; private set; }
    public string? ReplicaSet { get; private set; }
    public bool UseTls { get; private set; }
```

- [ ] **Step 4: Grow Server.Create**

Replace the existing `Server.Create` method with:

```csharp
    public static Server Create(
        string name,
        string kind,
        string host,
        int port,
        DateTimeOffset at,
        ConnectionMode connectionMode = ConnectionMode.Standard,
        string? authSource = null,
        string? replicaSet = null,
        bool useTls = false) =>
        new()
        {
            Id = ServerId.FromNewVersion7Guid(),
            Name = name,
            Kind = kind,
            Host = host,
            Port = port,
            ConnectionMode = connectionMode,
            AuthSource = authSource,
            ReplicaSet = replicaSet,
            UseTls = useTls,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };
```

- [ ] **Step 5: Grow Server.Update**

Replace the existing `Server.Update` method with:

```csharp
    public void Update(
        string name,
        string host,
        int port,
        string kind,
        bool isDisabled,
        DateTimeOffset at,
        ConnectionMode connectionMode = ConnectionMode.Standard,
        string? authSource = null,
        string? replicaSet = null,
        bool useTls = false)
    {
        Name = name;
        Host = host;
        Port = port;
        Kind = kind;
        ConnectionMode = connectionMode;
        AuthSource = authSource;
        ReplicaSet = replicaSet;
        UseTls = useTls;
        IsDisabled = isDisabled;
        UpdatedAt = at;
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/SluiceBase.Core.Tests/SluiceBase.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Map the new columns in ServerConfiguration**

In `src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs`, add these lines at the end of the `Configure` method (after the `Host` mapping):

```csharp
        builder.Property(s => s.ConnectionMode)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(s => s.AuthSource).HasMaxLength(128);
        builder.Property(s => s.ReplicaSet).HasMaxLength(128);
        builder.Property(s => s.UseTls).IsRequired();
```

- [ ] **Step 8: Generate the EF migration**

Run (from repo root):
`dotnet ef migrations add AddMongoServerFields --project src/SluiceBase.Api/SluiceBase.Api.csproj --startup-project src/SluiceBase.Api/SluiceBase.Api.csproj`

Do NOT hand-edit the generated files. Confirm the migration adds `connection_mode` (with a non-null default — EF will default the string; existing rows get `'Standard'`), `auth_source`, `replica_set`, and `use_tls` columns to the `server` table. If EF cannot reach a database to scaffold, ensure the metadata connection is available per the repo's normal migration workflow, then re-run.

- [ ] **Step 9: Verify build**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```bash
git add src/SluiceBase.Core/Servers/Server.cs \
        src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs \
        src/SluiceBase.Api/Data/Migrations/ \
        tests/SluiceBase.Core.Tests/ServerTests.cs
git commit -m "Add MongoDB connection fields to the server entity and schema"
```

---

## Task 3: Server API and connection factory wiring

**Files:**
- Modify: `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs`
- Modify: `src/SluiceBase.Api/Endpoints/ServerEndpoints.cs`
- Regenerate: `src/SluiceBase.Api/openapi.json`, `src/frontend/src/api/schema.ts`

**Interfaces:**
- Consumes: grown `ConnectionParameters` (Task 1), grown `Server.Create`/`Update` (Task 2), `Server.ConnectionMode/AuthSource/ReplicaSet/UseTls` (Task 2).
- Produces:
  - `CreateServerRequest(string Name, string Kind, string Host, int Port, ConnectionMode ConnectionMode = ConnectionMode.Standard, string? AuthSource = null, string? ReplicaSet = null, bool UseTls = false)`.
  - `UpdateServerRequest(string Name, string Host, int Port, string Kind, bool IsDisabled = false, ConnectionMode ConnectionMode = ConnectionMode.Standard, string? AuthSource = null, string? ReplicaSet = null, bool UseTls = false)`.
  - `ServerResponse` gains `ConnectionMode ConnectionMode, string? AuthSource, string? ReplicaSet, bool UseTls`.

- [ ] **Step 1: Pass Mongo options through the connection factory**

In `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs`, replace the `return engine.BuildConnectionString(new ConnectionParameters(...));` block with:

```csharp
        var engine = engineRegistry.Resolve(database.Server!.Kind);

        return engine.BuildConnectionString(new ConnectionParameters(
            database.Server.Host,
            database.Server.Port,
            database.DatabaseName,
            credential.Username,
            password,
            database.Server.ConnectionMode,
            database.Server.AuthSource,
            database.Server.ReplicaSet,
            database.Server.UseTls));
```

- [ ] **Step 2: Grow the request records**

In `src/SluiceBase.Api/Endpoints/ServerEndpoints.cs`, add `using SluiceBase.Core.Servers;` if not present, then replace the `CreateServerRequest` and `UpdateServerRequest` records with:

```csharp
    public sealed record CreateServerRequest(
        string Name,
        string Kind,
        string Host,
        int Port,
        ConnectionMode ConnectionMode = ConnectionMode.Standard,
        string? AuthSource = null,
        string? ReplicaSet = null,
        bool UseTls = false);

    public sealed record UpdateServerRequest(
        string Name,
        string Host,
        int Port,
        string Kind,
        bool IsDisabled = false,
        ConnectionMode ConnectionMode = ConnectionMode.Standard,
        string? AuthSource = null,
        string? ReplicaSet = null,
        bool UseTls = false);
```

- [ ] **Step 3: Grow ServerResponse and pass the fields through the handlers**

In the same file:

Replace the `ServerResponse` record's parameter list to add the four fields directly after `int Port,`:

```csharp
        int Port,
        ConnectionMode ConnectionMode,
        string? AuthSource,
        string? ReplicaSet,
        bool UseTls,
```

Update `ToResponse` to pass them (directly after `s.Port,`):

```csharp
        new(s.Id, s.Name, s.Kind, s.Host, s.Port,
            s.ConnectionMode, s.AuthSource, s.ReplicaSet, s.UseTls,
            s.IsDisabled,
```

In `CreateServer`, replace the `Server.Create(...)` call:

```csharp
        var server = Server.Create(req.Name, req.Kind, req.Host, req.Port, clock.GetUtcNow(),
            req.ConnectionMode, req.AuthSource, req.ReplicaSet, req.UseTls);
```

In `UpdateServer`, replace the `server.Update(...)` call:

```csharp
        server.Update(req.Name, req.Host, req.Port, req.Kind, req.IsDisabled, clock.GetUtcNow(),
            req.ConnectionMode, req.AuthSource, req.ReplicaSet, req.UseTls);
```

- [ ] **Step 4: Build and regenerate the API contract**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj` — expect 0 warnings/0 errors (a Debug build regenerates `src/SluiceBase.Api/openapi.json`).

Then regenerate the frontend types from the updated `openapi.json`. From `src/frontend`, run: `npm run gen:api` (this is `openapi-typescript ../SluiceBase.Api/openapi.json -o src/api/schema.ts`). Confirm `git diff --stat` shows both `src/SluiceBase.Api/openapi.json` and `src/frontend/src/api/schema.ts` changed, and that `schema.ts` now carries `connectionMode`, `authSource`, `replicaSet`, and `useTls` on the server request/response schemas.

- [ ] **Step 5: Run unit tests**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS. (Endpoint behavior is covered by CI integration tests; do not run `tests/IntegrationTests` locally.)

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Servers/ServerConnectionFactory.cs \
        src/SluiceBase.Api/Endpoints/ServerEndpoints.cs \
        src/SluiceBase.Api/openapi.json \
        src/frontend/src/api/schema.ts
git commit -m "Expose MongoDB connection fields through the server API and factory"
```

---

## Task 4: Add-server form — kind selector and Mongo fields

**Files:**
- Modify: `src/frontend/src/routes/_authed/server.tsx`
- Test: the existing frontend test suite (add/adjust a test if the file has a server-form test; otherwise verify via typecheck + lint + the component's manual render path)

**Interfaces:**
- Consumes: regenerated `CreateServerRequest` / `UpdateServerRequest` types from `schema.ts` (Task 3), which now include `connectionMode`, `authSource`, `replicaSet`, `useTls`.

- [ ] **Step 1: Make the Kind selector real and add Mongo state**

In `src/frontend/src/routes/_authed/server.tsx`, in the create/edit form component, replace the hardcoded kind state:

```tsx
  const [kind] = useState("postgres");
```

with editable kind plus Mongo option state (seed from `server` when editing):

```tsx
  const [kind, setKind] = useState(server?.kind ?? "postgres");
  const [connectionMode, setConnectionMode] = useState(
    server?.connectionMode ?? "Standard",
  );
  const [authSource, setAuthSource] = useState(server?.authSource ?? "");
  const [replicaSet, setReplicaSet] = useState(server?.replicaSet ?? "");
  const [useTls, setUseTls] = useState(server?.useTls ?? false);
```

- [ ] **Step 2: Make the Kind Select choose between engines**

Replace the read-only Kind `Select` block:

```tsx
        <Select
          label="Kind"
          required
          value={kind}
          data={[{ value: "postgres", label: "PostgreSQL" }]}
          readOnly
        />
```

with:

```tsx
        <Select
          label="Kind"
          required
          value={kind}
          onChange={(v) => setKind(v ?? "postgres")}
          data={[
            { value: "postgres", label: "PostgreSQL" },
            { value: "mongodb", label: "MongoDB" },
          ]}
        />
```

- [ ] **Step 3: Add conditional Mongo fields**

Directly after the `Host` / `Port` `<Group grow>` block, add MongoDB-only controls. For SRV, the port is unused, so disable it when `kind === "mongodb" && connectionMode === "Srv"` — update the `Port` `NumberInput` to include `disabled={kind === "mongodb" && connectionMode === "Srv"}`. Then insert:

```tsx
        {kind === "mongodb" && (
          <>
            <Select
              label="Connection mode"
              value={connectionMode}
              onChange={(v) => setConnectionMode(v ?? "Standard")}
              data={[
                { value: "Standard", label: "Standard (host:port)" },
                { value: "Srv", label: "SRV (mongodb+srv DNS name)" },
              ]}
            />
            <TextInput
              label="Auth source"
              placeholder="admin"
              value={authSource}
              onChange={(e) => setAuthSource(e.currentTarget.value)}
            />
            <TextInput
              label="Replica set"
              value={replicaSet}
              onChange={(e) => setReplicaSet(e.currentTarget.value)}
            />
            <Switch
              label="Use TLS"
              checked={useTls}
              onChange={(e) => setUseTls(e.currentTarget.checked)}
            />
          </>
        )}
```

Add `Switch` to the existing `@mantine/core` import if it is not already imported.

- [ ] **Step 4: Include the Mongo fields in submit payloads**

In the form's `handleSubmit`, build a shared options object and include it in both branches. Replace the update-branch body and the create call so they carry the Mongo fields. The mongo options are only meaningful for `kind === "mongodb"`, but sending the defaults for postgres is harmless (the backend defaults match). Use:

```tsx
    const mongoOptions =
      kind === "mongodb"
        ? {
            connectionMode,
            authSource: authSource || null,
            replicaSet: replicaSet || null,
            useTls,
          }
        : {};

    if (server) {
      await updateServer.mutateAsync({
        id: server.id,
        body: { name, host, port, kind, isDisabled: server.isDisabled, ...mongoOptions },
      });
    } else {
      await createServer.mutateAsync({ name, kind, host, port, ...mongoOptions });
    }
```

(Adjust to match the exact existing `mutateAsync` argument shapes in this file — keep the existing keys, only add `kind` as editable and spread `mongoOptions`.)

- [ ] **Step 5: Typecheck, lint, and test**

From `src/frontend`, run these three checks (from `package.json` scripts): `npx tsc -b` (typecheck), `npm run lint` (ESLint), and `npm test` (`vitest run --coverage`). Expected: all pass; no `array-type` violations; the generated `schema.ts` types accept the new fields. Paste the real results into your report.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/routes/_authed/server.tsx
git commit -m "Add MongoDB kind and connection options to the add-server form"
```

---

## Task 5: Aspire MongoDB resource and live TestConnection integration test

**Files:**
- Modify: `src/AppHost/Program.cs` (the AppHost entry file; it contains `builder.AddPostgres("main-pg")`)
- Create: `tests/IntegrationTests/MongoTestConnectionTests.cs`

**Interfaces:**
- Consumes: `MongoTargetEngine.TestConnectionAsync` (Task 1); the Aspire MongoDB hosting integration.

- [ ] **Step 1: Add the MongoDB hosting integration to AppHost**

Add the Aspire MongoDB hosting package to the AppHost project:
`dotnet add src/AppHost/AppHost.csproj package Aspire.Hosting.MongoDB`
Record the resolved version in your report.

- [ ] **Step 2: Register a MongoDB container resource**

In `src/AppHost/Program.cs`, near the other target databases (after the `target-green-pg` block), add a MongoDB target so integration tests and later phases have a live instance:

```csharp
var targetMongo = builder.AddMongoDB("target-mongo");
var mongoAppDb = targetMongo.AddDatabase("mongo-appdb");
```

Do NOT add a `.WithReference(...)` to the `api` project: the existing Postgres targets (`blue-appdb`, `green-appdb`) are also unreferenced by `api` — the integration fixture reaches them by resource name via `GetConnectionStringAsync`, and Mongo follows the same pattern. `mongoAppDb` is declared to register the resource in the AppHost model; a later phase may reference it. Silence the "unused variable" analyzer only if it fires — prefer keeping the named resource. Then verify the AppHost builds: `dotnet build src/AppHost/AppHost.csproj` (0 warnings/0 errors).

- [ ] **Step 3: Write the live TestConnection integration test**

The integration suite uses an assembly-wide Aspire fixture (`[assembly: AssemblyFixture(typeof(SluiceBaseStackFactory))]` in `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs`), injected into every test class via its primary constructor. Test classes get a live connection string for an AppHost resource through `factory.InitialisedApp.GetConnectionStringAsync("<resource-name>", TestContext.Current.CancellationToken)`. Mirror `TargetEngineTests.cs` exactly — do NOT use Testcontainers directly and do NOT add any `IClassFixture`/`[Collection]` attribute.

Create `tests/IntegrationTests/MongoTestConnectionTests.cs`:

```csharp
using IntegrationTests.Supports;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class MongoTestConnectionTests(SluiceBaseStackFactory factory)
{
    private readonly MongoTargetEngine _engine = new();

    [Fact]
    public async Task Mongo_TestConnection_Succeeds()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("mongo-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var result = await _engine.TestConnectionAsync(
            connectionString!, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("mongodb", _engine.Kind);
    }

    [Fact]
    public async Task Mongo_TestConnection_Fails_OnBadConnString()
    {
        const string broken =
            "mongodb://u:p@does-not-exist.invalid:65000/appdb?serverSelectionTimeoutMS=2000";

        var result = await _engine.TestConnectionAsync(
            broken, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
```

(The `"mongo-appdb"` resource name must match the database added in Step 2. The Aspire-provided connection string is already a `mongodb://` URL, so it feeds `TestConnectionAsync` directly.)

- [ ] **Step 4: Build the integration test project**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj`
Expected: Build succeeded, 0 warnings/0 errors. Do NOT run the integration suite locally (needs the Aspire/Docker stack); CI runs it. State this in your report.

- [ ] **Step 5: Commit**

```bash
git add src/AppHost/ tests/IntegrationTests/MongoTestConnectionTests.cs
git commit -m "Add MongoDB Aspire resource and live test-connection integration test"
```

---

## Done criteria

- A `MongoTargetEngine` (Kind `"mongodb"`) builds valid `mongodb://` / `mongodb+srv://` connection strings (Standard and SRV, with authSource/replicaSet/TLS) and pings a live server; read/schema/write methods throw `NotSupportedException`. MongoDB.Driver types appear only inside that engine.
- The `Server` entity, EF schema (new migration), server API (requests/response), connection factory, and add-server form all carry `ConnectionMode` + `AuthSource` + `ReplicaSet` + `UseTls`.
- `openapi.json` and `schema.ts` are regenerated and consistent.
- An operator can register a MongoDB server from the UI and use the existing test-connection action to verify it.
- PostgreSQL servers are unaffected (all new fields default to Standard/null/false).
- All unit tests green; `dotnet build` clean across Api, Core, AppHost, and IntegrationTests; integration tests verified by build and left to CI.
- Deferred to Phase 3: schema introspection (sampling → adapted `SchemaTree`) and the schema browser rendering for MongoDB. Deferred to Phase 4: Mongo read queries + MCP dialect generalization.
