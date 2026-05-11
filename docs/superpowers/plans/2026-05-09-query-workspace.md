# Query Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a functional SQL query editor to `/query` with schema-driven snippet generation, syntax highlighting, backend execution with timeout and read-only safety, and full query logging.

**Architecture:** Extend `ITargetEngine` with `ExecuteQueryAsync` (implemented in `PostgresTargetEngine`), add a new `POST /api/query` minimal-API endpoint that wraps execution in a configurable timeout and logs every query to a new `query_log` table. The frontend replaces the placeholder panel with a CodeMirror 6 editor, a Run button, and a results table; clicking any table in the schema sidebar appends a column-explicit `SELECT … LIMIT 100` snippet.

**Tech Stack:** .NET 10, Npgsql, EF Core 10, Vogen (strongly-typed IDs), `@uiw/react-codemirror`, `@codemirror/lang-sql`, `@uiw/codemirror-themes-all`, Mantine 9, TanStack React Query v5, Playwright (E2E)

---

## File Map

**New — backend:**
- `src/SluiceBase.Core/Queries/QueryData.cs` — return type from `ExecuteQueryAsync`
- `src/SluiceBase.Core/Queries/QueryLogId.cs` — Vogen strongly-typed ID
- `src/SluiceBase.Core/Queries/QueryLog.cs` — entity class
- `src/SluiceBase.Core/Queries/QueryLogStatus.cs` — string constants (`success`, `error`, `timeout`)
- `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs` — EF fluent config
- `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` — `POST /api/query` handler

**Modified — backend:**
- `src/SluiceBase.Core/Targets/ITargetEngine.cs` — add `ExecuteQueryAsync`
- `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` — implement `ExecuteQueryAsync`
- `src/SluiceBase.Api/Data/AppDbContext.cs` — add `QueryLogs` DbSet
- `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` — register `QueryEndpoints`
- `src/SluiceBase.Api/appsettings.json` — add `Query:TimeoutSeconds`
- `src/SluiceBase.Api/openapi.json` — regenerated after build

**New — tests:**
- `tests/IntegrationTests/QueryEndpointTests.cs`

**Modified — frontend:**
- `src/frontend/src/api/schema.ts` — regenerated
- `src/frontend/src/api/hooks.ts` — add `useExecuteQuery`
- `src/frontend/src/routes/_authed/query.tsx` — full editor + results
- `src/frontend/e2e/query-schema.spec.ts` — add execution test

---

## Task 1: Add ExecuteQueryAsync to ITargetEngine

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- Create: `src/SluiceBase.Core/Queries/QueryData.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- Modify: `tests/IntegrationTests/TargetEngineTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/IntegrationTests/TargetEngineTests.cs` after the existing tests:

```csharp
[Fact]
public async Task TargetEngine_Postgres_ExecuteQuery_ReturnsData()
{
    var ct = TestContext.Current.CancellationToken;
    var connectionString = await factory.InitialisedApp
        .GetConnectionStringAsync("blue-appdb", ct);
    Assert.NotNull(connectionString);

    var result = await _targetEngine.ExecuteQueryAsync(
        connectionString,
        "SELECT id, email FROM public.users ORDER BY id LIMIT 5",
        ct);

    Assert.NotNull(result);
    Assert.Equal(2, result.Columns.Length);
    Assert.Equal("id", result.Columns[0]);
    Assert.Equal("email", result.Columns[1]);
    Assert.NotEmpty(result.Rows);
    Assert.All(result.Rows, row => Assert.Equal(2, row.Length));
}

