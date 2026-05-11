# Schema Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the schema browser sidebar and `/query` page shell — a two-panel page with a server selector + expandable schema tree on the left and a stub right panel (for sub-project 5).

**Architecture:** A new `GET /api/schema/{serverId}` endpoint reads `information_schema.columns` via `ITargetEngine.GetSchemaAsync` (read credential only), returns a `SchemaTree` record, and is gated on `query:execute`. The React `/query` route renders a fixed-width left sidebar with a Mantine `Select` for server selection and an expandable tree of schemas → tables → columns, with the right panel stubbed for sub-project 5.

**Tech Stack:** .NET 10 / ASP.NET Minimal API, Npgsql, `SluiceBase.Core`/`SluiceBase.Api` project structure, xUnit v3 integration tests via Aspire.Hosting.Testing, React 19 / TypeScript / Mantine 9 / TanStack Query v5 / TanStack Router v1, Vitest, Playwright.

---

## File Map

| File | Action |
|---|---|
| `src/SluiceBase.Core/Schema/SchemaTree.cs` | **Create** — `SchemaTree`, `SchemaInfo`, `TableInfo`, `ColumnInfo` records |
| `src/SluiceBase.Core/Targets/ITargetEngine.cs` | **Modify** — add `GetSchemaAsync` to interface |
| `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` | **Modify** — implement `GetSchemaAsync` via `information_schema.columns` |
| `src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs` | **Create** — `GET /api/schema/{serverId}` handler |
| `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` | **Modify** — register `SchemaEndpoints` |
| `tests/IntegrationTests/TargetEngineTests.cs` | **Modify** — add `GetSchema` test |
| `tests/IntegrationTests/SchemaEndpointTests.cs` | **Create** — 4 endpoint integration tests |
| `src/frontend/src/api/hooks.ts` | **Modify** — add `useSchema` hook and `SchemaResponse` type |
| `src/frontend/src/routes/_authed/query.tsx` | **Create** — `/query` page with schema sidebar + stub right panel |
| `src/frontend/src/routes/_authed.tsx` | **Modify** — add "Query" `NavLink` |
| `src/frontend/src/api/__tests__/schema-hooks.test.ts` | **Create** — Vitest tests for `useSchema` |
| `src/frontend/e2e/query-schema.spec.ts` | **Create** — Playwright E2E spec |

---

## Task 1: SchemaTree domain model

**Files:**
- Create: `src/SluiceBase.Core/Schema/SchemaTree.cs`

- [ ] **Step 1: Create `SchemaTree.cs`**

```csharp
namespace SluiceBase.Core.Schema;

public sealed record SchemaTree(IReadOnlyList<SchemaInfo> Schemas);
public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);
public sealed record TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns);
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable);
```

- [ ] **Step 2: Verify the solution builds**

Run from the repo root:
```bash
dotnet build SluiceBase.slnx
```
Expected: build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Schema/SchemaTree.cs
git commit -m "feat: add SchemaTree domain records to Core"
```

---

## Task 2: Extend `ITargetEngine` and add a compiling stub

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`

- [ ] **Step 1: Add `GetSchemaAsync` to the interface**

Replace the contents of `src/SluiceBase.Core/Targets/ITargetEngine.cs`:

```csharp
using SluiceBase.Core.Schema;

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
}

public sealed record ConnectivityResult(bool Ok, string? Error);
```

- [ ] **Step 2: Add a stub implementation to `PostgresTargetEngine`**

Add the following method to `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` (after `TestConnectionAsync`):

```csharp
public Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct) =>
    throw new NotImplementedException();
```

Also add the required `using` at the top of the file:

```csharp
using SluiceBase.Core.Schema;
```

- [ ] **Step 3: Verify the solution builds**

