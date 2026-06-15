# MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Claude Code / Codex connect to SluiceBase as a remote MCP server and, acting as the authenticated user, list databases, browse schema, and run read-only queries — reusing all existing permission, sensitive-column, and audit logic.

**Architecture:** Host a streamable-HTTP MCP server in the existing `SluiceBase.Api` container. Authenticate clients via OAuth 2.1 where SluiceBase is a minimal authorization-server façade brokering upstream to the configured OIDC provider, issuing opaque (hashed) access + refresh tokens. Extract the inline query/schema/catalog endpoint logic into shared services so HTTP and MCP run one identical, non-bypassable code path.

**Tech Stack:** .NET 10, ASP.NET minimal APIs, EF Core 10 (Npgsql, snake_case), Vogen value objects, `ModelContextProtocol.AspNetCore` 1.4.0, xUnit IntegrationTests with the Aspire `SluiceBaseStackFactory` + `KeycloakLoginHelper`.

**Spec:** `docs/superpowers/specs/2026-06-15-mcp-server-design.md`

**Conventions (from CLAUDE.md / memory):**
- Branch `feat/mcp-server` (already created). Single-line commit messages.
- Never hand-edit EF migrations; analyzer warnings suppressed via `.editorconfig`.
- One squashed migration for this branch — regenerate it as schema evolves (Task 8), never accumulate multiple.
- `Array<T>` not `T[]` in any TypeScript.
- Verify backend with `dotnet build SluiceBase.slnx` (warnings-as-errors). Run `tests/IntegrationTests` locally as needed. CI gates regenerated `src/SluiceBase.Api/openapi.json` and `src/frontend/src/api/schema.ts` — regenerate & commit them (Task 21).

---

## File structure

**New files:**
- `src/SluiceBase.Core/Queries/QuerySource.cs` — enum `Ui` / `Mcp`.
- `src/SluiceBase.Core/Mcp/McpOAuthClient.cs`, `McpAuthCode.cs`, `McpToken.cs`, `McpTokenType.cs` + their `*Id` Vogen ids.
- `src/SluiceBase.Api/Data/Configurations/McpOAuthClientConfiguration.cs`, `McpAuthCodeConfiguration.cs`, `McpTokenConfiguration.cs`.
- `src/SluiceBase.Api/Services/ICatalogService.cs` (+ impl), `ISchemaService.cs` (+ impl), `IQueryService.cs` (+ impl).
- `src/SluiceBase.Api/Mcp/TokenHasher.cs`, `IMcpTokenService.cs` (+ impl), `McpOptions.cs`.
- `src/SluiceBase.Api/Mcp/Oauth/OAuthMetadataEndpoints.cs`, `OAuthRegistrationEndpoints.cs`, `OAuthAuthorizeEndpoints.cs`, `OAuthTokenEndpoints.cs`.
- `src/SluiceBase.Api/Mcp/McpBearerAuthenticationHandler.cs`, `McpAuthExtensions.cs`.
- `src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs` (the three MCP tools).
- Tests under `tests/IntegrationTests/`: `CatalogServiceTests.cs`, `SchemaServiceTests.cs`, `QueryServiceTests.cs`, `McpTokenServiceTests.cs`, `OAuthMetadataTests.cs`, `OAuthFlowTests.cs`, `McpBearerAuthTests.cs`, `McpToolsTests.cs`. Unit tests for pure logic under `tests/CoverageReport.Tests/` where no DB is needed (`TokenHasherTests.cs`).

**Modified files:**
- `src/SluiceBase.Api/SluiceBase.Api.csproj` — add `ModelContextProtocol.AspNetCore`.
- `src/SluiceBase.Core/Queries/QueryLog.cs` — add `Source` property + `Create` param.
- `src/SluiceBase.Api/Endpoints/{Catalog,Schema,Query}Endpoints.cs` — become thin wrappers.
- `src/SluiceBase.Api/Data/AppDbContext.cs` — add DbSets.
- `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs` — map `Source`.
- `src/SluiceBase.Api/Auth/AuthSetup.cs` — register the bearer scheme.
- `src/SluiceBase.Api/Program.cs` — register services, MCP server, map endpoints.
- `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` — map OAuth endpoints.
- `README.md` — MCP client setup section.

---

## Phase 1 — Shared access services (behavior-preserving refactor)

### Task 1: Add `QuerySource` to the audit log

**Files:**
- Create: `src/SluiceBase.Core/Queries/QuerySource.cs`
- Modify: `src/SluiceBase.Core/Queries/QueryLog.cs`
- Modify: `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs`
- Test: `tests/CoverageReport.Tests/QueryLogTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CoverageReport.Tests/QueryLogTests.cs
using SluiceBase.Core.Queries;
using Xunit;

namespace CoverageReport.Tests;

public class QueryLogTests
{
    [Fact]
    public void Create_DefaultsSourceToUi()
    {
        var log = QueryLog.Create(null, null, "select 1", QueryLogStatus.Success,
            DateTimeOffset.UnixEpoch, 1, 0, null);
        Assert.Equal(QuerySource.Ui, log.Source);
    }

    [Fact]
    public void Create_RecordsMcpSource()
    {
        var log = QueryLog.Create(null, null, "select 1", QueryLogStatus.Success,
            DateTimeOffset.UnixEpoch, 1, 0, null, source: QuerySource.Mcp);
        Assert.Equal(QuerySource.Mcp, log.Source);
    }
}
```

- [ ] **Step 2: Run test, verify it fails to compile** — Run: `dotnet test tests/CoverageReport.Tests --filter QueryLogTests`. Expected: build error, `QuerySource` not found.

- [ ] **Step 3: Create the enum**

```csharp
// src/SluiceBase.Core/Queries/QuerySource.cs
namespace SluiceBase.Core.Queries;

public enum QuerySource
{
    Ui,
    Mcp,
}
```

- [ ] **Step 4: Add `Source` to `QueryLog`** — add property and a trailing optional `Create` param (placed AFTER `sensitiveColumns` to preserve existing call sites):

```csharp
    public string[] SensitiveColumns { get; private set; } = [];
    public QuerySource Source { get; private set; }
```

```csharp
        string[]? sensitiveColumns = null,
        QuerySource source = QuerySource.Ui) => new()
    {
        // ...existing assignments...
        SensitiveColumns = sensitiveColumns ?? [],
        Source = source,
    };
```