[Fact]
public async Task TargetEngine_Postgres_ExecuteQuery_ReturnsEmptyRows_ForNoResults()
{
    var ct = TestContext.Current.CancellationToken;
    var connectionString = await factory.InitialisedApp
        .GetConnectionStringAsync("blue-appdb", ct);
    Assert.NotNull(connectionString);

    var result = await _targetEngine.ExecuteQueryAsync(
        connectionString,
        "SELECT id FROM public.users WHERE 1 = 0",
        ct);

    Assert.NotNull(result);
    Assert.Single(result.Columns);
    Assert.Empty(result.Rows);
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
cd /path/to/sluice-base
dotnet test tests/IntegrationTests --filter "TargetEngine_Postgres_ExecuteQuery"
```

Expected: compilation error — `ExecuteQueryAsync` does not exist.

- [ ] **Step 3: Create QueryData record**

Create `src/SluiceBase.Core/Queries/QueryData.cs`:

```csharp
namespace SluiceBase.Core.Queries;

public sealed record QueryData(string[] Columns, string?[][] Rows);
```

- [ ] **Step 4: Add ExecuteQueryAsync to ITargetEngine**

Edit `src/SluiceBase.Core/Targets/ITargetEngine.cs` — add the import and method:

```csharp
using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;

namespace SluiceBase.Core.Targets;

public interface ITargetEngine
{
    string Kind { get; }

    Task<ConnectivityResult> TestConnectionAsync(
        string connectionString,
        CancellationToken ct);

    Task<SchemaTree> GetSchemaAsync(
        string connectionString,
        CancellationToken ct);

    Task<QueryData> ExecuteQueryAsync(
        string connectionString,
        string sql,
        CancellationToken ct);
}

public sealed record ConnectivityResult(bool Ok, string? Error);
```

- [ ] **Step 5: Implement ExecuteQueryAsync in PostgresTargetEngine**

Edit `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` — add the import and method at the end of the class:

```csharp
using Npgsql;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Targets;

internal sealed class PostgresTargetEngine : ITargetEngine
{
    public string Kind => "postgres";

    public async Task<ConnectivityResult> TestConnectionAsync(
        string connectionString,
        CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return new ConnectivityResult(result is 1, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectivityResult(false, ex.Message);
        }
    }

    public async Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct)
    {
        const string sql = """
                           SELECT table_schema, table_name, column_name, data_type, is_nullable
                           FROM information_schema.columns
                           WHERE table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY table_schema, table_name, ordinal_position;
                           """;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<(string Schema, string Table, string Column, string DataType, bool IsNullable)>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4) == "YES"
            ));
        }

        var schemas = rows
            .GroupBy(r => r.Schema)
            .Select(sg => new SchemaInfo(
                sg.Key,
                [
                    .. sg.GroupBy(r => r.Table)
                        .Select(tg => new TableInfo(
                            tg.Key,
                            [.. tg.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable))]))
                ]))
            .ToList();

        return new SchemaTree(schemas);
    }

    public async Task<QueryData> ExecuteQueryAsync(
        string connectionString,
        string sql,
        CancellationToken ct)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx);
        await setReadOnly.ExecuteNonQueryAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();

        var rows = new List<string?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new string?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
            }
            rows.Add(row);
        }

        await tx.CommitAsync(ct);
        return new QueryData(columns, rows.ToArray());
    }
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

```bash
dotnet test tests/IntegrationTests --filter "TargetEngine_Postgres_ExecuteQuery"
```

Expected: both tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Queries/QueryData.cs \
        src/SluiceBase.Core/Targets/ITargetEngine.cs \
        src/SluiceBase.Api/Targets/PostgresTargetEngine.cs \
        tests/IntegrationTests/TargetEngineTests.cs
git commit -m "feat: add ExecuteQueryAsync to ITargetEngine with Postgres implementation"
```

---

## Task 2: QueryLog entity and migration

**Files:**
- Create: `src/SluiceBase.Core/Queries/QueryLogId.cs`
- Create: `src/SluiceBase.Core/Queries/QueryLog.cs`
- Create: `src/SluiceBase.Core/Queries/QueryLogStatus.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`
- Generate: `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddQueryLog.cs`

- [ ] **Step 1: Create QueryLogId**

Create `src/SluiceBase.Core/Queries/QueryLogId.cs`:

```csharp
using Vogen;