```bash
dotnet build SluiceBase.slnx
```
Expected: build succeeds with 0 errors (stub satisfies the interface).

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Core/Targets/ITargetEngine.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs
git commit -m "feat: add GetSchemaAsync to ITargetEngine with stub in PostgresTargetEngine"
```

---

## Task 3: Implement `PostgresTargetEngine.GetSchemaAsync`

**Files:**
- Modify: `tests/IntegrationTests/TargetEngineTests.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`

- [ ] **Step 1: Add a failing test to `TargetEngineTests.cs`**

Append this test to the class (after `TargetEngine_Postgres_TestConnection_Fails_OnBadConnString`):

```csharp
[Fact]
public async Task TargetEngine_Postgres_GetSchema_ReturnsPublicSchemaForBlue()
{
    var connectionString = await factory.InitialisedApp
        .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

    Assert.NotNull(connectionString);

    var engine = new PostgresTargetEngine();
    var tree = await engine.GetSchemaAsync(
        connectionString,
        TestContext.Current.CancellationToken);

    Assert.NotNull(tree);
    Assert.DoesNotContain(tree.Schemas, s => s.Name == "information_schema");
    Assert.DoesNotContain(tree.Schemas, s => s.Name == "pg_catalog");
    var publicSchema = Assert.Single(tree.Schemas, s => s.Name == "public");
    Assert.Contains(publicSchema.Tables, t => t.Name == "users");
    var usersTable = publicSchema.Tables.Single(t => t.Name == "users");
    Assert.NotEmpty(usersTable.Columns);
    Assert.All(usersTable.Columns, c => Assert.NotEmpty(c.DataType));
}
```

Also add `using SluiceBase.Core.Schema;` to the top of `TargetEngineTests.cs`.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test SluiceBase.slnx --filter "TargetEngine_Postgres_GetSchema_ReturnsPublicSchemaForBlue"
```
Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Implement `GetSchemaAsync` in `PostgresTargetEngine`**

Replace the stub body in `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`. The full file should be:

```csharp
using Npgsql;
using SluiceBase.Core.Schema;
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
            ORDER BY table_schema, table_name, ordinal_position
            """;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<(string Schema, string Table, string Column, string DataType, bool IsNullable)>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
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
                sg.GroupBy(r => r.Table)
                    .Select(tg => new TableInfo(
                        tg.Key,
                        tg.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable))
                            .ToList()))
                    .ToList()))
            .ToList();

        return new SchemaTree(schemas);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test SluiceBase.slnx --filter "TargetEngine_Postgres_GetSchema_ReturnsPublicSchemaForBlue"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/TargetEngineTests.cs
git commit -m "feat: implement GetSchemaAsync in PostgresTargetEngine"
```

---

## Task 4: `SchemaEndpoints` + integration tests

**Files:**
- Create: `tests/IntegrationTests/SchemaEndpointTests.cs`
- Create: `src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`

- [ ] **Step 1: Write the integration tests**

Create `tests/IntegrationTests/SchemaEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schema;

namespace IntegrationTests;

public class SchemaEndpointTests(SluiceBaseStackFactory factory)
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

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
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

        var serverName = $"sch-{Guid.NewGuid():N}"[..24];
        using var createReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(
                serverName, "postgres",
                blueBuilder.Host!, blueBuilder.Port, "appdb",
                "reader_blue", "reader_blue",
                null, null));
        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var server = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        return (session, server!.Id.Value.ToString());
    }

    [Fact]
    public async Task GetSchema_ReturnsTree_ForBlueServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync($"/api/schema/{serverId}", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var tree = await resp.Content.ReadFromJsonAsync<SchemaTree>(ct);
        Assert.NotNull(tree);
        Assert.DoesNotContain(tree.Schemas, s => s.Name == "information_schema");
        var publicSchema = Assert.Single(tree.Schemas, s => s.Name == "public");
        Assert.Contains(publicSchema.Tables, t => t.Name == "users");
        var usersTable = publicSchema.Tables.Single(t => t.Name == "users");
        Assert.NotEmpty(usersTable.Columns);
        Assert.All(usersTable.Columns, c => Assert.NotEmpty(c.DataType));
    }

    [Fact]
    public async Task GetSchema_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/schema/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Returns403_ForBob()
    {
        using var session = await LoginHelper.SignInAsync(
            "bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/schema/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Returns404_ForUnknownServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetAsync($"/api/schema/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
```