- [ ] **Step 5: Map the column** — in `QueryLogConfiguration.Configure`, add:

```csharp
        builder.Property(q => q.Source).HasMaxLength(16).HasConversion<string>().IsRequired();
```

- [ ] **Step 6: Run test, verify pass** — Run: `dotnet test tests/CoverageReport.Tests --filter QueryLogTests`. Expected: PASS. (Migration generated later in Task 8.)

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Queries src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs tests/CoverageReport.Tests/QueryLogTests.cs
git commit -m "Add QuerySource to query audit log"
```

---

### Task 2: Extract `ICatalogService`

**Files:**
- Create: `src/SluiceBase.Api/Services/ICatalogService.cs`
- Modify: `src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs`
- Modify: `src/SluiceBase.Api/Program.cs` (DI registration)
- Test: `tests/IntegrationTests/CatalogServiceTests.cs`

The service holds the exact logic currently inline in `CatalogEndpoints.ListServers` (lines 19-64): server-admin sees all active databases, others see only databases they hold a `UserDatabaseRole` on, grouped by server.

- [ ] **Step 1: Write the failing test** — copy the structure of `tests/IntegrationTests/CatalogEndpointTests.cs` but resolve the service from DI and assert it returns the same grouped shape. Use the existing `SluiceBaseStackFactory` + `DatabaseRoleTestHelper` to grant a role, then:

```csharp
// tests/IntegrationTests/CatalogServiceTests.cs — sketch following CatalogEndpointTests patterns
[Fact]
public async Task ListAccessible_ReturnsOnlyRoledDatabases_ForNonAdmin()
{
    // arrange: seed user with query:execute on exactly one database (see DatabaseRoleTestHelper)
    // act: resolve ICatalogService from a request scope, call ListAccessibleAsync(user, ct)
    // assert: returns exactly that one database, grouped under its server
}
```

- [ ] **Step 2: Run test, verify fails** — Run: `dotnet test tests/IntegrationTests --filter CatalogServiceTests`. Expected: build error, `ICatalogService` not found.

- [ ] **Step 3: Create the service** — move the query/grouping logic verbatim, returning the existing `CatalogEndpoints.CatalogServersResponse` record (keep the response records where they are, reference them from the service):

```csharp
// src/SluiceBase.Api/Services/ICatalogService.cs
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using static SluiceBase.Api.Endpoints.CatalogEndpoints;

namespace SluiceBase.Api.Services;

internal interface ICatalogService
{
    Task<CatalogServersResponse> ListAccessibleAsync(User user, CancellationToken ct);
}

internal sealed class CatalogService(AppDbContext db) : ICatalogService
{
    public async Task<CatalogServersResponse> ListAccessibleAsync(User user, CancellationToken ct)
    {
        var isServerAdmin = user.HasPermission(Permissions.ServerManage);
        var baseQuery = db.Databases.AsNoTracking().Where(d => d.DeletedAt == null && !d.IsDisabled);

        List<Database> databases;
        if (isServerAdmin)
        {
            databases = await baseQuery.Include(d => d.Server).ToListAsync(ct);
        }
        else
        {
            var allowedIds = await db.UserDatabaseRoles
                .Where(r => r.UserId == user.Id).Select(r => r.DatabaseId).ToListAsync(ct);
            databases = await baseQuery.Where(d => allowedIds.Contains(d.Id)).Include(d => d.Server).ToListAsync(ct);
        }

        var servers = databases
            .Where(d => d.Server != null && d.Server.DeletedAt == null && !d.Server.IsDisabled)
            .GroupBy(d => d.Server!)
            .OrderBy(g => g.Key.Name)
            .Select(g => new CatalogServerItem(g.Key.Id, g.Key.Name,
                [.. g.Select(d => new CatalogDatabaseItem(d.Id, d.DisplayName, d.CanWrite)).OrderBy(d => d.DisplayName)]))
            .ToList();

        return new CatalogServersResponse(servers);
    }
}
```

- [ ] **Step 4: Rewire the endpoint** — replace the body of `ListServers` with:

```csharp
    private static async Task<Ok<CatalogServersResponse>> ListServers(
        ICatalogService catalog, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        return TypedResults.Ok(await catalog.ListAccessibleAsync(user!, ct));
    }
```

- [ ] **Step 5: Register in DI** — in `Program.cs` after the other scoped registrations: `builder.Services.AddScoped<ICatalogService, CatalogService>();`

- [ ] **Step 6: Run tests, verify pass** — Run: `dotnet test tests/IntegrationTests --filter "CatalogServiceTests|CatalogEndpointTests"`. Expected: PASS (endpoint behavior unchanged).

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Services/ICatalogService.cs src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs src/SluiceBase.Api/Program.cs tests/IntegrationTests/CatalogServiceTests.cs
git commit -m "Extract ICatalogService from catalog endpoint"
```

---

### Task 3: Extract `ISchemaService`

**Files:**
- Create: `src/SluiceBase.Api/Services/ISchemaService.cs`
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`, `Program.cs`
- Test: `tests/IntegrationTests/SchemaServiceTests.cs`

Move the `GetSchema` logic (SchemaEndpoint.cs lines 27-105): role check, fetch schema via `targetEngine`, annotate sensitive/restricted columns honoring per-user bypasses. Return type: a discriminated result so the endpoint can map to NotFound / Forbid / BadRequest. Use a small result record:

- [ ] **Step 1: Write the failing test** — model on `tests/IntegrationTests/SchemaEndpointTests.cs`: grant role, assert `GetAnnotatedSchemaAsync` returns a tree with the same sensitive flags; assert `Forbidden` outcome when no role.

- [ ] **Step 2: Run test, verify fails** — Run: `dotnet test tests/IntegrationTests --filter SchemaServiceTests`. Expected: build error.

- [ ] **Step 3: Create the service** with an explicit outcome enum so callers (endpoint + MCP tool) map cleanly:

```csharp
// src/SluiceBase.Api/Services/ISchemaService.cs
namespace SluiceBase.Api.Services;

internal enum SchemaOutcome { Ok, NotFound, Forbidden, Error }

internal sealed record SchemaResult(SchemaOutcome Outcome, SchemaTree? Tree, string? Error);

