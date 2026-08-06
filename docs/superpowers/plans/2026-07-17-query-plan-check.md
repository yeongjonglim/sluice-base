# Query Plan Check & Performance Inspection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let playground users preview a query's estimated plan/cost before running (safety advisory) and deliberately inspect a specific query's plan and real timings (performance), for PostgreSQL.

**Architecture:** A new `ExplainAsync` on the `ITargetEngine` abstraction (Postgres impl runs `EXPLAIN [ANALYZE] (FORMAT JSON)` inside a rolled-back read-only transaction). `QueryService` gains an `ExplainAsync` that reuses the existing permission + sensitive-column checks and is exposed at `POST /api/query/explain`. A best-effort estimate is also attached inline to normal `/api/query` responses. The playground gets an Explain split-button and a Plan view (summary badges + collapsible raw plan), plus an advisory strip on normal runs. Everything is session-only — no persistence, no migration.

**Tech Stack:** .NET 10 minimal APIs, Npgsql, `System.Text.Json`; React + TypeScript + Mantine + TanStack Query; Vitest + Testing Library; xUnit + Aspire Testing (Testcontainers).

## Global Constraints

- Branch: work is on `feat/query-plan-check` (already created). Never commit to `main`.
- Commit messages: single subject line, no body.
- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Abstract DB-specific operations behind `ITargetEngine`; never hard-code Npgsql outside the engine.
- Analyzer warnings are **build errors** — verify with real Debug builds, never `--no-build`.
- After any API contract change: regenerate `openapi.json` (Debug `dotnet build` of `SluiceBase.Api`) then `schema.ts` (`npm run gen:api`). CI gates both.
- Run `npm run lint` in `src/frontend` after each frontend task (react-hooks / set-state-in-effect are errors Vitest won't catch).
- No threshold / no blocking logic in this feature — the advisory is informational only.
- Preserve existing comments unless factually wrong.

---

### Task 1: Core plan types + Postgres plan-summary parser

**Files:**
- Create: `src/SluiceBase.Core/Queries/QueryPlan.cs`
- Create: `src/SluiceBase.Api/Queries/PostgresPlanParser.cs`
- Test: `tests/SluiceBase.Api.Tests/PostgresPlanParserTests.cs`

**Interfaces:**
- Produces: `QueryPlan(string PlanJson, QueryPlanSummary Summary)` and `QueryPlanSummary(double TotalCost, double EstimatedRows, string RootNode, bool HasSeqScan, double? ActualTotalMs)` in namespace `SluiceBase.Core.Queries`; `PostgresPlanParser.Parse(string planJson) : QueryPlanSummary` in namespace `SluiceBase.Api.Queries`.

- [ ] **Step 1: Write the Core records**

Create `src/SluiceBase.Core/Queries/QueryPlan.cs`:

```csharp
namespace SluiceBase.Core.Queries;

public sealed record QueryPlan(string PlanJson, QueryPlanSummary Summary);

public sealed record QueryPlanSummary(
    double TotalCost,
    double EstimatedRows,
    string RootNode,
    bool HasSeqScan,
    double? ActualTotalMs);
```

- [ ] **Step 2: Write the failing parser test**

Create `tests/SluiceBase.Api.Tests/PostgresPlanParserTests.cs`:

```csharp
using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public sealed class PostgresPlanParserTests
{
    private const string EstimateJson = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Total Cost":123.45,"Plan Rows":1000}}]
        """;

    private const string NestedNoSeqScanJson = """
        [{"Plan":{"Node Type":"Aggregate","Total Cost":50.0,"Plan Rows":1,
          "Plans":[{"Node Type":"Index Scan","Total Cost":40.0,"Plan Rows":10}]}}]
        """;

    private const string AnalyzeJson = """
        [{"Plan":{"Node Type":"Index Scan","Total Cost":8.3,"Plan Rows":1,"Actual Total Time":0.5},
          "Planning Time":0.2,"Execution Time":1.75}]
        """;

    [Fact]
    public void Parse_Estimate_ExtractsCostRowsRootAndSeqScan()
    {
        var summary = PostgresPlanParser.Parse(EstimateJson);

        Assert.Equal(123.45, summary.TotalCost);
        Assert.Equal(1000, summary.EstimatedRows);
        Assert.Equal("Seq Scan", summary.RootNode);
        Assert.True(summary.HasSeqScan);
        Assert.Null(summary.ActualTotalMs);
    }

    [Fact]
    public void Parse_NestedPlanWithoutSeqScan_HasSeqScanFalse()
    {
        var summary = PostgresPlanParser.Parse(NestedNoSeqScanJson);

        Assert.False(summary.HasSeqScan);
        Assert.Equal("Aggregate", summary.RootNode);
    }

    [Fact]
    public void Parse_Analyze_PopulatesActualTotalMs()
    {
        var summary = PostgresPlanParser.Parse(AnalyzeJson);

        Assert.Equal(1.75, summary.ActualTotalMs);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/SluiceBase.Api.Tests --filter PostgresPlanParserTests`
Expected: FAIL — `PostgresPlanParser` does not exist (compile error).

- [ ] **Step 4: Implement the parser**

Create `src/SluiceBase.Api/Queries/PostgresPlanParser.cs`:

```csharp
using System.Text.Json;
using SluiceBase.Core.Queries;

namespace SluiceBase.Api.Queries;

// Parses the single JSON document produced by `EXPLAIN (FORMAT JSON ...)`.
// The document is an array with one element: { "Plan": {...}, "Execution Time": n? }.
internal static class PostgresPlanParser
{
    public static QueryPlanSummary Parse(string planJson)
    {
        using var doc = JsonDocument.Parse(planJson);
        var root = doc.RootElement[0];
        var plan = root.GetProperty("Plan");

        var totalCost = plan.TryGetProperty("Total Cost", out var tc) ? tc.GetDouble() : 0;
        var estimatedRows = plan.TryGetProperty("Plan Rows", out var pr) ? pr.GetDouble() : 0;
        var rootNode = plan.TryGetProperty("Node Type", out var nt) ? nt.GetString() ?? "" : "";
        var hasSeqScan = HasSeqScan(plan);

        // "Execution Time" is only present with ANALYZE.
        double? actualTotalMs = root.TryGetProperty("Execution Time", out var et)
            ? et.GetDouble()
            : null;

        return new QueryPlanSummary(totalCost, estimatedRows, rootNode, hasSeqScan, actualTotalMs);
    }

    private static bool HasSeqScan(JsonElement node)
    {
        if (node.TryGetProperty("Node Type", out var nt) && nt.GetString() == "Seq Scan")
        {
            return true;
        }

        if (node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in plans.EnumerateArray())
            {
                if (HasSeqScan(child))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SluiceBase.Api.Tests --filter PostgresPlanParserTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Core/Queries/QueryPlan.cs src/SluiceBase.Api/Queries/PostgresPlanParser.cs tests/SluiceBase.Api.Tests/PostgresPlanParserTests.cs
git commit -m "Add query plan summary types and Postgres plan parser"
```

---

### Task 2: `ExplainAsync` on `ITargetEngine` + Postgres implementation

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- Test: `tests/IntegrationTests/TargetEngineTests.cs`

**Interfaces:**
- Consumes: `PostgresPlanParser.Parse` (Task 1), `QueryPlan`/`QueryPlanSummary` (Task 1).
- Produces: `ITargetEngine.ExplainAsync(string connectionString, string sql, bool analyze, CancellationToken ct) : Task<QueryPlan>`.

- [ ] **Step 1: Add the interface method (build will break until implemented)**

In `src/SluiceBase.Core/Targets/ITargetEngine.cs`, add inside the `ITargetEngine` interface, after `ExecuteQueryAsync`:

```csharp
    Task<QueryPlan> ExplainAsync(
        string connectionString,
        string sql,
        bool analyze,
        CancellationToken ct);
```

- [ ] **Step 2: Write the failing integration test**

In `tests/IntegrationTests/TargetEngineTests.cs`, add these tests inside the class:

```csharp
    [Fact]
    public async Task TargetEngine_Postgres_Explain_Estimate_ReturnsCostAndRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var plan = await _targetEngine.ExplainAsync(
            connectionString, "SELECT * FROM users", analyze: false, ct);

        Assert.NotEmpty(plan.PlanJson);
        Assert.True(plan.Summary.TotalCost > 0);
        Assert.Null(plan.Summary.ActualTotalMs);
    }

    [Fact]
    public async Task TargetEngine_Postgres_Explain_Analyze_PopulatesActualTime()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var plan = await _targetEngine.ExplainAsync(
            connectionString, "SELECT * FROM users", analyze: true, ct);

        Assert.NotNull(plan.Summary.ActualTotalMs);
    }

    [Fact]
    public async Task TargetEngine_Postgres_Explain_Analyze_WriteIsBlockedByReadOnlyTx()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        // ANALYZE executes the statement; the read-only transaction must reject a write
        // rather than mutate data.
        await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() =>
            _targetEngine.ExplainAsync(
                connectionString,
                "UPDATE users SET email = email",
                analyze: true,
                ct));
    }
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter TargetEngine_Postgres_Explain`
Expected: FAIL — `PostgresTargetEngine` does not implement `ExplainAsync` (compile error), or `NotImplementedException`.

- [ ] **Step 4: Implement `ExplainAsync` in the Postgres engine**

In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, add `using SluiceBase.Api.Queries;` to the usings, then add this method (place it next to `ExecuteQueryAsync`):

```csharp
    public async Task<QueryPlan> ExplainAsync(
        string connectionString, string sql, bool analyze, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Same read-only guard as ExecuteQueryAsync. With ANALYZE the statement actually
        // runs, so the read-only transaction is what stops a write from mutating data;
        // we always roll back so nothing is committed either way.
        await using (var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
        {
            await setReadOnly.ExecuteNonQueryAsync(ct);
        }

        var options = analyze ? "ANALYZE true, BUFFERS true, FORMAT JSON" : "FORMAT JSON";
        var explainSql = $"EXPLAIN ({options}) {sql}";

        string planJson;
        await using (var cmd = new NpgsqlCommand(explainSql, conn, tx))
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            planJson = result as string ?? result?.ToString() ?? "[]";
        }

        await tx.RollbackAsync(ct);

        return new QueryPlan(planJson, PostgresPlanParser.Parse(planJson));
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/IntegrationTests --filter TargetEngine_Postgres_Explain`
Expected: PASS (3 tests).

- [ ] **Step 6: Full build to confirm no analyzer/build errors**

Run: `dotnet build src/SluiceBase.Api`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Targets/ITargetEngine.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/TargetEngineTests.cs
git commit -m "Add ExplainAsync to target engine and implement for Postgres"
```

---

### Task 3: `QueryService.ExplainAsync` + `POST /api/query/explain`

**Files:**
- Modify: `src/SluiceBase.Api/Services/IQueryService.cs`
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Modify: `src/SluiceBase.Api/openapi.json` (regenerated) and `src/frontend/src/api/schema.ts` (regenerated)
- Test: `tests/IntegrationTests/QueryExplainEndpointTests.cs`

**Interfaces:**
- Consumes: `ITargetEngine.ExplainAsync` (Task 2), existing `IAccessResolver`, `SqlColumnChecker`.
- Produces: `IQueryService.ExplainAsync(User user, DatabaseId databaseId, string sql, bool analyze, CancellationToken ct) : Task<QueryExplainResult>`; `QueryExplainResult(QueryOutcome Outcome, QueryPlan? Plan, IReadOnlyList<BlockedColumn>? BlockedColumns, string? Error)`; endpoint `POST /api/query/explain` with body `ExplainRequest(DatabaseId DatabaseId, string Sql, bool Analyze)` → `QueryPlanResponse(string PlanJson, QueryPlanSummary Summary)`.

This task extracts the shared permission + sensitive-column logic out of `ExecuteAsync` into a private `CheckAccessAsync` helper (DRY) so both `ExecuteAsync` and the new `ExplainAsync` use it. Explain does **not** write a `QueryLog` (session-only feature); only `ExecuteAsync` keeps its existing Blocked-logging behaviour.

- [ ] **Step 1: Write the failing endpoint test**

Create `tests/IntegrationTests/QueryExplainEndpointTests.cs`. It reuses the same helper style as `QueryServiceTests` — copy the `MutationRequest` helper and an Alice-with-blue-server setup. Because that setup already exists in `QueryServiceTests.AliceWithBlueServerAsync`, factor it: make `QueryServiceTests.AliceWithBlueServerAsync` and its `MutationRequest` `internal static` so this test can call them, OR duplicate the two helpers here. Prefer duplication only if making them static proves awkward; the helpers are short.

```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Api.Endpoints;

namespace IntegrationTests;

public sealed class QueryExplainEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    [Fact]
    public async Task Explain_Estimate_Returns200WithPlan()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId, sql = "SELECT * FROM users", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryPlanResponse>(ct);
        Assert.NotNull(body);
        Assert.NotEmpty(body!.PlanJson);
        Assert.True(body.Summary.TotalCost > 0);
    }

    [Fact]
    public async Task Explain_BadSql_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId, sql = "SELECT * FROM does_not_exist_xyz", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Explain_WithoutDatabaseRole_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        // Bob has no query:execute on the database.
        var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId = Guid.NewGuid(), sql = "SELECT 1", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.True(resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }
}
```

Also create `tests/IntegrationTests/QueryTestSetup.cs` holding the two shared helpers `MutationRequest` and `AliceWithBlueServerAsync` (moved verbatim from `QueryServiceTests`, made `internal static`, taking `SluiceBaseStackFactory factory` as a parameter). Update `QueryServiceTests` to call `QueryTestSetup.MutationRequest` / `QueryTestSetup.AliceWithBlueServerAsync(factory, ct)` instead of its private copies. (This is a pure move — no behaviour change.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter QueryExplainEndpointTests`
Expected: FAIL — endpoint `/api/query/explain` not mapped / `QueryPlanResponse` missing (compile error).