- [ ] **Step 2: Run the tests to verify they fail to build** (endpoint doesn't exist yet)

```bash
dotnet build SluiceBase.slnx
```
Expected: build error — `SchemaEndpoints` does not exist.

- [ ] **Step 3: Create `SchemaEndpoints.cs`**

Create `src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schema;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class SchemaEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/schema/{serverId}", GetSchema)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("GetSchema");
    }

    private static async Task<Results<Ok<SchemaTree>, NotFound>> GetSchema(
        ServerId serverId,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == serverId, ct);
        if (server is null) return TypedResults.NotFound();

        var connectionString = await connectionFactory
            .GetConnectionStringAsync(serverId, CredentialKind.Read, ct);

        var tree = await targetEngine.GetSchemaAsync(connectionString, ct);
        return TypedResults.Ok(tree);
    }
}
```

- [ ] **Step 4: Register `SchemaEndpoints` in `EndpointMapper`**

In `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`, add `SchemaEndpoints.Map(app);` after `ServerEndpoints.Map(app);`:

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

        if (app.Environment.IsDevelopment())
        {
            DevelopmentEndpoints.Map(app);
        }

        return app;
    }
}
```

- [ ] **Step 5: Build to confirm no compilation errors**

```bash
dotnet build SluiceBase.slnx
```
Expected: 0 errors.

- [ ] **Step 6: Run the integration tests**

```bash
dotnet test SluiceBase.slnx --filter "SchemaEndpointTests"
```
Expected: all 4 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add tests/IntegrationTests/SchemaEndpointTests.cs \
        src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs \
        src/SluiceBase.Api/Endpoints/EndpointMapper.cs
git commit -m "feat: add GET /api/schema/{serverId} endpoint with query:execute gate"
```

---

## Task 5: Regenerate OpenAPI spec and TypeScript types

**Files:**
- Modify: `src/SluiceBase.Api/openapi.json` (generated)
- Modify: `src/frontend/src/api/schema.ts` (generated)

- [ ] **Step 1: Regenerate `openapi.json`**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```
Expected: `src/SluiceBase.Api/openapi.json` is updated — `GET /api/schema/{serverId}` now appears in the spec.

- [ ] **Step 2: Verify the new endpoint appears in `openapi.json`**

```bash
grep -c "schema/{serverId}" src/SluiceBase.Api/openapi.json
```
Expected: output is `1` or greater.

- [ ] **Step 3: Regenerate TypeScript types**

```bash
cd src/frontend && npm run gen:api
```
Expected: `src/frontend/src/api/schema.ts` is regenerated with a `/api/schema/{serverId}` path entry.

- [ ] **Step 4: Verify the new path type exists in `schema.ts`**

```bash
grep "api/schema" src/frontend/src/api/schema.ts | head -3
```
Expected: lines containing `/api/schema/{serverId}`.

- [ ] **Step 5: Commit**

```bash
cd ../..
git add src/SluiceBase.Api/openapi.json src/frontend/src/api/schema.ts
git commit -m "chore: regenerate openapi.json and schema.ts for schema endpoint"
```

---

## Task 6: `useSchema` hook and Vitest tests

**Files:**
- Create: `src/frontend/src/api/__tests__/schema-hooks.test.ts`
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Write the failing Vitest tests**

Create `src/frontend/src/api/__tests__/schema-hooks.test.ts`:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useSchema } from "@/api/hooks";

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

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const { apiRequest } = await import("@/api/client");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useSchema", () => {
  it("is disabled and does not fetch when serverId is null", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ schemas: [] });

    const { result } = renderHook(() => useSchema(null), { wrapper });
    await new Promise((r) => setTimeout(r, 50));

    expect(apiRequest).not.toHaveBeenCalled();
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("fetches /api/schema/{serverId} and uses correct query key", async () => {
    const mockTree = {
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "users",
              columns: [{ name: "id", dataType: "integer", isNullable: false }],
            },
          ],
        },
      ],
    };
    vi.mocked(apiRequest).mockResolvedValue(mockTree);

    const { result } = renderHook(() => useSchema("server-abc"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(apiRequest).toHaveBeenCalledWith("/api/schema/server-abc");
    expect(result.current.data).toEqual(mockTree);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail** (`useSchema` doesn't exist yet)

```bash
cd src/frontend && npm run test -- schema-hooks
```
Expected: FAIL — `useSchema is not exported from @/api/hooks`.

- [ ] **Step 3: Add `useSchema` to `hooks.ts`**

Append the following section at the end of `src/frontend/src/api/hooks.ts`:

```typescript
// ── Schema browser ────────────────────────────────────────────────────────────