internal interface ISchemaService
{
    Task<SchemaResult> GetAnnotatedSchemaAsync(User user, DatabaseId databaseId, CancellationToken ct);
}
```

Implementation moves the existing body verbatim, returning `new SchemaResult(SchemaOutcome.Ok, annotatedTree, null)` etc. (Reuse `IServerConnectionFactory`, `ITargetEngine`, `CredentialKind.Read`, the `SensitiveColumns`/`UserColumnBypasses` annotation block from lines 56-99.)

- [ ] **Step 4: Rewire `GetSchema`** to call the service and map the outcome:

```csharp
        var result = await schema.GetAnnotatedSchemaAsync(user!, databaseId, ct);
        return result.Outcome switch
        {
            SchemaOutcome.Ok => TypedResults.Ok(result.Tree!),
            SchemaOutcome.NotFound => TypedResults.NotFound(),
            SchemaOutcome.Forbidden => TypedResults.Forbid(),
            _ => TypedResults.BadRequest(result.Error!),
        };
```

(Leave `ExportSchemaDdl` as-is for v1 — not exposed via MCP.)

- [ ] **Step 5: Register** `builder.Services.AddScoped<ISchemaService, SchemaService>();`

- [ ] **Step 6: Run tests** — Run: `dotnet test tests/IntegrationTests --filter "SchemaServiceTests|SchemaEndpointTests"`. Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Services/ISchemaService.cs src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs src/SluiceBase.Api/Program.cs tests/IntegrationTests/SchemaServiceTests.cs
git commit -m "Extract ISchemaService from schema endpoint"
```

---

### Task 4: Extract `IQueryService` (with source tagging)

**Files:**
- Create: `src/SluiceBase.Api/Services/IQueryService.cs`
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, `Program.cs`
- Test: `tests/IntegrationTests/QueryServiceTests.cs`

Move the full `ExecuteQuery` pipeline (QueryEndpoints.cs lines 29-163): db lookup → `query:execute` role check → sensitive-column screening/blocking → read-credential connection → timeout → execution → audit `QueryLog`. Add a `QuerySource source` parameter, threaded into every `QueryLog.Create(...)` call in the method.

- [ ] **Step 1: Write the failing test** — model on `tests/IntegrationTests/QueryEndpointTest.cs`: execute a SELECT through the service with `source: QuerySource.Mcp`, assert success rows AND assert the persisted `QueryLog.Source == QuerySource.Mcp`. Add a second test: blocked sensitive column returns the blocked outcome and logs `QueryLogStatus.Blocked`.

- [ ] **Step 2: Run test, verify fails** — Run: `dotnet test tests/IntegrationTests --filter QueryServiceTests`. Expected: build error.

- [ ] **Step 3: Create the service** with an outcome result mirroring the endpoint's union (Ok/NotFound/Forbidden/Blocked/BadRequest):

```csharp
// src/SluiceBase.Api/Services/IQueryService.cs
namespace SluiceBase.Api.Services;

internal enum QueryOutcome { Ok, NotFound, Forbidden, Blocked, BadRequest }

internal sealed record BlockedColumn(string Schema, string Table, string Column);

internal sealed record QueryExecutionResult(
    QueryOutcome Outcome,
    QueryEndpoints.QueryResponse? Response,
    IReadOnlyList<BlockedColumn>? BlockedColumns,
    string? Error);

internal interface IQueryService
{
    Task<QueryExecutionResult> ExecuteAsync(User user, DatabaseId databaseId, string sql, QuerySource source, CancellationToken ct);
}
```

The implementation is the moved endpoint body. Every `QueryLog.Create(...)` call gains `source: source`. Inject `IConfiguration`, `TimeProvider`, `IServerConnectionFactory`, `ITargetEngine`, `AppDbContext` as the endpoint did.

- [ ] **Step 4: Rewire `ExecuteQuery`** to call `queryService.ExecuteAsync(user!, request.DatabaseId, request.Sql, QuerySource.Ui, ct)` and map `QueryOutcome` to the existing `TypedResults` union (Blocked → the `TypedResults.Problem(403, type: "sensitive_columns", extensions.columns = BlockedColumns)` shape from lines 105-109).

- [ ] **Step 5: Register** `builder.Services.AddScoped<IQueryService, QueryService>();`

- [ ] **Step 6: Run tests** — Run: `dotnet test tests/IntegrationTests --filter "QueryServiceTests|QueryEndpointTest"`. Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Services/IQueryService.cs src/SluiceBase.Api/Endpoints/QueryEndpoints.cs src/SluiceBase.Api/Program.cs tests/IntegrationTests/QueryServiceTests.cs
git commit -m "Extract IQueryService with source tagging"
```

---

## Phase 2 — OAuth token store (entities, config, migration)

### Task 5: `McpOAuthClient` entity

**Files:**
- Create: `src/SluiceBase.Core/Mcp/McpOAuthClientId.cs`, `McpOAuthClient.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/McpOAuthClientConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`
- Test: `tests/IntegrationTests/McpTokenServiceTests.cs` (persistence smoke test added here, expanded in Task 9)

Follow the existing Vogen-id pattern (see `src/SluiceBase.Core/Queries/QueryLogId.cs`) for `McpOAuthClientId` with `FromNewVersion7Guid()`.

- [ ] **Step 1: Create the Vogen id** — mirror `QueryLogId.cs` exactly, renaming type to `McpOAuthClientId`. Register its EF converter in `src/SluiceBase.Api/Data/Converters/VogenEfCoreConverters.cs` (follow the existing `[EfCoreConverter<...>]` attribute list there).

- [ ] **Step 2: Create the entity** — clients are public (PKCE), so no secret:

```csharp
// src/SluiceBase.Core/Mcp/McpOAuthClient.cs
namespace SluiceBase.Core.Mcp;

public sealed class McpOAuthClient
{
#pragma warning disable CS8618
    private McpOAuthClient() { }
#pragma warning restore CS8618