namespace SluiceBase.Core.Queries;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct QueryLogId;
```

- [ ] **Step 2: Create QueryLogStatus**

Create `src/SluiceBase.Core/Queries/QueryLogStatus.cs`:

```csharp
namespace SluiceBase.Core.Queries;

public static class QueryLogStatus
{
    public const string Success = "success";
    public const string Error = "error";
    public const string Timeout = "timeout";
}
```

- [ ] **Step 3: Create QueryLog entity**

Create `src/SluiceBase.Core/Queries/QueryLog.cs`:

```csharp
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Queries;

public sealed class QueryLog
{
#pragma warning disable CS8618
    private QueryLog() { }
#pragma warning restore CS8618

    public QueryLogId Id { get; private set; }
    public UserId? UserId { get; private set; }
    public ServerId? ServerId { get; private set; }
    public string QueryText { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset ExecutedAt { get; private set; }
    public int? DurationMs { get; private set; }
    public int? RowCount { get; private set; }
    public string? Error { get; private set; }

    public static QueryLog Create(
        UserId? userId,
        ServerId? serverId,
        string queryText,
        string status,
        DateTimeOffset executedAt,
        int? durationMs,
        int? rowCount,
        string? error) => new()
    {
        Id = QueryLogId.FromNewVersion7Guid(),
        UserId = userId,
        ServerId = serverId,
        QueryText = queryText,
        Status = status,
        ExecutedAt = executedAt,
        DurationMs = durationMs,
        RowCount = rowCount,
        Error = error,
    };
}
```

- [ ] **Step 4: Create EF configuration**

Create `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class QueryLogConfiguration : IEntityTypeConfiguration<QueryLog>
{
    public void Configure(EntityTypeBuilder<QueryLog> builder)
    {
        builder.ToTable("query_log");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.QueryText).IsRequired();
        builder.Property(q => q.Status).HasMaxLength(16).IsRequired();

        builder.HasOne<User>().WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Server>().WithMany()
            .HasForeignKey(q => q.ServerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 5: Add QueryLogs DbSet to AppDbContext**

Edit `src/SluiceBase.Api/Data/AppDbContext.cs` — add one line after `Servers`:

```csharp
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SluiceBase.Api.Data.Converters;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermissionMap> UserPermissions => Set<UserPermissionMap>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<QueryLog> QueryLogs => Set<QueryLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove<TableNameFromDbSetConvention>();

        configurationBuilder.RegisterAllInVogenEfCoreConverters();
    }
}
```

- [ ] **Step 6: Generate the migration**

Run from the repo root:

```bash
dotnet ef migrations add AddQueryLog \
  --project src/SluiceBase.Api \
  --startup-project src/SluiceBase.Api
```

Verify the generated `Up` method in the new migration file creates a `query_log` table with columns: `id` (uuid PK), `user_id` (uuid nullable FK → `user`), `server_id` (uuid nullable FK → `server`), `query_text` (text), `status` (varchar 16), `executed_at` (timestamptz), `duration_ms` (int nullable), `row_count` (int nullable), `error` (text nullable). If the generated migration looks materially different, check that the configuration was picked up correctly.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Queries/ \
        src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs \
        src/SluiceBase.Api/Data/AppDbContext.cs \
        src/SluiceBase.Api/Data/Migrations/
git commit -m "feat: add QueryLog entity and migration"
```

---

## Task 3: POST /api/query endpoint

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`
- Modify: `src/SluiceBase.Api/appsettings.json`
- Create: `tests/IntegrationTests/QueryEndpointTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/IntegrationTests/QueryEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class QueryEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(AuthenticatedSession session, string serverId)> AuthorizedSessionWithBlueServerAsync(
        CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        using var grantQuery = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grantQuery, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"qry-{Guid.NewGuid():N}"[..24];
        using var createReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(
                serverName, "postgres",
                blueBuilder.Host!, blueBuilder.Port, "appdb",
                "reader_blue", "reader_blue"));
        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var server = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        return (session, server!.Id.Value.ToString());
    }

    [Fact]
    public async Task PostQuery_ReturnsData_ForValidSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { serverId, sql = "SELECT id, email FROM public.users ORDER BY id LIMIT 5" });

        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(result);
        Assert.Null(result.Error);
        Assert.NotNull(result.Columns);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("id", result.Columns[0]);
        Assert.Equal("email", result.Columns[1]);
        Assert.NotNull(result.Rows);
        Assert.NotEmpty(result.Rows);
        Assert.True(result.DurationMs >= 0);
        Assert.True(result.RowCount > 0);
    }

    [Fact]
    public async Task PostQuery_ReturnsError_ForInvalidSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { serverId, sql = "SELECT naem FROM public.users" });

        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Null(result.Columns);
    }

    [Fact]
    public async Task PostQuery_Returns404_ForUnknownServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { serverId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PostQuery_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/query");
        req.Content = JsonContent.Create(new { serverId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostQuery_Returns403_ForBob()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { serverId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/IntegrationTests --filter "QueryEndpoint"
```

Expected: compilation error — `QueryEndpoints` and `QueryEndpoints.QueryResponse` do not exist.

- [ ] **Step 3: Create QueryEndpoints**

Create `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class QueryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", ExecuteQuery)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("ExecuteQuery");
    }

    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>>> ExecuteQuery(
        QueryRequest request,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var startedAt = timeProvider.GetUtcNow();

        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.ServerId, ct);
        if (server is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(server.ReadUsername))
            return TypedResults.BadRequest("Server has no read-only credentials configured.");

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        QueryResponse response;
        string logStatus;
        int? rowCount = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(server.Id, CredentialKind.Read, ct);

            var data = await targetEngine.ExecuteQueryAsync(
                connectionString, request.Sql, linkedCts.Token);

            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            rowCount = data.Rows.Length;
            logStatus = QueryLogStatus.Success;
            response = new QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Timeout;
            response = new QueryResponse(null, null, 0, durationMs,
                $"Query timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryResponse(null, null, 0, durationMs, ex.Message);
        }

        var log = QueryLog.Create(
            userId: user?.Id,
            serverId: server.Id,
            queryText: request.Sql,
            status: logStatus,
            executedAt: startedAt,
            durationMs: response.DurationMs,
            rowCount: rowCount,
            error: response.Error);

        db.QueryLogs.Add(log);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(response);
    }

    public sealed record QueryRequest(ServerId ServerId, string Sql);

    public sealed record QueryResponse(
        string[]? Columns,
        string?[][]? Rows,
        int RowCount,
        int DurationMs,
        string? Error);
}
```

- [ ] **Step 4: Register QueryEndpoints in EndpointMapper**

Edit `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`:

```csharp
namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this WebApplication app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        PermissionEndpoints.Map(app);
        ServerEndpoints.Map(app);
        SchemaEndpoints.Map(app);
        QueryEndpoints.Map(app);

        if (app.Environment.IsDevelopment())
        {
            DevelopmentEndpoints.Map(app);
        }

        return app;
    }
}
```

- [ ] **Step 5: Add Query:TimeoutSeconds to appsettings.json**

Edit `src/SluiceBase.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Migrations": {
    "AutoApply": false
  },
  "Oidc": {
    "Authority": "",
    "ClientId": "",
    "ClientSecret": ""
  },
  "Frontend": {
    "BaseUrl": ""
  },
  "Query": {
    "TimeoutSeconds": 30
  }
}
```

- [ ] **Step 6: Regenerate openapi.json**

Build the API project (the MSBuild targets regenerate openapi.json automatically on build):

```bash
dotnet build src/SluiceBase.Api
```

Verify `src/SluiceBase.Api/openapi.json` now contains a `/api/query` POST operation with `QueryRequest` and `QueryResponse` schemas.

- [ ] **Step 7: Run the integration tests**

```bash
dotnet test tests/IntegrationTests --filter "QueryEndpoint"
```

Expected: all 5 tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/QueryEndpoints.cs \
        src/SluiceBase.Api/Endpoints/EndpointMapper.cs \
        src/SluiceBase.Api/appsettings.json \
        src/SluiceBase.Api/openapi.json \
        tests/IntegrationTests/QueryEndpointTests.cs
git commit -m "feat: add POST /api/query endpoint with timeout and query logging"
```

---

## Task 4: Install CodeMirror npm packages

**Files:**
- Modify: `src/frontend/package.json` (via npm install)

- [ ] **Step 1: Install packages**

```bash
cd src/frontend
npm install @uiw/react-codemirror @codemirror/lang-sql @uiw/codemirror-themes-all
```

- [ ] **Step 2: Verify the packages appear in package.json dependencies**

```bash
grep -E "react-codemirror|lang-sql|codemirror-themes" src/frontend/package.json
```

Expected output (versions may vary):
```
"@codemirror/lang-sql": "^6.x.x",
"@uiw/codemirror-themes-all": "^4.x.x",
"@uiw/react-codemirror": "^4.x.x",
```

- [ ] **Step 3: Commit**

```bash
git add src/frontend/package.json src/frontend/package-lock.json
git commit -m "chore: add CodeMirror 6 packages for SQL editor"
```

---

## Task 5: Frontend hook and schema types

**Files:**
- Modify: `src/frontend/src/api/schema.ts` (regenerated)
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Regenerate schema.ts from the updated openapi.json**

```bash
cd src/frontend
npm run gen:api
```

Verify `src/frontend/src/api/schema.ts` now contains a `/api/query` path with `post` operation.

- [ ] **Step 2: Add useExecuteQuery hook to hooks.ts**

Add to the end of `src/frontend/src/api/hooks.ts`:

```typescript
// ── Query execution ───────────────────────────────────────────────────────

export type ExecuteQueryRequest =
  paths["/api/query"]["post"]["requestBody"]["content"]["application/json"];
export type ExecuteQueryResponse =
  paths["/api/query"]["post"]["responses"][200]["content"]["application/json"];

export function useExecuteQuery() {
  return useMutation({
    mutationFn: (body: ExecuteQueryRequest) =>
      apiRequest<ExecuteQueryRequest, ExecuteQueryResponse>("/api/query", {
        method: "POST",
        body,
      }),
  });
}
```

- [ ] **Step 3: Verify TypeScript compiles cleanly**

```bash
cd src/frontend
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/api/schema.ts src/frontend/src/api/hooks.ts
git commit -m "feat: add useExecuteQuery hook and regenerate API schema types"
```

---

## Task 6: Query editor UI

**Files:**
- Modify: `src/frontend/src/routes/_authed/query.tsx`

- [ ] **Step 1: Replace query.tsx with the full implementation**

Replace the entire contents of `src/frontend/src/routes/_authed/query.tsx`:

```tsx
import {
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  NavLink,
  ScrollArea,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  useMantineColorScheme,
} from "@mantine/core";
import { IconChevronDown, IconChevronRight, IconDatabase, IconPlayerPlay, IconTable } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useCallback, useRef, useState } from "react";
import { meQueryOptions, useExecuteQuery, useSchema, useServers, type ExecuteQueryResponse } from "@/api/hooks";
import CodeMirror, { type ReactCodeMirrorRef } from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubLight, githubDark } from "@uiw/codemirror-themes-all";
import { keymap } from "@codemirror/view";
import { Prec } from "@codemirror/state";

export const Route = createFileRoute("/_authed/query")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryPage,
});