- [ ] **Step 3: Add `ExplainAsync` to `IQueryService` and refactor `QueryService`**

In `src/SluiceBase.Api/Services/IQueryService.cs`:

Add the result record and interface method near `QueryExecutionResult`:

```csharp
internal sealed record QueryExplainResult(
    QueryOutcome Outcome,
    QueryPlan? Plan,
    IReadOnlyList<BlockedColumn>? BlockedColumns,
    string? Error);
```

Add to the `IQueryService` interface:

```csharp
    Task<QueryExplainResult> ExplainAsync(User user, DatabaseId databaseId, string sql, bool analyze, CancellationToken ct);
```

In `QueryService`, add a private access-check helper that encapsulates the load + permission + sensitive-column computation (no logging), plus the new `ExplainAsync`:

```csharp
    private enum AccessCheck { Ok, NotFound, Forbidden, Blocked }

    private sealed record AccessResult(
        AccessCheck Check,
        Database? Database,           // SluiceBase.Core.Servers.Database (already imported)
        IReadOnlyList<BlockedColumn>? BlockedColumns,
        string[] TouchedSensitive);

    private async Task<AccessResult> CheckAccessAsync(
        User user, DatabaseId databaseId, string sql, CancellationToken ct)
    {
        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return new AccessResult(AccessCheck.NotFound, null, null, []);
        }

        var hasRole = await resolver.HasDatabasePermissionAsync(user.Id, database.Id, Permissions.QueryExecute, ct);
        if (!hasRole)
        {
            return new AccessResult(AccessCheck.Forbidden, null, null, []);
        }

        string[] touchedSensitive = [];

        var sensitiveColumns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == database.Id)
            .ToListAsync(ct);

        if (sensitiveColumns.Count > 0)
        {
            var allSensitive = sensitiveColumns
                .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
                .ToList();
            var allHits = SqlColumnChecker.FindBlockedColumns(sql, allSensitive);

            if (allHits.Count > 0)
            {
                touchedSensitive = allHits
                    .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
                    .ToArray();

                var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
                var bypassedIds = await db.UserColumnBypasses
                    .AsNoTracking()
                    .Where(b => b.UserId == user.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
                    .Select(b => b.SensitiveColumnId)
                    .ToListAsync(ct);

                var blockedColumns = sensitiveColumns
                    .Where(c => !bypassedIds.Contains(c.Id))
                    .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
                    .ToList();

                if (blockedColumns.Count > 0)
                {
                    var blockedHits = SqlColumnChecker.FindBlockedColumns(sql, blockedColumns);
                    if (blockedHits.Count > 0)
                    {
                        var blockedList = blockedHits
                            .Select(h => new BlockedColumn(h.Schema, h.Table, h.Column))
                            .ToList();
                        return new AccessResult(AccessCheck.Blocked, database, blockedList, touchedSensitive);
                    }
                }
            }
        }

        return new AccessResult(AccessCheck.Ok, database, null, touchedSensitive);
    }

    public async Task<QueryExplainResult> ExplainAsync(
        User user, DatabaseId databaseId, string sql, bool analyze, CancellationToken ct)
    {
        var access = await CheckAccessAsync(user, databaseId, sql, ct);
        switch (access.Check)
        {
            case AccessCheck.NotFound:
                return new QueryExplainResult(QueryOutcome.NotFound, null, null, null);
            case AccessCheck.Forbidden:
                return new QueryExplainResult(QueryOutcome.Forbidden, null, null, null);
            case AccessCheck.Blocked:
                return new QueryExplainResult(QueryOutcome.Blocked, null, access.BlockedColumns, null);
        }

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(access.Database!.Id, CredentialKind.Read, ct);
            var engine = engineRegistry.Resolve(access.Database.Server!.Kind);
            var plan = await engine.ExplainAsync(connectionString, sql, analyze, linkedCts.Token);
            return new QueryExplainResult(QueryOutcome.Ok, plan, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new QueryExplainResult(QueryOutcome.BadRequest, null, null, ex.Message);
        }
    }
```