    public McpOAuthClientId Id { get; private set; }
    public string ClientId { get; private set; }
    public string ClientName { get; private set; }
    public List<string> RedirectUris { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    public static McpOAuthClient Register(string clientName, IEnumerable<string> redirectUris, DateTimeOffset at) => new()
    {
        Id = McpOAuthClientId.FromNewVersion7Guid(),
        ClientId = Guid.NewGuid().ToString("N"),
        ClientName = clientName,
        RedirectUris = redirectUris.ToList(),
        CreatedAt = at,
    };
}
```

- [ ] **Step 3: EF config** — `ToTable("mcp_oauth_client")`, key `Id`, unique index on `ClientId`, `RedirectUris` stored as JSON (`builder.Property(c => c.RedirectUris).HasColumnType("jsonb")` with a value comparer; copy the JSON list pattern from `ClaimRecord` mapping in `ExternalLoginConfiguration.cs`).

- [ ] **Step 4: Add DbSet** — `public DbSet<McpOAuthClient> McpOAuthClients => Set<McpOAuthClient>();`

- [ ] **Step 5: Build** — Run: `dotnet build SluiceBase.slnx`. Expected: success.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Core/Mcp/McpOAuthClient*.cs src/SluiceBase.Api/Data
git commit -m "Add McpOAuthClient entity"
```

---

### Task 6: `McpAuthCode` entity

**Files:**
- Create: `src/SluiceBase.Core/Mcp/McpAuthCodeId.cs`, `McpAuthCode.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/McpAuthCodeConfiguration.cs`
- Modify: `AppDbContext.cs`, `VogenEfCoreConverters.cs`

Stores the SluiceBase-issued authorization code (hashed), bound to the resolved user, the requesting client, the client's redirect_uri, and the PKCE `code_challenge` (S256) for verification at `/token`.

- [ ] **Step 1: Vogen id** `McpAuthCodeId` (mirror existing) + register converter.
- [ ] **Step 2: Entity**

```csharp
// src/SluiceBase.Core/Mcp/McpAuthCode.cs
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Mcp;

public sealed class McpAuthCode
{
#pragma warning disable CS8618
    private McpAuthCode() { }
#pragma warning restore CS8618

    public McpAuthCodeId Id { get; private set; }
    public string CodeHash { get; private set; }
    public string ClientId { get; private set; }
    public UserId UserId { get; private set; }
    public string RedirectUri { get; private set; }
    public string CodeChallenge { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool Consumed { get; private set; }

    public static McpAuthCode Issue(string codeHash, string clientId, UserId userId,
        string redirectUri, string codeChallenge, DateTimeOffset expiresAt) => new()
    {
        Id = McpAuthCodeId.FromNewVersion7Guid(),
        CodeHash = codeHash, ClientId = clientId, UserId = userId,
        RedirectUri = redirectUri, CodeChallenge = codeChallenge, ExpiresAt = expiresAt,
    };

    public void Consume() => Consumed = true;
}
```

- [ ] **Step 3: EF config** — `ToTable("mcp_auth_code")`, unique index on `CodeHash`, FK to `User`.
- [ ] **Step 4: DbSet** + **Step 5: Build** (`dotnet build SluiceBase.slnx`) + **Step 6: Commit** `"Add McpAuthCode entity"`.

---

### Task 7: `McpToken` entity

**Files:**
- Create: `src/SluiceBase.Core/Mcp/McpTokenId.cs`, `McpTokenType.cs`, `McpToken.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/McpTokenConfiguration.cs`
- Modify: `AppDbContext.cs`, `VogenEfCoreConverters.cs`

- [ ] **Step 1: Vogen id + enum**

```csharp
// src/SluiceBase.Core/Mcp/McpTokenType.cs
namespace SluiceBase.Core.Mcp;
public enum McpTokenType { Access, Refresh }
```

- [ ] **Step 2: Entity** — access and refresh tokens share this table; refresh tokens link an access token via `ParentId` so refreshing can rotate:

```csharp
// src/SluiceBase.Core/Mcp/McpToken.cs
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Mcp;

public sealed class McpToken
{
#pragma warning disable CS8618
    private McpToken() { }
#pragma warning restore CS8618

    public McpTokenId Id { get; private set; }
    public string TokenHash { get; private set; }
    public McpTokenType Type { get; private set; }
    public UserId UserId { get; private set; }
    public string ClientId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public bool Revoked { get; private set; }

    public static McpToken Issue(string tokenHash, McpTokenType type, UserId userId,
        string clientId, DateTimeOffset createdAt, DateTimeOffset expiresAt) => new()
    {
        Id = McpTokenId.FromNewVersion7Guid(),
        TokenHash = tokenHash, Type = type, UserId = userId, ClientId = clientId,
        CreatedAt = createdAt, ExpiresAt = expiresAt,
    };

    public void Touch(DateTimeOffset at) => LastUsedAt = at;
    public void Revoke() => Revoked = true;
}
```

- [ ] **Step 3: EF config** — `ToTable("mcp_token")`, unique index on `TokenHash`, index on `(UserId, Type)`, `Type` stored as string, FK to `User`.
- [ ] **Step 4: DbSets** — `McpAuthCodes`, `McpTokens` + **Step 5: Build** + **Step 6: Commit** `"Add McpToken entity"`.

---

### Task 8: Generate the branch migration

**Files:** Create (generated): `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddMcp.*`

This single migration covers Task 1's `query_log.source` column **and** the three new tables. Do not hand-edit it.

- [ ] **Step 1: Generate** — Run: `dotnet ef migrations add AddMcp --project src/SluiceBase.Api`. Expected: a new migration + updated `AppDbContextModelSnapshot.cs`.
- [ ] **Step 2: Inspect** the generated `Up()` — confirm `source` column on `query_log`, and `mcp_oauth_client` / `mcp_auth_code` / `mcp_token` tables with the indexes named in Tasks 5-7. Do NOT edit; if wrong, fix the configuration and regenerate.
- [ ] **Step 3: Apply & verify** — Run: `dotnet build SluiceBase.slnx`. Expected: success (migration analyzer warnings suppressed via `.editorconfig`).
- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Data/Migrations
git commit -m "Add MCP schema migration"
```

> If later schema-affecting tasks change an entity, regenerate THIS migration (remove + re-add `AddMcp`) rather than stacking a second one — per branch convention.

---

## Phase 3 — Token services

### Task 9: `TokenHasher` + `IMcpTokenService`

**Files:**
- Create: `src/SluiceBase.Api/Mcp/TokenHasher.cs`, `src/SluiceBase.Api/Mcp/IMcpTokenService.cs`, `McpOptions.cs`
- Modify: `Program.cs`
- Test: `tests/CoverageReport.Tests/TokenHasherTests.cs`, `tests/IntegrationTests/McpTokenServiceTests.cs`

Opaque tokens are random 256-bit strings; only their SHA-256 hash is stored, so a DB leak can't be replayed.

- [ ] **Step 1: Hasher test (pure, no DB)**

```csharp
// tests/CoverageReport.Tests/TokenHasherTests.cs
using SluiceBase.Api.Mcp;
using Xunit;

namespace CoverageReport.Tests;

public class TokenHasherTests
{
    [Fact] public void Hash_IsStable() => Assert.Equal(TokenHasher.Hash("abc"), TokenHasher.Hash("abc"));
    [Fact] public void Hash_DiffersPerInput() => Assert.NotEqual(TokenHasher.Hash("a"), TokenHasher.Hash("b"));
    [Fact] public void Generate_ProducesUrlSafeDistinctValues()
    {
        var (a, b) = (TokenHasher.Generate(), TokenHasher.Generate());
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
    }
}
```

`CoverageReport.Tests` needs `InternalsVisibleTo` — add `<InternalsVisibleTo Include="CoverageReport.Tests" />` to `SluiceBase.Api.csproj` if not present (it currently only exposes to `IntegrationTests`).

- [ ] **Step 2: Run, verify fails** — Run: `dotnet test tests/CoverageReport.Tests --filter TokenHasherTests`. Expected: build error.

- [ ] **Step 3: Implement hasher**

```csharp
// src/SluiceBase.Api/Mcp/TokenHasher.cs
using System.Security.Cryptography;
using System.Text;

namespace SluiceBase.Api.Mcp;

internal static class TokenHasher
{
    public static string Generate() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: Run, verify pass** — Run: `dotnet test tests/CoverageReport.Tests --filter TokenHasherTests`. Expected: PASS.

- [ ] **Step 5: Options + service interface**

```csharp
// src/SluiceBase.Api/Mcp/McpOptions.cs
namespace SluiceBase.Api.Mcp;

internal sealed class McpOptions
{
    public const string SectionName = "Mcp";
    public bool Enabled { get; set; } = true;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int AuthCodeSeconds { get; set; } = 120;
}
```

```csharp
// src/SluiceBase.Api/Mcp/IMcpTokenService.cs
namespace SluiceBase.Api.Mcp;

internal sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

internal interface IMcpTokenService
{
    Task<string> IssueAuthCodeAsync(string clientId, UserId userId, string redirectUri, string codeChallenge, CancellationToken ct);
    Task<IssuedTokens?> RedeemAuthCodeAsync(string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct);
    Task<IssuedTokens?> RefreshAsync(string clientId, string refreshToken, CancellationToken ct);
    Task<UserId?> ValidateAccessTokenAsync(string accessToken, CancellationToken ct);
}
```

- [ ] **Step 6: Implement the service** — `AppDbContext` + `TimeProvider` + `IOptions<McpOptions>`. Key logic:
  - `IssueAuthCodeAsync`: `var code = TokenHasher.Generate();` persist `McpAuthCode.Issue(TokenHasher.Hash(code), ...)`; return raw `code`.
  - `RedeemAuthCodeAsync`: look up by `TokenHasher.Hash(code)`, assert not consumed/expired, `ClientId`+`RedirectUri` match, and **PKCE**: `Base64UrlEncode(SHA256(codeVerifier)) == CodeChallenge`. Mark consumed, then issue an access + refresh `McpToken` (raw values via `TokenHasher.Generate()`, store hashes). Return `IssuedTokens`.
  - `RefreshAsync`: look up refresh token by hash; assert not revoked/expired and `ClientId` matches; revoke the old access+refresh pair; issue a fresh pair.
  - `ValidateAccessTokenAsync`: look up access token by hash; if not revoked and not expired, `Touch(now)`, `SaveChanges`, return `UserId`; else `null`.

- [ ] **Step 7: DI** — `Program.cs`: `builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName)); builder.Services.AddScoped<IMcpTokenService, McpTokenService>();`

- [ ] **Step 8: Service test (DB)** — `tests/IntegrationTests/McpTokenServiceTests.cs`: issue code → redeem with correct verifier returns tokens; redeem with wrong verifier returns null; refresh rotates and old refresh becomes invalid; validate returns the user; validate after revoke returns null.

- [ ] **Step 9: Run** — Run: `dotnet test tests/IntegrationTests --filter McpTokenServiceTests`. Expected: PASS.

- [ ] **Step 10: Commit** `"Add MCP token service with PKCE and refresh"`.

---

## Phase 4 — OAuth authorization-server façade endpoints

> All endpoints under this phase are mapped in a new `OAuthEndpoints.Map(app)` called from `EndpointMapper`. They are anonymous (no cookie/bearer requirement) except `/authorize`, which triggers the existing OIDC challenge. None participate in antiforgery.

### Task 10: Discovery metadata endpoints

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Oauth/OAuthMetadataEndpoints.cs`
- Modify: `EndpointMapper.cs`
- Test: `tests/IntegrationTests/OAuthMetadataTests.cs`

Serve RFC 9728 protected-resource metadata and RFC 8414 authorization-server metadata. Base URL is derived from the request (works behind the existing `UseForwardedHeaders`).

- [ ] **Step 1: Failing test** — GET `/.well-known/oauth-protected-resource` returns 200 with `authorization_servers` containing the app base URL; GET `/.well-known/oauth-authorization-server` returns `authorization_endpoint`, `token_endpoint`, `registration_endpoint`, and `code_challenge_methods_supported: ["S256"]`.

- [ ] **Step 2: Run, verify fails** — Run: `dotnet test tests/IntegrationTests --filter OAuthMetadataTests`. Expected: 404.

- [ ] **Step 3: Implement**

```csharp
// src/SluiceBase.Api/Mcp/Oauth/OAuthMetadataEndpoints.cs
namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthMetadataEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
        {
            var baseUrl = BaseUrl(ctx);
            return Results.Ok(new Dictionary<string, object?>
            {
                ["resource"] = baseUrl,
                ["authorization_servers"] = new[] { baseUrl },
                ["bearer_methods_supported"] = new[] { "header" },
            });
        }).AllowAnonymous();

        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
        {
            var baseUrl = BaseUrl(ctx);
            return Results.Ok(new Dictionary<string, object?>
            {
                ["issuer"] = baseUrl,
                ["authorization_endpoint"] = $"{baseUrl}/mcp/oauth/authorize",
                ["token_endpoint"] = $"{baseUrl}/mcp/oauth/token",
                ["registration_endpoint"] = $"{baseUrl}/mcp/oauth/register",
                ["response_types_supported"] = new[] { "code" },
                ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
                ["code_challenge_methods_supported"] = new[] { "S256" },
                ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            });
        }).AllowAnonymous();
    }

    private static string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}