function QueryPage() {
  const servers = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const schema = useSchema(selectedServerId);
  const [editorContent, setEditorContent] = useState("");
  const editorRef = useRef<ReactCodeMirrorRef>(null);
  const executeQuery = useExecuteQuery();
  const { colorScheme } = useMantineColorScheme();

  const serverOptions = (servers.data?.servers ?? []).map((s) => ({
    value: s.id,
    label: s.name,
  }));

  const handleTableClick = useCallback(
    (schemaName: string, tableName: string, columns: { name: string }[]) => {
      const colList = columns.map((c) => c.name).join(", ");
      const snippet = `SELECT ${colList}\nFROM ${schemaName}.${tableName}\nLIMIT 100;\n`;
      setEditorContent((prev) => (prev.trimEnd() === "" ? snippet : `${prev.trimEnd()}\n\n${snippet}`));
    },
    [],
  );

  const runKeymap = Prec.highest(
    keymap.of([
      {
        key: "Ctrl-Enter",
        mac: "Cmd-Enter",
        run: () => {
          if (selectedServerId && editorContent.trim()) {
            executeQuery.mutate({ serverId: selectedServerId, sql: editorContent });
          }
          return true;
        },
      },
    ]),
  );

  const handleRun = () => {
    if (selectedServerId && editorContent.trim()) {
      executeQuery.mutate({ serverId: selectedServerId, sql: editorContent });
    }
  };

  return (
    <Flex h="calc(100vh - 90px)" style={{ overflow: "hidden" }}>
      <Box
        w={280}
        style={{
          borderRight: "1px solid var(--mantine-color-default-border)",
          overflow: "auto",
          flexShrink: 0,
        }}
      >
        <Stack gap={0} p="xs">
          <Select
            placeholder="Select a server"
            data={serverOptions}
            value={selectedServerId}
            onChange={setSelectedServerId}
            mb="xs"
            size="sm"
          />
          <SchemaSidebar schema={schema} onTableClick={handleTableClick} />
        </Stack>
      </Box>

      <Box flex={1} style={{ overflowY: "auto" }}>
        <Stack gap={0} p="md">
          <Box
            style={{
              border: "1px solid var(--mantine-color-default-border)",
              borderRadius: "var(--mantine-radius-sm)",
              overflow: "hidden",
            }}
          >
            <CodeMirror
              ref={editorRef}
              value={editorContent}
              onChange={setEditorContent}
              extensions={[sql(), runKeymap]}
              theme={colorScheme === "dark" ? githubDark : githubLight}
              height="300px"
              basicSetup={{ lineNumbers: true, foldGutter: false }}
            />
          </Box>

          <Group mt="xs" gap="xs">
            <Button
              leftSection={<IconPlayerPlay size={14} />}
              size="sm"
              onClick={handleRun}
              loading={executeQuery.isPending}
              disabled={!selectedServerId || !editorContent.trim()}
            >
              Run
            </Button>
            {!selectedServerId && (
              <Text size="xs" c="dimmed">
                Select a server to run queries
              </Text>
            )}
          </Group>

          <QueryResults
            result={executeQuery.data ?? null}
            isPending={executeQuery.isPending}
            isError={executeQuery.isError}
          />
        </Stack>
      </Box>
    </Flex>
  );
}

