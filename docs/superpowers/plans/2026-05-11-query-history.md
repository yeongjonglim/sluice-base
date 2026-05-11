# Query History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `/query/history` page where users see their own past queries (or all queries with `query:audit` permission), with filters for date range, database, status, and a per-row Copy SQL button.

**Architecture:** New `GET /api/query/history` endpoint on the backend applies an implicit user filter unless the caller has `query:audit`; EF correlated subqueries resolve database and user display names in a single DB round-trip. The frontend restructures the query route into a `query/` directory (matching the existing `update/` pattern), adds the history page with URL-search-param-driven filters, and updates the nav to a nested Query/History group.

**Tech Stack:** .NET 10 minimal APIs, EF Core with Npgsql, xUnit integration tests; React 19 + TanStack Router v1 + TanStack Query v5 + Mantine v9 + Vitest.

---

## File Map

**Backend — modify:**
- `src/SluiceBase.Core/Permissions/Permissions.cs` — add `QueryAudit` constant + to `All`
- `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` — add `GetHistory` handler + `QueryHistoryItem`/`QueryHistoryResponse` records

**Backend — create:**
- `tests/IntegrationTests/QueryHistoryEndpointTests.cs` — integration tests for the new endpoint

**Regenerated (after `dotnet build`):**
- `src/SluiceBase.Api/openapi.json`

**Regenerated (after `npm run gen:api`):**
- `src/frontend/src/api/schema.ts`

**Frontend — modify:**
- `src/frontend/src/auth/permission.ts` — add `"query:audit"` to `Permission` union
- `src/frontend/src/api/hooks.ts` — add `QueryHistoryFilters` type + `useQueryHistory` hook
- `src/frontend/src/routes/_authed.tsx` — update nav to nested Query/History group

**Frontend — create:**
- `src/frontend/src/routes/_authed/query/index.tsx` — moved from `query.tsx` (no logic changes)
- `src/frontend/src/routes/_authed/query/history.tsx` — new history page
- `src/frontend/src/api/__tests__/query-history-hooks.test.ts` — hook unit tests

**Frontend — delete:**
- `src/frontend/src/routes/_authed/query.tsx`

---

## Task 1: Add `query:audit` Permission

**Files:**
- Modify: `src/SluiceBase.Core/Permissions/Permissions.cs`
- Modify: `src/frontend/src/auth/permission.ts`

- [ ] **Step 1: Add the backend constant**

In `src/SluiceBase.Core/Permissions/Permissions.cs`, add `QueryAudit` after `QueryExecute` and include it in `All`:

```csharp
public const string QueryExecute = "query:execute";
public const string QueryAudit = "query:audit";          // ← add
public const string UpdateSubmit = "update:submit";
```

And in the `All` set:

```csharp
public static readonly IReadOnlySet<string> All = new HashSet<string>
{
    PermissionManage,
    ServerManage,
    QueryExecute,
    QueryAudit,           // ← add
    UpdateSubmit,
    UpdateApprove,
    UpdateExecute,
};
```

- [ ] **Step 2: Add the frontend type**

In `src/frontend/src/auth/permission.ts`, extend the `Permission` union:

```ts
export type Permission =
  | "permission:manage"
  | "server:manage"
  | "query:execute"
  | "query:audit"        // ← add
  | "update:submit"
  | "update:approve"
  | "update:execute";
```

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Permissions/Permissions.cs src/frontend/src/auth/permission.ts
git commit -m "feat: add query:audit permission"
```

---

## Task 2: Integration Tests for GetHistory (Write Failing)

**Files:**
- Create: `tests/IntegrationTests/QueryHistoryEndpointTests.cs`

- [ ] **Step 1: Create the test file**

Create `tests/IntegrationTests/QueryHistoryEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace IntegrationTests;