Then refactor the existing `ExecuteAsync` to call `CheckAccessAsync` for its load/permission/sensitive stages, preserving current behaviour (including writing the Blocked `QueryLog`). Replace the block from the `database` load through the sensitive-column `return new QueryExecutionResult(QueryOutcome.Blocked, ...)` with:

```csharp
        var access = await CheckAccessAsync(user, databaseId, sql, ct);
        switch (access.Check)
        {
            case AccessCheck.NotFound:
                return new QueryExecutionResult(QueryOutcome.NotFound, null, null, null);
            case AccessCheck.Forbidden:
                return new QueryExecutionResult(QueryOutcome.Forbidden, null, null, null);
            case AccessCheck.Blocked:
            {
                var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
                var logEntry = QueryLog.Create(user.Id, access.Database!.Id, sql,
                    QueryLogStatus.Blocked, startedAt, durationMs, null,
                    $"Sensitive columns: {string.Join(", ", access.BlockedColumns!.Select(c => $"{c.Schema}.{c.Table}.{c.Column}"))}",
                    access.TouchedSensitive,
                    source);
                db.QueryLogs.Add(logEntry);
                await db.SaveChangesAsync(ct);
                return new QueryExecutionResult(QueryOutcome.Blocked, null, access.BlockedColumns, null);
            }
        }

        var database = access.Database!;
        var touchedSensitive = access.TouchedSensitive;
```