function QueryResults({
  result,
  isPending,
  isError,
}: {
  result: ExecuteQueryResponse | null;
  isPending: boolean;
  isError: boolean;
}) {
  if (isPending) {
    return (
      <Stack mt="md" gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={28} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (isError) {
    return (
      <Alert color="red" title="Request failed" mt="md">
        Could not reach the server. Check your connection and try again.
      </Alert>
    );
  }

  if (!result) {
    return (
      <Text mt="md" size="sm" c="dimmed">
        Run a query to see results.
      </Text>
    );
  }

  if (result.error) {
    return (
      <Stack mt="md" gap="xs">
        <Text size="xs" c="dimmed">
          Error · {result.durationMs} ms
        </Text>
        <Alert color="red" title="Query error">
          {result.error}
        </Alert>
      </Stack>
    );
  }

  const columns = result.columns ?? [];
  const rows = result.rows ?? [];

  return (
    <Stack mt="md" gap="xs">
      <Text size="xs" c="dimmed">
        {result.rowCount} {result.rowCount === 1 ? "row" : "rows"} · {result.durationMs} ms
      </Text>
      <ScrollArea type="auto">
        <Table striped withTableBorder withColumnBorders fz="xs" style={{ whiteSpace: "nowrap" }}>
          <Table.Thead>
            <Table.Tr>
              {columns.map((col) => (
                <Table.Th key={col}>{col}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row, i) => (
              <Table.Tr key={i}>
                {row.map((cell, j) => (
                  <Table.Td key={j}>
                    {cell === null ? (
                      <Text size="xs" c="dimmed" fs="italic">
                        null
                      </Text>
                    ) : (
                      cell
                    )}
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>
    </Stack>
  );
}

type TableClickHandler = (schemaName: string, tableName: string, columns: { name: string }[]) => void;

function SchemaSidebar({
  schema,
  onTableClick,
}: {
  schema: ReturnType<typeof useSchema>;
  onTableClick: TableClickHandler;
}) {
  const [expandedSchemas, setExpandedSchemas] = useState<Set<string>>(new Set());
  const [expandedTables, setExpandedTables] = useState<Set<string>>(new Set());

  if (schema.isFetching) {
    return (
      <Stack gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (schema.isError) {
    return (
      <Alert color="red" title="Schema load failed" mt="xs">
        Could not connect to the server.
      </Alert>
    );
  }

  if (!schema.data) {
    return (
      <Text size="sm" c="dimmed" p="xs">
        Select a server to browse its schema.
      </Text>
    );
  }

  function toggleSchema(name: string) {
    setExpandedSchemas((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }

  function toggleTable(key: string) {
    setExpandedTables((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  return (
    <Stack gap={0}>
      {schema.data.schemas.map((s) => {
        const schemaExpanded = expandedSchemas.has(s.name);
        return (
          <div key={s.name}>
            <NavLink
              label={s.name}
              leftSection={<IconDatabase size={14} />}
              rightSection={
                schemaExpanded ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />
              }
              onClick={() => toggleSchema(s.name)}
              active={false}
            />
            {schemaExpanded &&
              s.tables.map((t) => {
                const tableKey = `${s.name}.${t.name}`;
                const tableExpanded = expandedTables.has(tableKey);
                return (
                  <div key={tableKey}>
                    <NavLink
                      label={t.name}
                      leftSection={<IconTable size={14} />}
                      rightSection={
                        tableExpanded ? (
                          <IconChevronDown size={12} />
                        ) : (
                          <IconChevronRight size={12} />
                        )
                      }
                      onClick={() => {
                        toggleTable(tableKey);
                        onTableClick(s.name, t.name, t.columns);
                      }}
                      pl="lg"
                      active={false}
                    />
                    {tableExpanded && (
                      <Stack
                        gap={0}
                        pl="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-xs))"
                      >
                        {t.columns.map((c) => (
                          <Group key={c.name} gap="xs" px="xs" py={2} wrap="nowrap">
                            <Text size="xs" style={{ minWidth: 0 }}>
                              {c.name}
                            </Text>
                            <Code fz="xs">{c.dataType}</Code>
                            {c.isNullable && (
                              <Text size="xs" c="dimmed">
                                null
                              </Text>
                            )}
                          </Group>
                        ))}
                      </Stack>
                    )}
                  </div>
                );
              })}
          </div>
        );
      })}
    </Stack>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles cleanly**

```bash
cd src/frontend
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Start the dev stack and test manually in the browser**

Start the Aspire dev stack (from the repo root) and the frontend dev server:

```bash
# Terminal 1
dotnet run --project src/AppHost

# Terminal 2
cd src/frontend && npm run dev
```

Open `http://localhost:5173/query` as a user with `query:execute`. Verify:
1. Schema sidebar still works — select server, expand schema, see tables and columns
2. Click a table — a `SELECT col1, col2 … FROM schema.table LIMIT 100;` snippet appears in the editor
3. Click another table — the new snippet is appended below the existing text (not replaced)
4. Click Run (or press Ctrl+Enter / Cmd+Enter) — results table appears below with row count and duration
5. Run an invalid SQL (`SELECT naem FROM public.users`) — red error alert appears with the error message
6. Scroll down when results are long — the full page scrolls, editor stays visible at top

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/routes/_authed/query.tsx
git commit -m "feat: implement query editor with CodeMirror 6, snippet generation, and results table"
```

---

## Task 7: E2E test for query execution

**Files:**
- Modify: `src/frontend/e2e/query-schema.spec.ts`

- [ ] **Step 1: Add query execution test**

Append to `src/frontend/e2e/query-schema.spec.ts`:

```typescript
test("alice can run a query and see results", async ({ page }) => {
  // Sign in as alice
  await page.goto("http://localhost:5173");
  await page.waitForURL(/realms\/sluicebase/);
  await page.fill('[name="username"]', "alice");
  await page.fill('[name="password"]', "dev");
  await page.click('[type="submit"]');
  await page.waitForURL("http://localhost:5173/");

  // Grant query:execute if not already set
  await page.getByRole("link", { name: "Permission" }).click();
  await expect(page).toHaveURL("/permission");
  const aliceRow = page.getByRole("row").filter({ hasText: "alice@example.com" });
  const querySwitch = aliceRow.getByRole("switch", { name: /Run read queries/i });
  if (!(await querySwitch.isChecked())) {
    await querySwitch.click({ force: true });
    await page.reload({ waitUntil: "domcontentloaded" });
  }

  // Navigate to /query and select a server
  await page.goto("http://localhost:5173/query");
  await page.getByPlaceholder("Select a server").click({ force: true });
  await page.getByRole("option", { name: "Blue" }).click();

  // Wait for schema to load, then click users table to generate snippet
  await expect(page.getByText("public")).toBeVisible({ timeout: 10_000 });
  await page.getByText("public").click();
  await page.getByText("users").first().click();

  // The editor should now contain a SELECT snippet
  const editorContent = await page.locator(".cm-content").textContent();
  expect(editorContent).toContain("SELECT");
  expect(editorContent).toContain("users");
  expect(editorContent).toContain("LIMIT 100");

  // Click Run
  await page.getByRole("button", { name: /run/i }).click();

  // Results table should appear
  await expect(page.getByRole("table")).toBeVisible({ timeout: 15_000 });

  // Status line should show row count and duration
  await expect(page.getByText(/rows? · \d+ ms/)).toBeVisible();
});

test("alice sees error message for invalid SQL", async ({ page }) => {
  // Sign in as alice (query:execute assumed already granted from previous test in session)
  await page.goto("http://localhost:5173");
  await page.waitForURL(/realms\/sluicebase/);
  await page.fill('[name="username"]', "alice");
  await page.fill('[name="password"]', "dev");
  await page.click('[type="submit"]');
  await page.waitForURL("http://localhost:5173/");

  // Grant query:execute if needed
  await page.getByRole("link", { name: "Permission" }).click();
  const aliceRow = page.getByRole("row").filter({ hasText: "alice@example.com" });
  const querySwitch = aliceRow.getByRole("switch", { name: /Run read queries/i });
  if (!(await querySwitch.isChecked())) {
    await querySwitch.click({ force: true });
    await page.reload({ waitUntil: "domcontentloaded" });
  }

  await page.goto("http://localhost:5173/query");
  await page.getByPlaceholder("Select a server").click({ force: true });
  await page.getByRole("option", { name: "Blue" }).click();
  await expect(page.getByText("public")).toBeVisible({ timeout: 10_000 });

  // Type invalid SQL directly into the editor
  await page.locator(".cm-content").click();
  await page.keyboard.type("SELECT naem FROM public.users LIMIT 5");

  await page.getByRole("button", { name: /run/i }).click();

  // Error alert should appear
  await expect(page.getByRole("alert")).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/error/i, { exact: false })).toBeVisible();
});
```

- [ ] **Step 2: Run the E2E tests**

Make sure the Aspire dev stack is running, then:

```bash
cd src/frontend
npm run test:e2e
```

Expected: all E2E tests pass (including the two new ones and the existing schema browser test).

- [ ] **Step 3: Commit**

```bash
git add src/frontend/e2e/query-schema.spec.ts
git commit -m "test(e2e): add query execution and error handling Playwright specs"
```