public class QueryHistoryEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>
    /// Signs in as Alice, grants her server:manage + query:execute, and creates a
    /// server/database against the blue Postgres instance. Returns Alice's session,
    /// her XSRF token, Alice's user-id, and the new database id.
    /// </summary>
    private async Task<(AuthenticatedSession session, string xsrf, string aliceId, DatabaseId databaseId)>
        AliceWithBlueServerAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        foreach (var perm in new[] { Permissions.ServerManage, Permissions.QueryExecute })
        {
            using var grant = MutationRequest(HttpMethod.Post,
                $"/api/admin/user/{alice.Id}/permission", xsrf, new { permission = perm });
            (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();
        }

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"hist-{Guid.NewGuid():N}"[..24];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("Read-only role", "reader_blue", "reader_blue"));
        var rcResp = await session.Client.SendAsync(rcReq, ct);
        rcResp.EnsureSuccessStatusCode();
        var readCred = (await rcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var database = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (session, xsrf, alice.Id, database.Id);
    }

    [Fact]
    public async Task GetHistory_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/query/history", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_Returns403_WithoutQueryExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        var resp = await session.Client.GetAsync("/api/query/history", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsBadRequest_WhenFromIsAfterTo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync(
            "/api/query/history?from=2030-01-01&to=2020-01-01", ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsOnlyOwnEntries_WithoutQueryAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aliceSession, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _a = aliceSession;

        // Ensure bob is registered and has query:execute
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);
        var users = await aliceSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");
        using var grantBob = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{bob.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await aliceSession.Client.SendAsync(grantBob, ct)).EnsureSuccessStatusCode();

        // Alice and bob each run a uniquely-tagged query
        var aliceSql = $"SELECT 1 -- alice-{Guid.NewGuid():N}";
        var bobSql   = $"SELECT 1 -- bob-{Guid.NewGuid():N}";

        using var aliceReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = aliceSql });
        (await aliceSession.Client.SendAsync(aliceReq, ct)).EnsureSuccessStatusCode();

        using var bobReq = MutationRequest(HttpMethod.Post, "/api/query", bobXsrf,
            new { databaseId, sql = bobSql });
        (await bobSession.Client.SendAsync(bobReq, ct)).EnsureSuccessStatusCode();

        // Alice fetches history — she has no query:audit
        var resp = await aliceSession.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == aliceSql);
        Assert.DoesNotContain(resp.Items, i => i.QueryText == bobSql);
    }

    [Fact]
    public async Task GetHistory_ReturnsAllEntries_WithQueryAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aliceSession, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _a = aliceSession;

        // Ensure bob is registered and has query:execute
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);
        var users = await aliceSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");
        using var grantBob = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{bob.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await aliceSession.Client.SendAsync(grantBob, ct)).EnsureSuccessStatusCode();

        // Grant alice query:audit
        using var grantAudit = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{aliceId}/permission", xsrf,
            new { permission = Permissions.QueryAudit });
        (await aliceSession.Client.SendAsync(grantAudit, ct)).EnsureSuccessStatusCode();

        var aliceSql = $"SELECT 1 -- audit-alice-{Guid.NewGuid():N}";
        var bobSql   = $"SELECT 1 -- audit-bob-{Guid.NewGuid():N}";

        using var aliceReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = aliceSql });
        (await aliceSession.Client.SendAsync(aliceReq, ct)).EnsureSuccessStatusCode();

        using var bobReq = MutationRequest(HttpMethod.Post, "/api/query", bobXsrf,
            new { databaseId, sql = bobSql });
        (await bobSession.Client.SendAsync(bobReq, ct)).EnsureSuccessStatusCode();

        // Alice fetches history — she has query:audit so she sees both
        var resp = await aliceSession.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == aliceSql);
        Assert.Contains(resp.Items, i => i.QueryText == bobSql);
    }

    [Fact]
    public async Task GetHistory_FiltersByStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var successSql = $"SELECT 1 -- status-ok-{Guid.NewGuid():N}";
        var errorSql   = $"SELECT nonexistent_col_xyz -- status-err-{Guid.NewGuid():N}";

        using var successReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = successSql });
        await session.Client.SendAsync(successReq, ct); // 200 or 400, either is fine

        using var errorReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = errorSql });
        await session.Client.SendAsync(errorReq, ct);

        // Filter by Error — should not contain the success entry
        var resp = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history?status=Error", ct);
        Assert.NotNull(resp);
        Assert.All(resp.Items, i => Assert.Equal("Error", i.Status));
        Assert.DoesNotContain(resp.Items, i => i.QueryText == successSql);
    }

    [Fact]
    public async Task GetHistory_FiltersByDatabaseId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var sql = $"SELECT 1 -- db-filter-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql });
        await session.Client.SendAsync(req, ct);

        // Filter by known databaseId
        var resp = await session.Client.GetFromJsonAsync<HistoryBody>(
            $"/api/query/history?databaseId={databaseId}", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == sql);
        Assert.All(resp.Items, i => Assert.Equal(databaseId.ToString(), i.DatabaseId));

        // Filter by unknown databaseId → empty
        var empty = await session.Client.GetFromJsonAsync<HistoryBody>(
            $"/api/query/history?databaseId={Guid.NewGuid()}", ct);
        Assert.NotNull(empty);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task GetHistory_FiltersByDateRange()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var sql = $"SELECT 1 -- date-filter-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql });
        await session.Client.SendAsync(req, ct);

        // Far-future `to` → no results
        var noResults = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?to=2000-01-01", ct);
        Assert.NotNull(noResults);
        Assert.DoesNotContain(noResults.Items, i => i.QueryText == sql);

        // Far-past `from` still returns the entry
        var withResults = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?from=2020-01-01", ct);
        Assert.NotNull(withResults);
        Assert.Contains(withResults.Items, i => i.QueryText == sql);
    }

    // ── Private DTO records (mirrors the JSON the API will return) ──────────

    private sealed record HistoryBody(HistoryItem[] Items);
    private sealed record HistoryItem(string QueryText, string Status, string? DatabaseId, string? DatabaseDisplayName, string? UserId, string? UserName, string ExecutedAt);
    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