export type SchemaResponse =
  paths["/api/schema/{serverId}"]["get"]["responses"][200]["content"]["application/json"];

export function useSchema(serverId: string | null) {
  return useQuery({
    queryKey: ["schema", serverId] as const,
    queryFn: () => apiRequest<void, SchemaResponse>(`/api/schema/${serverId}`),
    enabled: serverId !== null,
    staleTime: 5 * 60 * 1000,
  });
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
npm run test -- schema-hooks
```
Expected: both tests PASS.

- [ ] **Step 5: Commit**

```bash
cd ../..
git add src/frontend/src/api/hooks.ts src/frontend/src/api/__tests__/schema-hooks.test.ts
git commit -m "feat: add useSchema hook with 5-minute staleTime"
```

---

## Task 7: `/query` route with schema sidebar

**Files:**
- Create: `src/frontend/src/routes/_authed/query.tsx`

- [ ] **Step 1: Create `query.tsx`**

Create `src/frontend/src/routes/_authed/query.tsx`:

```tsx
import {
  Alert,
  Box,
  Center,
  Code,
  Flex,
  Group,
  NavLink,
  Select,
  Skeleton,
  Stack,
  Text,
} from "@mantine/core";
import { IconChevronDown, IconChevronRight, IconDatabase, IconTable } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import { meQueryOptions, useSchema, useServers } from "@/api/hooks";
import type { SchemaResponse } from "@/api/hooks";

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

  const serverOptions = (servers.data?.servers ?? []).map((s) => ({
    value: s.id,
    label: s.name,
  }));

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
          <SchemaSidebar schema={schema} />
        </Stack>
      </Box>
      <Box flex={1} style={{ overflow: "auto" }}>
        <Center h="100%">
          <Text c="dimmed">Query editor coming soon</Text>
        </Center>
      </Box>
    </Flex>
  );
}