Keep the rest of `ExecuteAsync` (timeout, execute, logging) unchanged, referencing the `database` and `touchedSensitive` locals defined above. Remove the now-duplicated load/permission/sensitive code that preceded this block.

- [ ] **Step 4: Add the endpoint + DTOs**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`:

In `Map`, after the `/api/query` mapping:

```csharp
        app.MapPost("/api/query/explain", ExplainQuery)
            .RequireAuthorization()
            .WithName("ExplainQuery");
```

Add the handler:

```csharp
    private static async Task<Results<Ok<QueryPlanResponse>, NotFound, BadRequest<string>, ForbidHttpResult, ProblemHttpResult>> ExplainQuery(
        ExplainRequest request,
        ICurrentUserAccessor currentUser,
        IQueryService queryService,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var result = await queryService.ExplainAsync(user!, request.DatabaseId, request.Sql, request.Analyze, ct);

        return result.Outcome switch
        {
            QueryOutcome.NotFound => TypedResults.NotFound(),
            QueryOutcome.Forbidden => TypedResults.Forbid(),
            QueryOutcome.Blocked => TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sensitive columns",
                type: "sensitive_columns",
                extensions: new Dictionary<string, object?> { ["columns"] = result.BlockedColumns!.Select(c => new { schema = c.Schema, table = c.Table, column = c.Column }).ToArray() }),
            QueryOutcome.BadRequest => TypedResults.BadRequest(result.Error!),
            _ => TypedResults.Ok(new QueryPlanResponse(result.Plan!.PlanJson, result.Plan.Summary)),
        };
    }
```

Add the DTO records near `QueryRequest` (add `using SluiceBase.Core.Queries;` if not present — it is):

```csharp
    public sealed record ExplainRequest(DatabaseId DatabaseId, string Sql, bool Analyze);

    public sealed record QueryPlanResponse(string PlanJson, QueryPlanSummary Summary);
```

- [ ] **Step 5: Run the endpoint tests to verify they pass**

Run: `dotnet test tests/IntegrationTests --filter QueryExplainEndpointTests`
Expected: PASS (3 tests). Also run `dotnet test tests/IntegrationTests --filter QueryServiceTests` to confirm the helper move + `ExecuteAsync` refactor kept existing behaviour green.

- [ ] **Step 6: Regenerate the API contract**

Run: `dotnet build src/SluiceBase.Api` (Debug — emits `src/SluiceBase.Api/openapi.json`)
Then: `cd src/frontend && npm run gen:api` (rewrites `src/api/schema.ts`)
Expected: `openapi.json` now contains `/api/query/explain`; `schema.ts` has the new path. Confirm with `git diff --stat`.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api tests/IntegrationTests src/frontend/src/api/schema.ts
git commit -m "Add query explain service, endpoint, and DTOs"
```

---

### Task 4: Inline best-effort advisory estimate on `/api/query`

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` (`QueryResponse` record)
- Modify: `src/SluiceBase.Api/Services/IQueryService.cs` (`ExecuteAsync`)
- Modify: `src/SluiceBase.Api/openapi.json` + `src/frontend/src/api/schema.ts` (regenerated)
- Test: `tests/IntegrationTests/QueryExplainEndpointTests.cs` (add advisory tests)

**Interfaces:**
- Consumes: `ITargetEngine.ExplainAsync` (Task 2), `QueryPlanSummary` (Task 1).
- Produces: `QueryResponse` gains a trailing `QueryPlanSummary? Estimate` property, serialized as `estimate` on `/api/query` 200 responses.

- [ ] **Step 1: Write the failing advisory test**

Add to `tests/IntegrationTests/QueryExplainEndpointTests.cs`:

```csharp
    [Fact]
    public async Task Query_SuccessfulSelect_IncludesPlanEstimate()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = "SELECT * FROM users" });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(body);
        Assert.NotNull(body!.Estimate);
        Assert.True(body.Estimate!.TotalCost > 0);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/IntegrationTests --filter Query_SuccessfulSelect_IncludesPlanEstimate`
Expected: FAIL — `QueryResponse` has no `Estimate` member (compile error).

- [ ] **Step 3: Add the `Estimate` field to `QueryResponse`**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, change the `QueryResponse` record to add a trailing nullable field:

```csharp
    public sealed record QueryResponse(
        string[]? Columns,
        string?[][]? Rows,
        int RowCount,
        int DurationMs,
        string? Error,
        QueryPlanSummary? Estimate);
```

Every existing `new QueryResponse(...)` in `QueryService.ExecuteAsync` must add a trailing argument. For the error, timeout, and catch-all responses pass `null`. Only the success response passes the computed estimate (Step 4).

- [ ] **Step 4: Compute the estimate before executing (success path)**

In `QueryService.ExecuteAsync`, inside the `try` block, after resolving `connectionString` and `targetEngine` and **before** `targetEngine.ExecuteQueryAsync`, insert:

```csharp
            QueryPlanSummary? estimate = null;
            if (configuration.GetValue("Query:AutoExplain", true))
            {
                try
                {
                    var estimatePlan = await targetEngine.ExplainAsync(connectionString, sql, analyze: false, linkedCts.Token);
                    estimate = estimatePlan.Summary;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Advisory estimate is best-effort; never fail the real query over it.
                    _ = ex;
                }
            }
```

Then change the success `response` assignment to include `estimate`:

```csharp
            response = new QueryEndpoints.QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null, estimate);