cd /Users/voltendron/Projects/sluice-base
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~QueryHistoryEndpointTests" 2>&1 | tail -20
```

Expected: tests fail with `404 Not Found` or compile errors about missing `QueryHistoryResponse`.

---

## Task 3: Implement the GetHistory Endpoint

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Regenerate: `src/SluiceBase.Api/openapi.json` (via `dotnet build`)
- Regenerate: `src/frontend/src/api/schema.ts` (via `npm run gen:api`)

- [ ] **Step 1: Add the GetHistory handler to QueryEndpoints.Map**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, add to the `Map` method after `MapPost`:

```csharp
public static void Map(IEndpointRouteBuilder app)
{
    app.MapPost("/api/query", ExecuteQuery)
        .RequireAuthorization(Permissions.QueryExecute)
        .WithName("ExecuteQuery");

    app.MapGet("/api/query/history", GetHistory)          // ← add
        .RequireAuthorization(Permissions.QueryExecute)
        .WithName("GetQueryHistory");
}
```

- [ ] **Step 2: Add the GetHistory handler method**

Add this method to the `QueryEndpoints` class (before the existing record definitions at the bottom):

```csharp
private static async Task<Results<Ok<QueryHistoryResponse>, BadRequest<string>>> GetHistory(
    DateTimeOffset? @from,
    DateTimeOffset? to,
    string? databaseId,
    string? status,
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    CancellationToken ct)
{
    if (@from.HasValue && to.HasValue && @from > to)
        return TypedResults.BadRequest("'from' must be before 'to'.");

    var user = await currentUser.GetAsync(ct);
    var hasAudit = user?.HasPermission(Permissions.QueryAudit) ?? false;

    DatabaseId? filterDb = databaseId is not null && Guid.TryParse(databaseId, out var dbGuid)
        ? DatabaseId.From(dbGuid)
        : null;

    QueryLogStatus? filterStatus = status is not null
        && Enum.TryParse<QueryLogStatus>(status, ignoreCase: true, out var parsedStatus)
        ? parsedStatus
        : null;

    var items = await db.QueryLogs
        .AsNoTracking()
        .Where(q => hasAudit || q.UserId == user!.Id)
        .Where(q => @from == null || q.ExecutedAt >= @from)
        .Where(q => to == null || q.ExecutedAt <= to)
        .Where(q => filterDb == null || q.DatabaseId == filterDb)
        .Where(q => filterStatus == null || q.Status == filterStatus)
        .OrderByDescending(q => q.ExecutedAt)
        .Take(100)
        .Select(q => new QueryHistoryItem(
            q.Id,
            q.DatabaseId,
            db.Databases.Where(d => d.Id == q.DatabaseId).Select(d => d.DisplayName).FirstOrDefault(),
            q.QueryText,
            q.Status,
            q.ExecutedAt,
            q.DurationMs,
            q.RowCount,
            q.Error,
            q.UserId,
            db.Users.Where(u => u.Id == q.UserId).Select(u => u.Name ?? u.Email).FirstOrDefault()
        ))
        .ToListAsync(ct);

    return TypedResults.Ok(new QueryHistoryResponse(items));
}
```

- [ ] **Step 3: Add the response records**

At the bottom of `QueryEndpoints.cs`, after the existing `QueryResponse` record, add:

```csharp
public sealed record QueryHistoryItem(
    QueryLogId Id,
    DatabaseId? DatabaseId,
    string? DatabaseDisplayName,
    string QueryText,
    QueryLogStatus Status,
    DateTimeOffset ExecutedAt,
    int? DurationMs,
    int? RowCount,
    string? Error,
    UserId? UserId,
    string? UserName);