```

- [ ] **Step 4: Map** in `EndpointMapper.MapAllEndpoints`: `OAuthMetadataEndpoints.Map(app);` (and the other OAuth maps added in Tasks 11-12).
- [ ] **Step 5: Run, verify pass** + **Step 6: Commit** `"Add OAuth discovery metadata endpoints"`.

---

### Task 11: Dynamic client registration (`/register`)

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Oauth/OAuthRegistrationEndpoints.cs`
- Test: `tests/IntegrationTests/OAuthFlowTests.cs`

RFC 7591: accept `client_name` + `redirect_uris`, persist an `McpOAuthClient`, return `client_id`.

- [ ] **Step 1: Failing test** — POST `/mcp/oauth/register` with `{ "client_name": "Claude Code", "redirect_uris": ["http://127.0.0.1:33418/callback"] }` returns 201 with a non-empty `client_id`, and a row exists.

- [ ] **Step 2: Run, verify fails.**

- [ ] **Step 3: Implement** — bind a request record `(string? client_name, string[] redirect_uris)`; reject empty `redirect_uris` with 400; `McpOAuthClient.Register(...)`, save, return 201 `{ client_id, client_name, redirect_uris }`. `.AllowAnonymous()`.

- [ ] **Step 4: Run, verify pass** + **Step 5: Commit** `"Add OAuth dynamic client registration"`.