```

Update the other `new QueryEndpoints.QueryResponse(...)` calls (InvalidOperationException, timeout, generic catch) to pass a trailing `null`:

```csharp
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, ex.Message, null);
            // ... timeout:
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, $"Query timed out after {timeoutSeconds}s.", null);
            // ... generic catch:
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, ex.Message, null);
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/IntegrationTests --filter QueryExplainEndpointTests`
Expected: PASS (all tests, including the new advisory test).

- [ ] **Step 6: Regenerate the API contract**

Run: `dotnet build src/SluiceBase.Api`
Then: `cd src/frontend && npm run gen:api`
Expected: `/api/query` 200 response type now has an `estimate` property in `schema.ts`.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api src/frontend/src/api/schema.ts tests/IntegrationTests/QueryExplainEndpointTests.cs
git commit -m "Attach best-effort plan estimate to query responses"
```

---

### Task 5: Frontend explain hook (`useExplainRuns`) + shared types

**Files:**
- Modify: `src/frontend/src/api/hooks.ts` (add exported response types)
- Create: `src/frontend/src/api/useExplainRuns.ts`
- Test: `src/frontend/src/api/__tests__/useExplainRuns.test.ts`

**Interfaces:**
- Consumes: generated `paths` from `schema.ts` (Tasks 3–4), `apiRequest`, `runLimited`, and `isBlocked` (exported from `useQueryRuns.ts`).
- Produces: `useExplainRuns()` returning `{ runs: Array<ExplainEntry>, run(databaseId, statements, analyze), isRunning }`; exported types `ExplainResponse`, `QueryPlanSummary`, `ExplainEntry`.

- [ ] **Step 1: Add exported types to `hooks.ts`**

In `src/frontend/src/api/hooks.ts`, near `ExecuteQueryResponse` (line ~168), add:

```typescript
export type ExplainResponse =
  paths["/api/query/explain"]["post"]["responses"][200]["content"]["application/json"];

export type QueryPlanSummary = ExplainResponse["summary"];
```

- [ ] **Step 2: Write the failing hook test**

Create `src/frontend/src/api/__tests__/useExplainRuns.test.ts`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { useExplainRuns } from "@/api/useExplainRuns";
import * as client from "@/api/client";

const stmt = (text: string) => ({
  text, fromPos: 0, toPos: text.length, fromLine: 1, toLine: 1,
});

describe("useExplainRuns", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("posts to /api/query/explain with the analyze flag and records the plan", async () => {
    const spy = vi.spyOn(client, "apiRequest").mockResolvedValue({
      planJson: "[{}]",
      summary: { totalCost: 1, estimatedRows: 2, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
    });

    const { result } = renderHook(() => useExplainRuns());
    act(() => result.current.run("db-1", [stmt("SELECT 1")], true));

    await waitFor(() => expect(result.current.runs[0].status).toBe("success"));
    expect(result.current.runs[0].plan?.summary.rootNode).toBe("Seq Scan");
    expect(spy).toHaveBeenCalledWith("/api/query/explain", {
      method: "POST",
      body: { databaseId: "db-1", sql: "SELECT 1", analyze: true },
    });
  });

  it("classifies a sensitive-column 403 as blocked", async () => {
    vi.spyOn(client, "apiRequest").mockRejectedValue(
      new client.ApiError(403, { type: "sensitive_columns" }),
    );

    const { result } = renderHook(() => useExplainRuns());
    act(() => result.current.run("db-1", [stmt("SELECT ssn FROM users")], false));

    await waitFor(() => expect(result.current.runs[0].status).toBe("blocked"));
  });
});
```

Note: match `ApiError`'s real constructor signature in `client.ts`; adjust the `new client.ApiError(...)` args if it differs.

- [ ] **Step 3: Run to verify it fails**

Run: `cd src/frontend && npx vitest run src/api/__tests__/useExplainRuns.test.ts`
Expected: FAIL — `useExplainRuns` module not found.

- [ ] **Step 4: Implement `useExplainRuns`**

Create `src/frontend/src/api/useExplainRuns.ts`:

```typescript
import { useCallback, useRef, useState } from "react";
import type { ExplainResponse } from "@/api/hooks";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import type { paths } from "@/api/schema";
import { runLimited } from "@/utils/runLimited";
import { apiRequest } from "@/api/client";
import { isBlocked } from "@/api/useQueryRuns";

const MAX_CONCURRENCY = 6;

type ExplainRequestBody =
  paths["/api/query/explain"]["post"]["requestBody"]["content"]["application/json"];

export interface ExplainEntry {
  id: string;
  index: number;
  text: string;
  fromPos: number;
  toPos: number;
  fromLine: number;
  toLine: number;
  analyze: boolean;
  status: "pending" | "success" | "error" | "blocked";
  plan: ExplainResponse | null;
  error: unknown;
}