function SchemaSidebar({ schema }: { schema: ReturnType<typeof useSchema> }) {
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
                schemaExpanded ? (
                  <IconChevronDown size={12} />
                ) : (
                  <IconChevronRight size={12} />
                )
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
                      onClick={() => toggleTable(tableKey)}
                      pl="lg"
                      active={false}
                    />
                    {tableExpanded && (
                      <Stack gap={0} pl="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-xs))">
                        {t.columns.map((c) => (
                          <Group key={c.name} gap="xs" px="xs" py={2} wrap="nowrap">
                            <Text size="xs" style={{ minWidth: 0 }} truncate>
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

- [ ] **Step 2: Verify TypeScript build**

```bash
cd src/frontend && npm run build
```
Expected: build succeeds — no TypeScript or ESLint errors. The route tree (`routeTree.gen.ts`) will be regenerated by TanStack Router to include `/_authed/query`.

- [ ] **Step 3: Commit**

```bash
cd ../..
git add src/frontend/src/routes/_authed/query.tsx src/frontend/src/routeTree.gen.ts
git commit -m "feat: add /query route with schema sidebar and stub right panel"
```

---

## Task 8: "Query" navbar link

**Files:**
- Modify: `src/frontend/src/routes/_authed.tsx`

- [ ] **Step 1: Add the Query nav link to `_authed.tsx`**

In `src/frontend/src/routes/_authed.tsx`, make the following changes:

1. Add `IconTerminal2` to the tabler icons import:
```typescript
import {
  IconHeartRateMonitor,
  IconHome,
  IconLogout,
  IconMoon,
  IconServer,
  IconShieldLock,
  IconSun,
  IconTerminal2,
} from "@tabler/icons-react";
```

2. Add a `canQuery` variable after `isServerAdmin`:
```typescript
const canQuery = useHasPermission("query:execute");
```

3. Add the "Query" `NavLink` before the "Servers" link (inside `AppShell.Navbar`):
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

The full updated `AppShell.Navbar` section should look like:

```tsx
<AppShell.Navbar p="sm">
  <NavLink
    label="Home"
    leftSection={<IconHome size={16} />}
    component={Link}
    to="/"
    active={location.pathname === "/"}
  />
  <NavLink
    label="Health"
    leftSection={<IconHeartRateMonitor size={16} />}
    component={Link}
    to="/health"
    active={location.pathname === "/health"}
  />
  {canQuery && (
    <NavLink
      label="Query"
      leftSection={<IconTerminal2 size={16} />}
      component={Link}
      to="/query"
      active={location.pathname === "/query"}
    />
  )}
  {isServerAdmin && (
    <NavLink
      label="Servers"
      leftSection={<IconServer size={16} />}
      component={Link}
      to="/server"
      active={location.pathname === "/server"}
    />
  )}
  {isAdmin && (
    <NavLink
      label="Permission"
      leftSection={<IconShieldLock size={16} />}
      component={Link}
      to="/permission"
      active={location.pathname === "/permission"}
    />
  )}
</AppShell.Navbar>
```

- [ ] **Step 2: Verify TypeScript build**

```bash
cd src/frontend && npm run build
```
Expected: build succeeds.

- [ ] **Step 3: Run full test suite**

```bash
npm run test
```
Expected: all Vitest tests pass (including new schema-hooks tests).

- [ ] **Step 4: Commit**

```bash
cd ../..
git add src/frontend/src/routes/_authed.tsx
git commit -m "feat: add Query navbar link visible to users with query:execute"
```

---

## Task 9: Playwright E2E spec

**Files:**
- Create: `src/frontend/e2e/query-schema.spec.ts`

- [ ] **Step 1: Create `query-schema.spec.ts`**

Create `src/frontend/e2e/query-schema.spec.ts`:

```typescript
import { expect, test } from "@playwright/test";

test.describe("Query schema browser — alice", () => {
  test("can browse schema of a registered server", async ({ page }) => {
    // Sign in as alice
    await page.goto("http://localhost:5173");
    await page.waitForURL(/realms\/sluicebase/);
    await page.fill('[name="username"]', "alice");
    await page.fill('[name="password"]', "dev");
    await page.click('[type="submit"]');
    await page.waitForURL("http://localhost:5173/");

    // Grant query:execute via the Permission page
    await page.getByRole("link", { name: "Permission" }).click();
    await expect(page).toHaveURL("/permission");

    const aliceRow = page.getByRole("row").filter({ hasText: "alice@example.com" });
    await expect(aliceRow).toBeVisible();
    const querySwitch = aliceRow.getByRole("switch", { name: /Run read queries/i });
    if (!(await querySwitch.isChecked())) {
      await querySwitch.click({ force: true });
      await page.reload({ waitUntil: "domcontentloaded" });
    }

    // Navigate to /query
    await page.goto("http://localhost:5173/query");
    await expect(page.getByPlaceholder("Select a server")).toBeVisible();

    // Select Blue server from the dropdown
    await page.getByPlaceholder("Select a server").click();
    await page.getByRole("option", { name: "Blue" }).click();

    // Schema tree should populate — public schema visible
    await expect(page.getByText("public")).toBeVisible({ timeout: 10_000 });

    // Expand the public schema
    await page.getByText("public").click();

    // At least one table visible (Blue has users, orders, products)
    await expect(page.getByText("users")).toBeVisible();

    // Expand the users table
    await page.getByText("users").click();

    // At least one column with a data type visible
    await expect(page.getByText("id")).toBeVisible();
    await expect(page.getByText("integer")).toBeVisible();
  });

  test("bob is redirected to / when navigating to /query", async ({ page, context }) => {
    await context.clearCookies();
    await page.goto("http://localhost:5173");
    await page.waitForURL(/realms\/sluicebase/);
    await page.fill('[name="username"]', "bob");
    await page.fill('[name="password"]', "dev");
    await page.click('[type="submit"]');
    await page.waitForURL("http://localhost:5173/");

    await page.goto("http://localhost:5173/query");
    await expect(page).toHaveURL("http://localhost:5173/");
  });
});
```

- [ ] **Step 2: Commit**

```bash
git add src/frontend/e2e/query-schema.spec.ts
git commit -m "test(e2e): add query-schema Playwright spec"
```

---

## Final verification checklist

- [ ] `dotnet build SluiceBase.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test SluiceBase.slnx` — all tests pass (prior + new)
- [ ] `cd src/frontend && npm run build` — TypeScript strict + ESLint clean
- [ ] `npm run test` — all Vitest tests pass
- [ ] (Manual / CI) `npm run test:e2e` — `query-schema.spec.ts` passes with Aspire running and Blue/Green seeded
