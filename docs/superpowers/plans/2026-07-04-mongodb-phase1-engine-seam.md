# MongoDB Phase 1 — Engine Seam Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the single hard-wired `ITargetEngine` into a kind-resolved registry and move connection-string building behind the engine seam, so a second engine (MongoDB) can be added later — with zero behavior change to PostgreSQL.

**Architecture:** Introduce `ITargetEngineRegistry` that resolves an `ITargetEngine` by its `Kind`. Move Npgsql connection-string construction out of `ServerConnectionFactory` and onto the engine via `BuildConnectionString(ConnectionParameters)`. Every current direct consumer of `ITargetEngine` (two services, three endpoint handlers) resolves its engine from the owning `Server.Kind` instead of receiving the concrete engine by DI.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core (Npgsql), Vogen value objects, xUnit v3.

## Global Constraints

- Develop on branch `feat/mongodb-support` (already created). Never commit to `main`.
- Commit messages are a single subject line — no body paragraph, repo style (imperative, no `feat:` prefix).
- Suppress experimental API warnings with inline `#pragma warning disable` in the `.cs` file, not `<NoWarn>`.
- Abstract database-specific operations behind interfaces — never hard-code Npgsql calls in domain/business code.
- Preserve existing comments; only remove one if it is factually wrong or references something that no longer exists.
- Unit tests (`tests/SluiceBase.Api.Tests`) run locally: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`.
- Integration tests need a healthy Aspire stack that is not available in automated sessions — verify those paths with `dotnet build` and rely on CI. Do not block a task on running `tests/IntegrationTests`.
- This is a **pure refactor**: PostgreSQL behavior must not change. The existing integration tests (`QueryServiceTests`, `SchemaServiceTests`, `TargetEngineTests`, `SchemaEndpointTests`, `QueryEndpointTest`) are the behavioral safety net and must remain green in CI.

---

## File Structure

**Created:**
- `src/SluiceBase.Core/Targets/ITargetEngineRegistry.cs` — the resolution interface (Core).
- `src/SluiceBase.Core/Targets/ConnectionParameters.cs` — engine-neutral connection inputs (Core).
- `src/SluiceBase.Api/Targets/TargetEngineRegistry.cs` — dictionary-backed registry keyed on `Kind` (Api).
- `tests/SluiceBase.Api.Tests/TargetEngineRegistryTests.cs` — registry unit tests.
- `tests/SluiceBase.Api.Tests/PostgresTargetEngineConnectionStringTests.cs` — connection-string unit tests.

**Modified:**
- `src/SluiceBase.Core/Targets/ITargetEngine.cs` — add `BuildConnectionString`.
- `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` — implement `BuildConnectionString`.
- `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs` — build via the resolved engine, drop `using Npgsql`.
- `src/SluiceBase.Api/Program.cs` — register the registry.
- `src/SluiceBase.Api/Services/IQueryService.cs` — resolve engine by kind.
- `src/SluiceBase.Api/Services/ISchemaService.cs` — resolve engine by kind.
- `src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs` — resolve engine by kind (`TestDatabaseConnection`).
- `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs` — resolve engine by kind (`ExportSchemaDdl`).
- `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` — resolve engine by kind (`Execute`).

---

## Task 1: Engine registry

**Files:**
- Create: `src/SluiceBase.Core/Targets/ITargetEngineRegistry.cs`
- Create: `src/SluiceBase.Api/Targets/TargetEngineRegistry.cs`
- Modify: `src/SluiceBase.Api/Program.cs:48-50`
- Test: `tests/SluiceBase.Api.Tests/TargetEngineRegistryTests.cs`

**Interfaces:**
- Consumes: existing `ITargetEngine.Kind` (string), existing `PostgresTargetEngine` (parameterless ctor, `internal`).
- Produces: `ITargetEngineRegistry.Resolve(string kind) -> ITargetEngine`; throws `InvalidOperationException` for an unregistered kind. Case-insensitive match.

- [ ] **Step 1: Write the failing test**

Create `tests/SluiceBase.Api.Tests/TargetEngineRegistryTests.cs`:

```csharp
using SluiceBase.Api.Targets;

namespace SluiceBase.Api.Tests;