export function useExplainRuns() {
  const [runs, setRuns] = useState<Array<ExplainEntry>>([]);
  const batchRef = useRef(0);

  const run = useCallback(
    (databaseId: string, statements: Array<SqlStatement>, analyze: boolean) => {
      const batchId = ++batchRef.current;

      const initial: Array<ExplainEntry> = statements.map((s, index) => ({
        id: `${batchId}-${index}`,
        index,
        text: s.text,
        fromPos: s.fromPos,
        toPos: s.toPos,
        fromLine: s.fromLine,
        toLine: s.toLine,
        analyze,
        status: "pending",
        plan: null,
        error: null,
      }));
      setRuns(initial);

      const patch = (id: string, update: Partial<ExplainEntry>) => {
        if (batchId !== batchRef.current) return;
        setRuns((prev) => prev.map((r) => (r.id === id ? { ...r, ...update } : r)));
      };

      void runLimited(initial, MAX_CONCURRENCY, async (entry) => {
        try {
          const plan = await apiRequest<ExplainRequestBody, ExplainResponse>(
            "/api/query/explain",
            { method: "POST", body: { databaseId, sql: entry.text, analyze } },
          );
          patch(entry.id, { status: "success", plan, error: null });
        } catch (err) {
          patch(entry.id, {
            status: isBlocked(err) ? "blocked" : "error",
            plan: null,
            error: err,
          });
        }
      });
    },
    [],
  );

  const isRunning = runs.some((r) => r.status === "pending");
  return { runs, run, isRunning };
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `cd src/frontend && npx vitest run src/api/__tests__/useExplainRuns.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 6: Lint + commit**

```bash
cd src/frontend && npm run lint
cd ../.. && git add src/frontend/src/api/hooks.ts src/frontend/src/api/useExplainRuns.ts src/frontend/src/api/__tests__/useExplainRuns.test.ts
git commit -m "Add useExplainRuns hook and explain response types"
```

---

### Task 6: Frontend Plan view components (badges + raw panel + tabs)

**Files:**
- Create: `src/frontend/src/components/query/PlanSummaryBadges.tsx`
- Create: `src/frontend/src/components/query/PlanView.tsx`
- Create: `src/frontend/src/components/query/PlanTabs.tsx`
- Test: `src/frontend/src/components/query/__tests__/PlanView.test.tsx`

**Interfaces:**
- Consumes: `QueryPlanSummary` (Task 5), `ExplainEntry` (Task 5).
- Produces: `PlanSummaryBadges({ summary, label? })`, `PlanView({ entry })`, `PlanTabs({ runs, onHighlight })`.

- [ ] **Step 1: Write the failing component test**

Create `src/frontend/src/components/query/__tests__/PlanView.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { PlanView } from "@/components/query/PlanView";
import type { ExplainEntry } from "@/api/useExplainRuns";

const entry = (over: Partial<ExplainEntry> = {}): ExplainEntry => ({
  id: "1-0", index: 0, text: "SELECT * FROM users",
  fromPos: 0, toPos: 19, fromLine: 1, toLine: 1, analyze: false,
  status: "success",
  plan: {
    planJson: '[{"Plan":{"Node Type":"Seq Scan"}}]',
    summary: { totalCost: 42, estimatedRows: 1000, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
  },
  error: null,
  ...over,
});

const renderView = (e: ExplainEntry) =>
  render(<MantineProvider><PlanView entry={e} /></MantineProvider>);

describe("PlanView", () => {
  it("shows estimated cost, rows and a seq-scan warning", () => {
    renderView(entry());
    expect(screen.getByText(/1,?000/)).toBeInTheDocument();
    expect(screen.getByText(/42/)).toBeInTheDocument();
    expect(screen.getByText(/seq scan/i)).toBeInTheDocument();
  });

  it("shows actual time when analyzed", () => {
    renderView(entry({
      analyze: true,
      plan: {
        planJson: "[{}]",
        summary: { totalCost: 42, estimatedRows: 10, rootNode: "Index Scan", hasSeqScan: false, actualTotalMs: 3.5 },
      },
    }));
    expect(screen.getByText(/3\.5\s*ms/i)).toBeInTheDocument();
  });

  it("renders the query error for a failed explain", () => {
    renderView(entry({ status: "error", plan: null, error: new Error("boom") }));
    expect(screen.getByText(/could not/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npx vitest run src/components/query/__tests__/PlanView.test.tsx`
Expected: FAIL — component modules not found.

- [ ] **Step 3: Implement `PlanSummaryBadges`**

Create `src/frontend/src/components/query/PlanSummaryBadges.tsx`:

```typescript
import { Badge, Group, Text } from "@mantine/core";
import type { QueryPlanSummary } from "@/api/hooks";

function fmt(n: number): string {
  return new Intl.NumberFormat().format(Math.round(n));
}

export function PlanSummaryBadges({
  summary,
  label,
}: {
  summary: QueryPlanSummary;
  label?: string;
}) {
  return (
    <Group gap="xs" wrap="wrap" align="center">
      {label && (
        <Text size="xs" c="dimmed" fw={500}>
          {label}
        </Text>
      )}
      <Badge variant="light" color="gray">~{fmt(summary.estimatedRows)} rows</Badge>
      <Badge variant="light" color="gray">cost {fmt(summary.totalCost)}</Badge>
      <Badge variant="light" color="blue">{summary.rootNode}</Badge>
      {summary.hasSeqScan && (
        <Badge variant="light" color="orange">Seq Scan</Badge>
      )}
      {summary.actualTotalMs != null && (
        <Badge variant="light" color="teal">{summary.actualTotalMs} ms actual</Badge>
      )}
    </Group>
  );
}
```

- [ ] **Step 4: Implement `PlanView`**

Create `src/frontend/src/components/query/PlanView.tsx`:

```typescript
import { Alert, Code, Collapse, Stack, Text, UnstyledButton } from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconChevronRight } from "@tabler/icons-react";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { ApiError } from "@/api/client";
import { PlanSummaryBadges } from "@/components/query/PlanSummaryBadges";

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

export function PlanView({ entry }: { entry: ExplainEntry }) {
  const [open, { toggle }] = useDisclosure(false);

  if (entry.status === "pending") {
    return <Text p="xs" size="sm" c="dimmed">Analyzing…</Text>;
  }

  if (entry.status === "blocked") {
    const body = entry.error instanceof ApiError
      ? (entry.error.body as { columns?: Array<{ schema: string; table: string; column: string }> } | null)
      : null;
    return (
      <Alert color="orange" title="Blocked — restricted columns" m="xs">
        {(body?.columns ?? []).map((c, i) => (
          <Code key={i} display="block" fz="xs">{c.schema}.{c.table}.{c.column}</Code>
        ))}
      </Alert>
    );
  }

  if (entry.status === "error" || !entry.plan) {
    const message = entry.error instanceof ApiError
      ? String(entry.error.body ?? entry.error.message)
      : "Could not analyze this statement.";
    return (
      <Alert color="red" title="Explain failed" m="xs">
        {message}
      </Alert>
    );
  }

  return (
    <Stack p="xs" gap="xs">
      <PlanSummaryBadges summary={entry.plan.summary} />
      <UnstyledButton onClick={toggle}>
        <Text size="xs" c="dimmed" style={{ display: "flex", alignItems: "center", gap: 4 }}>
          <IconChevronRight
            size={12}
            style={{ transform: open ? "rotate(90deg)" : "none", transition: "transform 120ms" }}
          />
          Raw plan
        </Text>
      </UnstyledButton>
      <Collapse in={open}>
        <Code block fz="xs" style={{ maxHeight: 320, overflow: "auto" }}>
          {prettyJson(entry.plan.planJson)}
        </Code>
      </Collapse>
    </Stack>
  );
}
```

- [ ] **Step 5: Implement `PlanTabs`**

Create `src/frontend/src/components/query/PlanTabs.tsx`:

```typescript
import { useState } from "react";
import { Box, Group, Tabs, Text, Tooltip } from "@mantine/core";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { PlanView } from "@/components/query/PlanView";

function snippet(text: string, max = 36): string {
  const oneLine = text.replace(/\s+/g, " ").trim();
  return oneLine.length > max ? `${oneLine.slice(0, max - 1)}…` : oneLine;
}

export function PlanTabs({
  runs,
  onHighlight,
}: {
  runs: Array<ExplainEntry>;
  onHighlight: (entry: ExplainEntry) => void;
}) {
  const [active, setActive] = useState<string | null>(runs[0]?.id ?? null);

  if (runs.length === 0) {
    return <Text p="xs" size="sm" c="dimmed">Explain a query to see its plan.</Text>;
  }

  const activeEntry = runs.find((r) => r.id === active) ?? runs[0];

  return (
    <Tabs
      value={activeEntry.id}
      onChange={(value) => {
        if (!value) return;
        setActive(value);
        const entry = runs.find((r) => r.id === value);
        if (entry) onHighlight(entry);
      }}
      keepMounted={false}
      style={{ display: "flex", flexDirection: "column", height: "100%" }}
    >
      <Tabs.List style={{ flexShrink: 0, flexWrap: "nowrap", overflowX: "auto" }}>
        {runs.map((entry) => (
          <Tabs.Tab key={entry.id} value={entry.id}>
            <Tooltip label={entry.text} multiline maw={480} withArrow openDelay={400}>
              <Group gap={6} wrap="nowrap">
                <Text size="xs" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
                  {snippet(entry.text)}
                </Text>
              </Group>
            </Tooltip>
          </Tabs.Tab>
        ))}
      </Tabs.List>
      <Box style={{ flex: 1, minHeight: 0, overflow: "auto" }}>
        <PlanView entry={activeEntry} />
      </Box>
    </Tabs>
  );
}
```

- [ ] **Step 6: Run to verify tests pass**

Run: `cd src/frontend && npx vitest run src/components/query/__tests__/PlanView.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 7: Lint + commit**

```bash
cd src/frontend && npm run lint
cd ../.. && git add src/frontend/src/components/query/PlanSummaryBadges.tsx src/frontend/src/components/query/PlanView.tsx src/frontend/src/components/query/PlanTabs.tsx src/frontend/src/components/query/__tests__/PlanView.test.tsx
git commit -m "Add plan view components for query performance display"
```

---

### Task 7: Advisory strip on normal run results

**Files:**
- Modify: `src/frontend/src/components/query/ResultGrid.tsx`
- Test: `src/frontend/src/components/query/__tests__/ResultGrid.test.tsx`

**Interfaces:**
- Consumes: `PlanSummaryBadges` (Task 6); the `estimate` field now present on `RunEntry.response` (Task 4 contract → `ExecuteQueryResponse`).

- [ ] **Step 1: Write the failing test**

Add to `src/frontend/src/components/query/__tests__/ResultGrid.test.tsx` (follow the file's existing render helper/imports):

```typescript
  it("shows an advisory estimate strip on a successful run", () => {
    const entry = {
      id: "1-0", index: 0, text: "SELECT 1",
      fromPos: 0, toPos: 8, fromLine: 1, toLine: 1,
      status: "success" as const,
      response: {
        columns: ["?column?"], rows: [["1"]], rowCount: 1, durationMs: 2, error: null,
        estimate: { totalCost: 15, estimatedRows: 500, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
      },
      error: null,
    };
    renderGrid(entry); // use the test file's existing render helper
    expect(screen.getByText(/planner estimate/i)).toBeInTheDocument();
    expect(screen.getByText(/~500 rows/i)).toBeInTheDocument();
  });
```

If the test file has no shared `renderGrid` helper, wrap inline: `render(<MantineProvider><ResultGrid entry={entry} /></MantineProvider>)`.

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npx vitest run src/components/query/__tests__/ResultGrid.test.tsx`
Expected: FAIL — "planner estimate" text not found.

- [ ] **Step 3: Add the advisory strip to the success branch**

In `src/frontend/src/components/query/ResultGrid.tsx`, add the import:

```typescript
import { PlanSummaryBadges } from "@/components/query/PlanSummaryBadges";
```

Replace the `// success` return with a wrapper that renders the strip above the table when an estimate is present:

```typescript
  // success
  return (
    <Stack gap={0} h="100%">
      {entry.response?.estimate && (
        <Box px="xs" pt="xs">
          <PlanSummaryBadges summary={entry.response.estimate} label="Planner estimate" />
        </Box>
      )}
      <Box style={{ flex: 1, minHeight: 0 }}>
        <ResultTable
          columns={entry.response?.columns ?? []}
          rows={entry.response?.rows ?? []}
          rowCount={Number(entry.response?.rowCount ?? 0)}
          durationMs={Number(entry.response?.durationMs ?? 0)}
          resultIndex={entry.index}
        />
      </Box>
    </Stack>
  );
```

Add `Box` to the existing `@mantine/core` import in this file.

- [ ] **Step 4: Run to verify it passes**

Run: `cd src/frontend && npx vitest run src/components/query/__tests__/ResultGrid.test.tsx`
Expected: PASS (existing tests + the new strip test).

- [ ] **Step 5: Lint + commit**

```bash
cd src/frontend && npm run lint
cd ../.. && git add src/frontend/src/components/query/ResultGrid.tsx src/frontend/src/components/query/__tests__/ResultGrid.test.tsx
git commit -m "Show advisory plan estimate strip on query results"
```

---

### Task 8: Wire the Explain split-button + Result/Plan toggle in the playground

**Files:**
- Modify: `src/frontend/src/routes/_authed/query/index.tsx`
- Test: `src/frontend/src/routes/_authed/query/__tests__/explain-button.test.tsx`

**Interfaces:**
- Consumes: `useExplainRuns` (Task 5), `PlanTabs` (Task 6), existing `selectStatements`, `splitSqlStatements`, `highlightStatementInEditor`, `useQueryRuns`.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/routes/_authed/query/__tests__/explain-button.test.tsx`. Model the harness on the existing `history-page.test.tsx` (query client + MantineProvider wrappers). The test drives the Explain button and asserts an explain request fires for the selected statement scope:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as client from "@/api/client";

// Route component is exported for testing; import the page component directly.
// If index.tsx does not export QueryPage, add `export` to it in Step 3.
import { QueryPage } from "@/routes/_authed/query/index";
import { TestProviders } from "@/test/TestProviders"; // reuse whatever history-page.test.tsx uses

describe("Explain button", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("sends an explain request for the editor contents", async () => {
    const spy = vi.spyOn(client, "apiRequest").mockResolvedValue({
      planJson: "[{}]",
      summary: { totalCost: 1, estimatedRows: 1, rootNode: "Result", hasSeqScan: false, actualTotalMs: null },
    });

    render(<TestProviders><QueryPage /></TestProviders>);
    // Select a database and type SQL using the same interactions history-page.test.tsx uses.
    // ... (harness-specific setup) ...

    await userEvent.click(screen.getByRole("button", { name: /^explain$/i }));

    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith(
        "/api/query/explain",
        expect.objectContaining({ method: "POST", body: expect.objectContaining({ analyze: false }) }),
      ),
    );
  });
});
```

Note: this test is harness-heavy. If wiring full page providers is impractical, instead extract the run/explain dispatch into a small pure helper `buildExplainTargets(view, runAll)` and unit-test that, and verify the button visually in Step 5's `/run`-style check. Prefer the extracted-helper approach if the page harness does not already exist in the repo.

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npx vitest run src/routes/_authed/query/__tests__/explain-button.test.tsx`
Expected: FAIL — no Explain button.

- [ ] **Step 3: Add explain state, handler, and the split-button**

In `src/frontend/src/routes/_authed/query/index.tsx`:

Add imports:

```typescript
import { Menu } from "@mantine/core";
import { IconChartBar, IconChevronDown } from "@tabler/icons-react";
import { useExplainRuns } from "@/api/useExplainRuns";
import { PlanTabs } from "@/components/query/PlanTabs";
```

Inside `QueryPage`, next to `const { runs, run, isRunning } = useQueryRuns();` add:

```typescript
  const explain = useExplainRuns();
  // Which view the bottom pane shows: last action wins.
  const [resultView, setResultView] = useState<"results" | "plan">("results");
```

Add an explain handler mirroring `handleRun` (statement scoping via `selectStatements`):

```typescript
  const handleExplain = useCallback(
    (analyze: boolean, view: EditorView | null | undefined) => {
      if (!selectedDatabaseId || !view) return;
      const stmts = splitSqlStatements(view.state.doc.toString());
      if (stmts.length === 0) return;
      const { from, to, empty } = view.state.selection.main;
      const targets = selectStatements(stmts, { from, to, empty }, false);
      if (targets.length > 0) {
        setResultView("plan");
        explain.run(selectedDatabaseId, targets, analyze);
      }
    },
    [selectedDatabaseId, explain],
  );
```

In `handleRun`, add `setResultView("results");` right after the `if (targets.length > 0) {` line so running switches the pane back to results.

Add the Explain split-button to the button `Group` (after the "Run all" button):

```typescript
                <Button.Group>
                  <Button
                    variant="default"
                    leftSection={<IconChartBar size={14} />}
                    size="sm"
                    onClick={() => handleExplain(false, editorRef.current?.view)}
                    loading={explain.isRunning}
                    disabled={!selectedDatabaseId || statements.length === 0 || explain.isRunning}
                  >
                    Explain
                  </Button>
                  <Menu position="bottom-end" withArrow shadow="md">
                    <Menu.Target>
                      <Button
                        variant="default"
                        size="sm"
                        px={6}
                        disabled={!selectedDatabaseId || statements.length === 0 || explain.isRunning}
                        aria-label="Explain options"
                      >
                        <IconChevronDown size={14} />
                      </Button>
                    </Menu.Target>
                    <Menu.Dropdown>
                      <Menu.Item onClick={() => handleExplain(true, editorRef.current?.view)}>
                        Explain with timings (runs the query)
                      </Menu.Item>
                    </Menu.Dropdown>
                  </Menu>
                </Button.Group>
```

- [ ] **Step 4: Swap the bottom pane between results and plan**

Replace the bottom `Splitter.Pane` content:

```typescript
          <Splitter.Pane defaultSize={65} min={15} style={{ overflow: "hidden" }}>
            {resultView === "plan" ? (
              <PlanTabs runs={explain.runs} onHighlight={handleHighlight} />
            ) : (
              <ResultTabs runs={runs} onHighlight={handleHighlight} />
            )}
          </Splitter.Pane>
```

`handleHighlight` already accepts `{ fromPos, toPos }`, which both `RunEntry` and `ExplainEntry` satisfy — no change needed.

If Step 1 imports `QueryPage`, add `export` to `function QueryPage()`.

- [ ] **Step 5: Run the test + verify in the app**

Run: `cd src/frontend && npx vitest run src/routes/_authed/query/__tests__/explain-button.test.tsx`
Expected: PASS.
Then invoke the `run` skill (or `npm run dev`) to open the playground, select a database, type `SELECT * FROM users`, click **Explain**, and confirm the Plan tab shows summary badges + a collapsible raw plan; click the chevron → "Explain with timings" and confirm an "actual ms" badge appears; run a normal query and confirm the advisory strip shows above the grid.

- [ ] **Step 6: Full frontend gate + commit**

```bash
cd src/frontend && npm run lint && npx vitest run
cd ../.. && git add src/frontend/src/routes/_authed/query/index.tsx src/frontend/src/routes/_authed/query/__tests__/explain-button.test.tsx
git commit -m "Wire explain split-button and plan view into query playground"
```

---

## Final verification

- [ ] Backend: `dotnet build` (Debug, whole solution) — 0 warnings; `dotnet test tests/SluiceBase.Api.Tests tests/IntegrationTests` green.
- [ ] Frontend: `cd src/frontend && npm run lint && npx vitest run` green.
- [ ] Contract: `git diff --stat` shows `openapi.json` + `schema.ts` regenerated and committed; `/api/query/explain` present and `/api/query` response carries `estimate`.
- [ ] Manual smoke (via `run` skill): Explain (estimate), Explain with timings (actual ms), advisory strip on a normal run, sensitive-column query → blocked plan view.

## Notes / deviations from the spec

- **Non-explainable statements** (e.g. `SET`, some DDL) surface as a `400` with the Postgres error, rendered by `PlanView`'s error state, rather than a dedicated "not explainable" flag. This keeps the DTO minimal; revisit if the error text proves confusing.
- **Gateway route** needs no change — AppHost uses a catch-all `/api/{**rest}` (verified in `src/AppHost/Program.cs`), which already covers `/api/query/explain`.