public sealed record QueryHistoryResponse(IReadOnlyList<QueryHistoryItem> Items);
```

Add the missing using at the top:

```csharp
using SluiceBase.Core.Users;
```

- [ ] **Step 4: Build the project to regenerate openapi.json**

```bash
cd /Users/voltendron/Projects/sluice-base
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeds. `src/SluiceBase.Api/openapi.json` is updated with the new `/api/query/history` endpoint.

- [ ] **Step 5: Regenerate schema.ts**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend
npm run gen:api
```

Expected: `src/frontend/src/api/schema.ts` is updated with the new path types.

- [ ] **Step 6: Run integration tests to verify they pass**

```bash
cd /Users/voltendron/Projects/sluice-base
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~QueryHistoryEndpointTests" 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Permissions/Permissions.cs \
        src/SluiceBase.Api/Endpoints/QueryEndpoints.cs \
        src/SluiceBase.Api/openapi.json \
        src/frontend/src/api/schema.ts \
        tests/IntegrationTests/QueryHistoryEndpointTests.cs
git commit -m "feat: add GET /api/query/history endpoint with audit permission support"
```

---

## Task 4: Frontend Hook — useQueryHistory

**Files:**
- Create: `src/frontend/src/api/__tests__/query-history-hooks.test.ts`
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Write the failing unit tests**

Create `src/frontend/src/api/__tests__/query-history-hooks.test.ts`:

```ts
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useQueryHistory } from "@/api/hooks";

vi.mock("@/api/client", () => ({
  apiRequest: vi.fn(),
  ApiError: class ApiError extends Error {
    constructor(
      public status: number,
      public body: unknown,
    ) {
      super(`API ${status}`);
    }
  },
}));