---

### Task 12: `/authorize` (broker upstream) + `/token`

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Oauth/OAuthAuthorizeEndpoints.cs`, `OAuthTokenEndpoints.cs`
- Test: `tests/IntegrationTests/OAuthFlowTests.cs` (extend)

This is the core of the façade. `/authorize` must establish the user's identity via the existing OIDC provider, then redirect back to the client with a SluiceBase auth code.

**Flow design:**
1. Client → `GET /mcp/oauth/authorize?response_type=code&client_id=..&redirect_uri=..&code_challenge=..&code_challenge_method=S256&state=..`.
2. Validate `client_id` exists and `redirect_uri` is one of its registered URIs (reject otherwise — do NOT redirect to an unregistered URI).
3. If the request is **not** authenticated by the SluiceBase cookie, issue an OIDC `ChallengeAsync` with a `RedirectUri` back to this same `/authorize` URL (preserving all query params). This reuses the existing `AddOpenIdConnect` config → the user sees the normal Entra/Keycloak login.
4. On return, the cookie is set and `ctx.User` carries the `InternalUserIdClaim` (added by the existing `OnTokenValidated`). Resolve the user, mint an auth code via `IMcpTokenService.IssueAuthCodeAsync`, and 302 to `redirect_uri?code=..&state=..`.

- [ ] **Step 1: Implement `/authorize`**

```csharp
// src/SluiceBase.Api/Mcp/Oauth/OAuthAuthorizeEndpoints.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthAuthorizeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/oauth/authorize", async (
            HttpContext ctx, AppDbContext db, IMcpTokenService tokens, CancellationToken ct) =>
        {
            var q = ctx.Request.Query;
            var clientId = q["client_id"].ToString();
            var redirectUri = q["redirect_uri"].ToString();
            var codeChallenge = q["code_challenge"].ToString();
            var state = q["state"].ToString();

            if (q["response_type"] != "code" || q["code_challenge_method"] != "S256"
                || string.IsNullOrEmpty(codeChallenge))
            {
                return Results.BadRequest("invalid_request");
            }

            var client = await db.McpOAuthClients.AsNoTracking()
                .SingleOrDefaultAsync(c => c.ClientId == clientId, ct);
            if (client is null || !client.RedirectUris.Contains(redirectUri))
            {
                return Results.BadRequest("invalid_client_or_redirect_uri");
            }

            if (ctx.User?.TryGetInternalUserId(out var userId) != true)
            {
                // Not logged in yet: bounce through the existing OIDC provider, then come back here.
                var returnUrl = ctx.Request.Path + ctx.Request.QueryString;
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl },
                    [OpenIdConnectDefaults.AuthenticationScheme]);
            }

            var code = await tokens.IssueAuthCodeAsync(clientId, userId, redirectUri, codeChallenge, ct);
            var sep = redirectUri.Contains('?') ? '&' : '?';
            return Results.Redirect($"{redirectUri}{sep}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
        });
    }
}
```

- [ ] **Step 2: Implement `/token`** — handles both grants; public client (no secret), so PKCE is the proof:

```csharp
// src/SluiceBase.Api/Mcp/Oauth/OAuthTokenEndpoints.cs
namespace SluiceBase.Api.Mcp.Oauth;