public class TargetEngineRegistryTests
{
    [Fact]
    public void Resolve_ReturnsEngineMatchingKind()
    {
        var registry = new TargetEngineRegistry([new PostgresTargetEngine()]);

        var engine = registry.Resolve("postgres");

        Assert.Equal("postgres", engine.Kind);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var registry = new TargetEngineRegistry([new PostgresTargetEngine()]);

        var engine = registry.Resolve("POSTGRES");

        Assert.Equal("postgres", engine.Kind);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownKind()
    {
        var registry = new TargetEngineRegistry([new PostgresTargetEngine()]);

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("mongodb"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: FAIL — compile error, `TargetEngineRegistry` does not exist.

- [ ] **Step 3: Create the interface**

Create `src/SluiceBase.Core/Targets/ITargetEngineRegistry.cs`:

```csharp
namespace SluiceBase.Core.Targets;

public interface ITargetEngineRegistry
{
    ITargetEngine Resolve(string kind);
}
```

- [ ] **Step 4: Create the implementation**

Create `src/SluiceBase.Api/Targets/TargetEngineRegistry.cs`:

```csharp
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Targets;

internal sealed class TargetEngineRegistry : ITargetEngineRegistry
{
    private readonly IReadOnlyDictionary<string, ITargetEngine> _engines;

    public TargetEngineRegistry(IEnumerable<ITargetEngine> engines) =>
        _engines = engines.ToDictionary(e => e.Kind, StringComparer.OrdinalIgnoreCase);

    public ITargetEngine Resolve(string kind) =>
        _engines.TryGetValue(kind, out var engine)
            ? engine
            : throw new InvalidOperationException($"No target engine registered for kind '{kind}'.");
}
```

- [ ] **Step 5: Register the registry in DI**

In `src/SluiceBase.Api/Program.cs`, immediately after the existing line
`builder.Services.AddSingleton<ITargetEngine, PostgresTargetEngine>();` add:

```csharp
builder.Services.AddSingleton<ITargetEngineRegistry, TargetEngineRegistry>();
```

(Leave the existing `AddSingleton<ITargetEngine, PostgresTargetEngine>()` in place — the registry receives all registered `ITargetEngine` instances via `IEnumerable<ITargetEngine>`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS (all three registry tests).

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Targets/ITargetEngineRegistry.cs \
        src/SluiceBase.Api/Targets/TargetEngineRegistry.cs \
        src/SluiceBase.Api/Program.cs \
        tests/SluiceBase.Api.Tests/TargetEngineRegistryTests.cs
git commit -m "Add target engine registry resolving engines by kind"
```

---

## Task 2: Connection-string building on the engine seam

**Files:**
- Create: `src/SluiceBase.Core/Targets/ConnectionParameters.cs`
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- Modify: `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs`
- Test: `tests/SluiceBase.Api.Tests/PostgresTargetEngineConnectionStringTests.cs`

**Interfaces:**
- Consumes: `ITargetEngineRegistry.Resolve` (Task 1); existing `Server.Host`, `Server.Port`, `Server.Kind`, `Database.DatabaseName`, `Credential.Username`.
- Produces: `ConnectionParameters(string Host, int Port, string Database, string Username, string Password)`; `ITargetEngine.BuildConnectionString(ConnectionParameters) -> string`.

- [ ] **Step 1: Write the failing test**

Create `tests/SluiceBase.Api.Tests/PostgresTargetEngineConnectionStringTests.cs`:

```csharp
using SluiceBase.Api.Targets;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

public class PostgresTargetEngineConnectionStringTests
{
    [Fact]
    public void BuildConnectionString_IncludesAllParameters()
    {
        var engine = new PostgresTargetEngine();

        var cs = engine.BuildConnectionString(
            new ConnectionParameters("db.example.com", 5433, "appdb", "reader", "s3cret"));

        Assert.Contains("Host=db.example.com", cs);
        Assert.Contains("Port=5433", cs);
        Assert.Contains("Database=appdb", cs);
        Assert.Contains("Username=reader", cs);
        Assert.Contains("Password=s3cret", cs);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: FAIL — compile error, `ConnectionParameters` / `BuildConnectionString` do not exist.

- [ ] **Step 3: Create the ConnectionParameters record**

Create `src/SluiceBase.Core/Targets/ConnectionParameters.cs`:

```csharp
namespace SluiceBase.Core.Targets;

// Engine-neutral inputs for building a connection string. Mongo-specific options
// (connection mode, authSource, replica set, TLS) are added in a later phase.
public sealed record ConnectionParameters(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password);
```

- [ ] **Step 4: Add BuildConnectionString to the interface**

In `src/SluiceBase.Core/Targets/ITargetEngine.cs`, add this member to the `ITargetEngine` interface (place it directly after the `string Kind { get; }` line):

```csharp
    string BuildConnectionString(ConnectionParameters parameters);
```

- [ ] **Step 5: Implement BuildConnectionString on the Postgres engine**

In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, add the method directly under the existing `public string Kind => "postgres";` line:

```csharp
    public string BuildConnectionString(ConnectionParameters p) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = p.Host,
            Port = p.Port,
            Database = p.Database,
            Username = p.Username,
            Password = p.Password,
        }.ConnectionString;
```

(`NpgsqlConnectionStringBuilder` is already imported at the top of this file.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Refactor ServerConnectionFactory to build via the resolved engine**

In `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs`:

1. Remove the `using Npgsql;` line.
2. Add `using SluiceBase.Core.Targets;`.
3. Change the constructor to also receive the registry:

```csharp
internal sealed class ServerConnectionFactory(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    ITargetEngineRegistry engineRegistry) : IServerConnectionFactory
```

4. Replace the closing `return new NpgsqlConnectionStringBuilder { ... }.ConnectionString;` block with:

```csharp
        var engine = engineRegistry.Resolve(database.Server!.Kind);

        return engine.BuildConnectionString(new ConnectionParameters(
            database.Server.Host,
            database.Server.Port,
            database.DatabaseName,
            credential.Username,
            password));
```

(The method already loads `database` with `.Include(d => d.Server)`, so `database.Server` is populated.)

- [ ] **Step 8: Verify the build**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded, no warnings introduced.

- [ ] **Step 9: Commit**

```bash
git add src/SluiceBase.Core/Targets/ConnectionParameters.cs \
        src/SluiceBase.Core/Targets/ITargetEngine.cs \
        src/SluiceBase.Api/Targets/PostgresTargetEngine.cs \
        src/SluiceBase.Api/Servers/ServerConnectionFactory.cs \
        tests/SluiceBase.Api.Tests/PostgresTargetEngineConnectionStringTests.cs
git commit -m "Move connection string building behind the target engine seam"
```

---

## Task 3: Resolve engine by kind in the services

**Files:**
- Modify: `src/SluiceBase.Api/Services/IQueryService.cs`
- Modify: `src/SluiceBase.Api/Services/ISchemaService.cs`

**Interfaces:**
- Consumes: `ITargetEngineRegistry.Resolve` (Task 1); `Database.Server.Kind`.
- Produces: no new public surface; `QueryService` and `SchemaService` now depend on `ITargetEngineRegistry` instead of `ITargetEngine`.

- [ ] **Step 1: Refactor QueryService**

In `src/SluiceBase.Api/Services/IQueryService.cs`:

1. In the `QueryService` primary constructor, replace the parameter `ITargetEngine targetEngine,` with `ITargetEngineRegistry engineRegistry,`.
2. Change the database load to include the server. Replace:

```csharp
        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
```

with:

```csharp
        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
```

3. Immediately before the line `var data = await targetEngine.ExecuteQueryAsync(...)`, add:

```csharp
            var targetEngine = engineRegistry.Resolve(database.Server!.Kind);
```

(The `ExecuteQueryAsync` call itself is unchanged.)

- [ ] **Step 2: Refactor SchemaService**

In `src/SluiceBase.Api/Services/ISchemaService.cs`:

1. In the `SchemaService` primary constructor, replace `ITargetEngine targetEngine,` with `ITargetEngineRegistry engineRegistry,`.
2. Change the database load to include the server. Replace:

```csharp
        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
```

with:

```csharp
        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
```

3. Immediately before the line `var tree = await targetEngine.GetSchemaAsync(connectionString, ct);`, add:

```csharp
            var targetEngine = engineRegistry.Resolve(database.Server!.Kind);
```

- [ ] **Step 3: Verify the build**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded. Both services now compile against `ITargetEngineRegistry`.

- [ ] **Step 4: Run unit tests**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS (unchanged — no service-level unit tests; this confirms nothing else broke).

> Integration coverage for these services (`QueryServiceTests`, `SchemaServiceTests`) is exercised in CI against a real Postgres container; do not attempt to run `tests/IntegrationTests` locally.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Services/IQueryService.cs \
        src/SluiceBase.Api/Services/ISchemaService.cs
git commit -m "Resolve target engine by server kind in query and schema services"
```

---

## Task 4: Resolve engine by kind in the endpoint handlers

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs` (`TestDatabaseConnection`)
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs` (`ExportSchemaDdl`)
- Modify: `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` (`Execute`)

**Interfaces:**
- Consumes: `ITargetEngineRegistry.Resolve` (Task 1); `Database.Server.Kind`.
- Produces: no new public surface; the three handlers receive `ITargetEngineRegistry` instead of `ITargetEngine`.

- [ ] **Step 1: Refactor DatabaseEndpoints.TestDatabaseConnection**

In `src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs`, in `TestDatabaseConnection`:

1. Replace the handler parameter `ITargetEngine targetEngine,` with `ITargetEngineRegistry engineRegistry,`.
2. Change the database load to include the server. Replace:

```csharp
        var dbRecord = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
```

with:

```csharp
        var dbRecord = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
```

3. Directly after the `if (dbRecord is null) { return TypedResults.NotFound(); }` block, add:

```csharp
        var targetEngine = engineRegistry.Resolve(dbRecord.Server!.Kind);
```

(The two existing `targetEngine.TestConnectionAsync(...)` calls now bind to this local.)

- [ ] **Step 2: Refactor SchemaEndpoint.ExportSchemaDdl**

In `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`, in `ExportSchemaDdl`:

1. Replace the handler parameter `ITargetEngine targetEngine,` with `ITargetEngineRegistry engineRegistry,`.
2. Change the database load to include the server. Replace:

```csharp
        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
```

with:

```csharp
        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
```

3. Inside the `try` block, replace:

```csharp
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var ddl = await targetEngine.ExportSchemaDdlAsync(connectionString, ct);
```

with:

```csharp
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var targetEngine = engineRegistry.Resolve(database.Server!.Kind);
            var ddl = await targetEngine.ExportSchemaDdlAsync(connectionString, ct);
```

- [ ] **Step 3: Refactor UpdateEndpoints.Execute**

In `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`, in `Execute`:

1. Replace the handler parameter `ITargetEngine targetEngine,` with `ITargetEngineRegistry engineRegistry,`.
2. Find where the `database` local is loaded earlier in the handler and ensure that query includes the server: add `.Include(d => d.Server)` to it (the load is the query whose result is assigned to `database` and later used as `database.Id`).
3. Inside the execute `try` block, replace:

```csharp
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Write, ct);
            var raw = await targetEngine.ExecuteUpdateAsync(
                connectionString,
                request.SqlText,
                linkedCts.Token);
```

with:

```csharp
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Write, ct);
            var targetEngine = engineRegistry.Resolve(database.Server!.Kind);
            var raw = await targetEngine.ExecuteUpdateAsync(
                connectionString,
                request.SqlText,
                linkedCts.Token);
```

- [ ] **Step 4: Verify the build**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded. Confirm there are **no** remaining direct `ITargetEngine` handler/constructor injections:

Run: `grep -rn "ITargetEngine " src --include="*.cs" | grep -v "/obj/" | grep -v "/bin/"`
Expected: only the interface definition context remains; no `ITargetEngine targetEngine,` DI parameters.

- [ ] **Step 5: Run unit tests**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS.

> Endpoint behavior is covered by `SchemaEndpointTests` / `QueryEndpointTest` in CI.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs \
        src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs \
        src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs
git commit -m "Resolve target engine by server kind in database, schema, and update endpoints"
```

---

## Done criteria

- `ITargetEngineRegistry` resolves engines by `Kind`; PostgreSQL resolves as before.
- No code outside the engine layer constructs an Npgsql connection string.
- No handler or service receives a concrete `ITargetEngine` by DI; all resolve by `Server.Kind`.
- `dotnet build` clean; `tests/SluiceBase.Api.Tests` green; CI integration suite green.
- PostgreSQL behavior unchanged — this phase adds the seam only. MongoDB engine registration, `Server` model growth, and Mongo `ConnectionParameters` options land in Phase 2.