const { apiRequest } = await import("@/api/client");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useQueryHistory", () => {
  it("fetches GET /api/query/history with no params when filters are empty", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(() => useQueryHistory({}), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/query/history");
  });

  it("appends status filter to the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(() => useQueryHistory({ status: "Error" }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/query/history?status=Error");
  });

  it("appends multiple filters to the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(
      () => useQueryHistory({ status: "Success", databaseId: "db-123" }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const calledUrl = vi.mocked(apiRequest).mock.calls[0][0] as string;
    expect(calledUrl).toContain("status=Success");
    expect(calledUrl).toContain("databaseId=db-123");
  });

  it("includes filter values in the cache key", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });

    const { result: r1 } = renderHook(() => useQueryHistory({ status: "Error" }), { wrapper });
    const { result: r2 } = renderHook(() => useQueryHistory({ status: "Success" }), { wrapper });

    await waitFor(() => expect(r1.current.isSuccess).toBe(true));
    await waitFor(() => expect(r2.current.isSuccess).toBe(true));

    // Two different fetches because cache keys differ
    expect(apiRequest).toHaveBeenCalledTimes(2);
  });

  it("omits undefined filter values from the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(
      () => useQueryHistory({ status: undefined, from: "2024-01-01" }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const calledUrl = vi.mocked(apiRequest).mock.calls[0][0] as string;
    expect(calledUrl).toContain("from=2024-01-01");
    expect(calledUrl).not.toContain("status=");
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend
npm test -- query-history-hooks 2>&1 | tail -20
```

Expected: FAIL — `useQueryHistory` is not exported from `@/api/hooks`.

- [ ] **Step 3: Add the hook to hooks.ts**

At the bottom of `src/frontend/src/api/hooks.ts`, add:

```ts
// ── Query history ─────────────────────────────────────────────────────────

export interface QueryHistoryItem {
  id: string;
  databaseId: string | null;
  databaseDisplayName: string | null;
  queryText: string;
  status: string;
  executedAt: string;
  durationMs: number | null;
  rowCount: number | null;
  error: string | null;
  userId: string | null;
  userName: string | null;
}

export interface QueryHistoryFilters {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
}

export function useQueryHistory(filters: QueryHistoryFilters) {
  return useQuery({
    queryKey: ["query", "history", filters] as const,
    queryFn: () => {
      const params = new URLSearchParams();
      if (filters.from) params.set("from", filters.from);
      if (filters.to) params.set("to", filters.to);
      if (filters.databaseId) params.set("databaseId", filters.databaseId);
      if (filters.status) params.set("status", filters.status);
      const qs = params.toString();
      return apiRequest<void, { items: QueryHistoryItem[] }>(
        qs ? `/api/query/history?${qs}` : "/api/query/history",
      );
    },
  });
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend
npm test -- query-history-hooks 2>&1 | tail -20
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/hooks.ts \
        src/frontend/src/api/__tests__/query-history-hooks.test.ts
git commit -m "feat: add useQueryHistory hook"
```

---

## Task 5: Restructure Query Route

**Files:**
- Create: `src/frontend/src/routes/_authed/query/index.tsx` (copy of `query.tsx`)
- Delete: `src/frontend/src/routes/_authed/query.tsx`

This is a pure file-system move. TanStack Router's Vite plugin auto-regenerates `routeTree.gen.ts` on the next dev server start or build, so do not manually edit `routeTree.gen.ts`.

- [ ] **Step 1: Create the query directory and copy the file**

```bash
mkdir -p src/frontend/src/routes/_authed/query
cp src/frontend/src/routes/_authed/query.tsx \
   src/frontend/src/routes/_authed/query/index.tsx
```

- [ ] **Step 2: Delete the old file**

```bash
rm src/frontend/src/routes/_authed/query.tsx
```

- [ ] **Step 3: Verify the TypeScript build still compiles**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend
npx tsc --noEmit 2>&1 | head -30
```

Expected: No errors (the route tree will regenerate automatically at dev/build time).

- [ ] **Step 4: Run the existing frontend unit tests**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend
npm test 2>&1 | tail -20
```

Expected: All existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/routes/_authed/query/index.tsx
git rm src/frontend/src/routes/_authed/query.tsx
git commit -m "refactor: move query route to query/index.tsx for nested routing"
```

---

## Task 6: Build the History Page and Update Nav

**Files:**
- Create: `src/frontend/src/routes/_authed/query/history.tsx`
- Modify: `src/frontend/src/routes/_authed.tsx`

- [ ] **Step 1: Create the history page**

Create `src/frontend/src/routes/_authed/query/history.tsx`:

```tsx
import {
  ActionIcon,
  Alert,
  Badge,
  Code,
  Group,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { IconCopy } from "@tabler/icons-react";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import { meQueryOptions, useQueryHistory, useServers } from "@/api/hooks";
import type { QueryHistoryItem, QueryHistoryFilters } from "@/api/hooks";
import { useHasPermission } from "@/auth/permission";

type HistorySearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
};

export const Route = createFileRoute("/_authed/query/history")({
  validateSearch: (search: Record<string, unknown>): HistorySearch => ({
    from: typeof search.from === "string" ? search.from : undefined,
    to: typeof search.to === "string" ? search.to : undefined,
    databaseId: typeof search.databaseId === "string" ? search.databaseId : undefined,
    status: typeof search.status === "string" ? search.status : undefined,
  }),
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryHistoryPage,
});

const STATUS_COLOR: Record<string, string> = {
  Success: "teal",
  Error: "red",
  Timeout: "orange",
  Unknown: "gray",
};

const STATUS_OPTIONS = [
  { value: "", label: "All statuses" },
  { value: "Success", label: "Success" },
  { value: "Error", label: "Error" },
  { value: "Timeout", label: "Timeout" },
];

function QueryHistoryPage() {
  const search = Route.useSearch();
  const navigate = useNavigate();
  const canAudit = useHasPermission("query:audit");
  const [userSearch, setUserSearch] = useState("");

  const servers = useServers();
  const filters: QueryHistoryFilters = {
    from: search.from,
    to: search.to,
    databaseId: search.databaseId,
    status: search.status,
  };
  const history = useQueryHistory(filters);

  const databaseOptions = [
    { value: "", label: "All databases" },
    ...(servers.data?.servers ?? []).flatMap((s) =>
      s.databases.map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
    ),
  ];

  function setFilter(key: keyof HistorySearch, value: string | undefined) {
    void navigate({
      to: "/query/history",
      search: (prev: HistorySearch) => ({ ...prev, [key]: value || undefined }),
    });
  }

  const allItems = history.data?.items ?? [];
  const displayedItems = canAudit && userSearch
    ? allItems.filter((i) =>
        (i.userName ?? "").toLowerCase().includes(userSearch.toLowerCase()),
      )
    : allItems;

  return (
    <Stack gap="md">
      <Title order={2}>Query History</Title>

      <Group gap="sm" wrap="wrap">
        <TextInput
          type="date"
          label="From"
          size="sm"
          value={search.from ?? ""}
          onChange={(e) => setFilter("from", e.currentTarget.value)}
          style={{ width: 160 }}
        />
        <TextInput
          type="date"
          label="To"
          size="sm"
          value={search.to ?? ""}
          onChange={(e) => setFilter("to", e.currentTarget.value)}
          style={{ width: 160 }}
        />
        <Select
          label="Database"
          size="sm"
          data={databaseOptions}
          value={search.databaseId ?? ""}
          onChange={(v) => setFilter("databaseId", v ?? undefined)}
          style={{ width: 240 }}
        />
        <Select
          label="Status"
          size="sm"
          data={STATUS_OPTIONS}
          value={search.status ?? ""}
          onChange={(v) => setFilter("status", v ?? undefined)}
          style={{ width: 160 }}
        />
        {canAudit && (
          <TextInput
            label="User"
            placeholder="Filter by name…"
            size="sm"
            value={userSearch}
            onChange={(e) => setUserSearch(e.currentTarget.value)}
            style={{ width: 200 }}
          />
        )}
      </Group>

      {history.isPending && (
        <Text c="dimmed" size="sm">Loading…</Text>
      )}

      {history.isError && (
        <Alert color="red" title="Failed to load history">
          Could not reach the server. Check your connection and try again.
        </Alert>
      )}

      {history.data && displayedItems.length === 0 && (
        <Text c="dimmed" size="sm">No entries match the current filters.</Text>
      )}

      {history.data && displayedItems.length > 0 && (
        <ScrollArea type="auto">
          <Table striped withTableBorder highlightOnHover fz="sm" style={{ whiteSpace: "nowrap" }}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Status</Table.Th>
                <Table.Th>Database</Table.Th>
                {canAudit && <Table.Th>User</Table.Th>}
                <Table.Th>SQL</Table.Th>
                <Table.Th>Executed At</Table.Th>
                <Table.Th>Duration</Table.Th>
                <Table.Th>Rows</Table.Th>
                <Table.Th></Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {displayedItems.map((item) => (
                <HistoryRow key={item.id} item={item} canAudit={canAudit} />
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Stack>
  );
}

function HistoryRow({ item, canAudit }: { item: QueryHistoryItem; canAudit: boolean }) {
  function copySql() {
    void navigator.clipboard.writeText(item.queryText).then(() => {
      notifications.show({ message: "SQL copied to clipboard", color: "teal" });
    }).catch(() => {
      // Clipboard API unavailable — silent no-op per spec
    });
  }

  const sqlPreview = item.queryText.length > 80
    ? `${item.queryText.slice(0, 80)}…`
    : item.queryText;

  return (
    <Table.Tr>
      <Table.Td>
        <Badge color={STATUS_COLOR[item.status] ?? "gray"} size="sm">
          {item.status}
        </Badge>
      </Table.Td>
      <Table.Td>{item.databaseDisplayName ?? "—"}</Table.Td>
      {canAudit && <Table.Td>{item.userName ?? "—"}</Table.Td>}
      <Table.Td style={{ maxWidth: 400 }}>
        <Code fz="xs">{sqlPreview}</Code>
      </Table.Td>
      <Table.Td>
        <Text size="xs">
          {new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" })
            .format(new Date(item.executedAt))}
        </Text>
      </Table.Td>
      <Table.Td>
        {item.durationMs != null ? (
          <Text size="xs">{item.durationMs} ms</Text>
        ) : "—"}
      </Table.Td>
      <Table.Td>
        {item.rowCount != null ? (
          <Text size="xs">{item.rowCount}</Text>
        ) : "—"}
      </Table.Td>
      <Table.Td>
        <ActionIcon size="sm" variant="subtle" onClick={copySql} aria-label="Copy SQL">
          <IconCopy size={14} />
        </ActionIcon>
      </Table.Td>
    </Table.Tr>
  );
}
```

- [ ] **Step 2: Update the nav in `_authed.tsx`**

In `src/frontend/src/routes/_authed.tsx`, change the imports to add `IconHistory`:

```tsx
import {
  IconArrowsExchange,
  IconHistory,          // ← add
  IconLogout,
  IconMoon,
  IconServer,
  IconShieldLock,
  IconSun,
  IconTerminal2,
} from "@tabler/icons-react";
```

Replace the single Query `NavLink`:

```tsx
{canQuery && (
  <NavLink
    label="Query"
    leftSection={<IconTerminal2 size={16} />}
    component={Link}
    to="/query"
    active={location.pathname === "/query"}
  />
)}
```

With a nested group:

```tsx
{canQuery && (
  <NavLink
    label="Query"
    leftSection={<IconTerminal2 size={16} />}
    active={location.pathname.startsWith("/query")}
    defaultOpened={location.pathname.startsWith("/query")}
  >
    <NavLink
      label="Editor"
      component={Link}
      to="/query"
      active={location.pathname === "/query"}
      pl="xl"
    />
    <NavLink
      label="History"
      leftSection={<IconHistory size={16} />}
      component={Link}
      to="/query/history"
      active={location.pathname === "/query/history"}
      pl="xl"
    />
  </NavLink>
)}
```

- [ ] **Step 3: Run all frontend unit tests**

```bash
cd /Users/voltendron/Projects/sluice-base/src/frontend
npm test 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 4: Start the dev server and smoke-test manually**

```bash
cd /Users/voltendron/Projects/sluice-base
dotnet run --project src/AppHost
```

Open the frontend in a browser. Verify:
1. The "Query" nav item is now a collapsible group with "Editor" and "History" sub-items.
2. Navigating to `/query` loads the existing query editor unchanged.
3. Navigating to `/query/history` loads the history page.
4. Running a query in the editor, then opening History, shows the new entry.
5. Filtering by status, database, and date range narrows the results.
6. Users without `query:audit` do not see the User filter column.
7. Users with `query:audit` see the User column and the client-side user filter input.
8. Clicking the Copy button copies the SQL and shows a notification.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/routes/_authed/query/history.tsx \
        src/frontend/src/routes/_authed.tsx
git commit -m "feat: add query history page with filters and copy-SQL button"
```