internal static class OAuthTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/oauth/token", async (HttpContext ctx, IMcpTokenService tokens, CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var grant = form["grant_type"].ToString();
            var clientId = form["client_id"].ToString();

            IssuedTokens? issued = grant switch
            {
                "authorization_code" => await tokens.RedeemAuthCodeAsync(
                    clientId, form["code"].ToString(), form["redirect_uri"].ToString(),
                    form["code_verifier"].ToString(), ct),
                "refresh_token" => await tokens.RefreshAsync(
                    clientId, form["refresh_token"].ToString(), ct),
                _ => null,
            };

            if (issued is null)
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            return Results.Json(new
            {
                access_token = issued.AccessToken,
                token_type = "Bearer",
                expires_in = issued.ExpiresInSeconds,
                refresh_token = issued.RefreshToken,
            });
        }).AllowAnonymous();
    }
}
```

- [ ] **Step 3: Map** both in `EndpointMapper`. `/token` and `/register` `.AllowAnonymous()`; `/authorize` left without `.AllowAnonymous()` is fine since it self-challenges.

- [ ] **Step 4: Integration test of the full dance** — in `OAuthFlowTests.cs`, drive it with `KeycloakLoginHelper`: register a client; perform the authorize request following redirects through Keycloak login; capture the `code` from the final redirect to the client `redirect_uri`; POST `/token` with the matching `code_verifier`; assert an access + refresh token come back; POST `/token` again with `grant_type=refresh_token`; assert new tokens. (See `tests/IntegrationTests/Supports/KeycloakLoginHelper.cs` for the login round-trip plumbing.)

- [ ] **Step 5: Run** — Run: `dotnet test tests/IntegrationTests --filter OAuthFlowTests`. Expected: PASS. (If the login round-trip needs the dev HTTPS cert, see memory `project_integration_tests_local_oidc`.)

- [ ] **Step 6: Commit** `"Add OAuth authorize and token endpoints"`.

---

## Phase 5 — Bearer authentication

### Task 13: Opaque bearer auth handler + scheme wiring

**Files:**
- Create: `src/SluiceBase.Api/Mcp/McpBearerAuthenticationHandler.cs`, `McpAuthExtensions.cs`
- Modify: `src/SluiceBase.Api/Auth/AuthSetup.cs`
- Test: `tests/IntegrationTests/McpBearerAuthTests.cs`

The handler reads `Authorization: Bearer <token>`, validates it via `IMcpTokenService`, and — critically — issues a principal carrying the **same `AppClaims.InternalUserIdClaim`** the cookie path uses, so `ICurrentUserAccessor` and all permission checks work unchanged.

- [ ] **Step 1: Failing test** — issue a token via `IMcpTokenService` for a known user, call a bearer-protected probe (use `/mcp` once it exists, or a temporary `/mcp/oauth/whoami` returning the resolved user id) with the token → 200; with a garbage token → 401 carrying `WWW-Authenticate: Bearer` with a `resource_metadata` parameter.

- [ ] **Step 2: Run, verify fails.**

- [ ] **Step 3: Implement the handler**

```csharp
// src/SluiceBase.Api/Mcp/McpBearerAuthenticationHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Auth;

namespace SluiceBase.Api.Mcp;

internal sealed class McpBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IMcpTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        var userId = await tokens.ValidateAccessTokenAsync(token, Context.RequestAborted);
        if (userId is null)
        {
            return AuthenticateResult.Fail("Invalid token");
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(AppClaims.InternalUserIdClaim, userId.Value.ToString()));
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate =
            $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Register the scheme** — in `AuthSetup.AddSluiceBaseAuth`, add to the `AddAuthentication(...)` chain:

```csharp
            .AddScheme<AuthenticationSchemeOptions, McpBearerAuthenticationHandler>(
                McpBearerAuthenticationHandler.SchemeName, _ => { })
```

Add an authorization policy the MCP endpoint will require:

```csharp
        services.AddAuthorization(options =>
        {
            // ...existing per-permission policies...
            options.AddPolicy("McpBearer", p => p
                .AddAuthenticationSchemes(McpBearerAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());
        });
```

- [ ] **Step 5: Run, verify pass** + **Step 6: Commit** `"Add opaque MCP bearer authentication scheme"`.

---

## Phase 6 — MCP server and tools

### Task 14: Add the SDK and mount the MCP endpoint

**Files:**
- Modify: `src/SluiceBase.Api/SluiceBase.Api.csproj`, `Program.cs`
- Test: manual + Task 15-17 cover tools.

- [ ] **Step 1: Add package** — Run: `dotnet add src/SluiceBase.Api package ModelContextProtocol.AspNetCore --version 1.4.0`. Expected: csproj updated.

- [ ] **Step 2: Register + map** in `Program.cs`:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<SluiceBase.Api.Mcp.Tools.DatabaseTools>();
```

```csharp
// after app.MapAllEndpoints();
app.MapMcp("/mcp").RequireAuthorization("McpBearer");
```

- [ ] **Step 3: Build** — Run: `dotnet build SluiceBase.slnx`. Expected: success.

- [ ] **Step 4: Commit** `"Mount MCP server endpoint behind bearer auth"`.

---

### Task 15: `list_databases` tool

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs`
- Test: `tests/IntegrationTests/McpToolsTests.cs`

Tools resolve the current user from DI (`ICurrentUserAccessor`, which reads the bearer-populated claim) and delegate to the shared services. They receive scoped dependencies via method injection (`IServiceProvider`/typed params — the SDK supports DI in tool methods).

- [ ] **Step 1: Failing test** — using a bearer token for a user with `query:execute` on one DB, call the MCP `list_databases` tool over the streamable-HTTP endpoint (use the `ModelContextProtocol` client in the test, or assert via the service-backed result) and assert the accessible DB appears. Keep this test focused on the tool→service wiring.

- [ ] **Step 2: Run, verify fails.**

- [ ] **Step 3: Implement**

```csharp
// src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs
using System.ComponentModel;
using ModelContextProtocol.Server;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Services;

namespace SluiceBase.Api.Mcp.Tools;

[McpServerToolType]
internal sealed class DatabaseTools
{
    [McpServerTool(Name = "list_databases")]
    [Description("List databases the authenticated user can query, grouped by server.")]
    public static async Task<object> ListDatabases(
        ICatalogService catalog, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct)
            ?? throw new InvalidOperationException("No authenticated user.");
        var result = await catalog.ListAccessibleAsync(user, ct);
        return result;
    }
}
```

- [ ] **Step 4: Run, verify pass** + **Step 5: Commit** `"Add list_databases MCP tool"`.

---

### Task 16: `get_schema` tool

**Files:** Modify `DatabaseTools.cs`; extend `McpToolsTests.cs`.

- [ ] **Step 1: Failing test** — call `get_schema` with the database id; assert schema returns and sensitive columns are flagged; assert a forbidden database yields a tool error.

- [ ] **Step 2: Run, verify fails.**

- [ ] **Step 3: Implement**

```csharp
    [McpServerTool(Name = "get_schema")]
    [Description("Return the table/column schema for a database the user can query. Sensitive columns are flagged.")]
    public static async Task<object> GetSchema(
        [Description("The database id (GUID) from list_databases.")] string databaseId,
        ISchemaService schema, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) throw new ArgumentException("databaseId must be a GUID.");
        var result = await schema.GetAnnotatedSchemaAsync(user, DatabaseId.From(g), ct);
        return result.Outcome switch
        {
            SchemaOutcome.Ok => result.Tree!,
            SchemaOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            SchemaOutcome.Forbidden => throw new InvalidOperationException("You do not have query access to this database."),
            _ => throw new InvalidOperationException(result.Error ?? "Schema error."),
        };
    }
```

- [ ] **Step 4: Run, verify pass** + **Step 5: Commit** `"Add get_schema MCP tool"`.

---

### Task 17: `run_query` tool

**Files:** Modify `DatabaseTools.cs`; extend `McpToolsTests.cs`.

- [ ] **Step 1: Failing test** — run a SELECT, assert rows return AND the persisted `QueryLog.Source == QuerySource.Mcp`; run a query hitting a blocked sensitive column, assert a tool error naming the columns and a `Blocked` log row.

- [ ] **Step 2: Run, verify fails.**

- [ ] **Step 3: Implement**

```csharp
    [McpServerTool(Name = "run_query")]
    [Description("Execute a read-only SQL query against a database the user can query. Returns columns and rows.")]
    public static async Task<object> RunQuery(
        [Description("The database id (GUID) from list_databases.")] string databaseId,
        [Description("A read-only SQL statement.")] string sql,
        IQueryService queries, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) throw new ArgumentException("databaseId must be a GUID.");
        var result = await queries.ExecuteAsync(user, DatabaseId.From(g), sql, QuerySource.Mcp, ct);
        return result.Outcome switch
        {
            QueryOutcome.Ok => result.Response!,
            QueryOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            QueryOutcome.Forbidden => throw new InvalidOperationException("You do not have query access to this database."),
            QueryOutcome.Blocked => throw new InvalidOperationException(
                "Query touches sensitive columns: " + string.Join(", ",
                    result.BlockedColumns!.Select(c => $"{c.Schema}.{c.Table}.{c.Column}"))),
            _ => throw new InvalidOperationException(result.Error ?? "Query error."),
        };
    }
```

- [ ] **Step 4: Run, verify pass** + **Step 5: Commit** `"Add run_query MCP tool"`.

---

## Phase 7 — Wiring, docs, verification

### Task 18: Feature flag

**Files:** Modify `Program.cs`.

- [ ] **Step 1:** Guard MCP registration + mapping behind `McpOptions.Enabled`:

```csharp
var mcpEnabled = builder.Configuration.GetValue($"{McpOptions.SectionName}:Enabled", true);
```

Only call `AddMcpServer()...` and `app.MapMcp(...)` / OAuth endpoint mapping when `mcpEnabled`. The OAuth endpoints and MCP endpoint should all be gated together.

- [ ] **Step 2: Build** — Run: `dotnet build SluiceBase.slnx`. Expected: success.
- [ ] **Step 3: Commit** `"Add Mcp:Enabled feature flag"`.

---

### Task 19: README — connecting Claude Code / Codex

**Files:** Modify `README.md`.

- [ ] **Step 1:** Add a "Connecting AI tools (MCP)" section documenting:
  - The endpoint: `https://your-domain/mcp`.
  - Claude Code: `claude mcp add --transport http sluicebase https://your-domain/mcp`, then `/mcp` → authenticate → the normal login page → done.
  - Codex: the equivalent remote-MCP config block pointing at the same URL.
  - That auth uses the existing OIDC provider; no separate credentials; tokens are user-scoped and revocable.
  - The optional `Mcp__Enabled`, `Mcp__AccessTokenMinutes`, `Mcp__RefreshTokenDays` env vars in the env table.

- [ ] **Step 2: Commit** `"Document MCP client setup"`.

---

### Task 20: Full backend verification

- [ ] **Step 1: Build** — Run: `dotnet build SluiceBase.slnx`. Expected: 0 warnings, 0 errors.
- [ ] **Step 2: Full test run** — Run: `dotnet test tests/IntegrationTests` and `dotnet test tests/CoverageReport.Tests`. Expected: PASS (rely on CI if a local OIDC round-trip flakes — see memory).

---

### Task 21: Regenerate CI-gated artifacts

**Files:** `src/SluiceBase.Api/openapi.json`, `src/frontend/src/api/schema.ts`.

The new OAuth/MCP endpoints may alter the OpenAPI document. CI fails if these drift.

- [ ] **Step 1: Regenerate OpenAPI** — Run a Debug build of `src/SluiceBase.Api` (the `Microsoft.Extensions.ApiDescription.Server` target writes `openapi.json`). Confirm via `git diff --stat`.
- [ ] **Step 2: Regenerate the frontend schema** — Run the frontend's schema-generation script (see `src/frontend/package.json`) to refresh `src/frontend/src/api/schema.ts`.
- [ ] **Step 3: Commit** `"Regenerate OpenAPI and frontend schema for MCP endpoints"`.

> Note: MCP-internal types are minimal-API/`Results.Json` shapes, not typed endpoints, so OpenAPI churn should be small or none. Commit whatever changes appear.

---

### Task 22: Open PR

- [ ] **Step 1:** Push `feat/mcp-server`.
- [ ] **Step 2:** Open a PR. Body uses `## Summary` with bullets only (no Test Plan section), per conventions. Summarize: MCP server, OAuth Option-B façade, opaque tokens, three read-only tools, shared-service refactor.

---

## Self-review notes

- **Spec coverage:** transport/SDK (Task 14), OAuth Option-B façade (Tasks 10-12), opaque tokens + refresh (Tasks 7, 9), bearer→identity reuse (Task 13), three tools (Tasks 15-17), shared-service refactor (Tasks 2-4), source tagging via new column not SourceRequestId (Tasks 1, 4), feature flag + config (Tasks 9, 18), docs (Task 19), single migration (Task 8), CI artifacts (Task 21). All covered.
- **Out of scope confirmed absent:** no writes/approval tool, no session-management UI, no Option-A path, no JWT signing — matches spec.
- **Type consistency:** `IssuedTokens`, `SchemaOutcome`/`SchemaResult`, `QueryOutcome`/`QueryExecutionResult`/`BlockedColumn`, `McpToken`/`McpAuthCode`/`McpOAuthClient`, `AppClaims.InternalUserIdClaim`, `TokenHasher.{Generate,Hash}` are defined once and reused consistently.
- **Known risk:** exact `ModelContextProtocol` 1.4.0 tool-DI signature (method injection vs `[FromServices]`) — verify against the SDK in Task 15; adjust the parameter attributes if the build complains. Endpoint paths for OAuth are app-internal and self-consistent.
