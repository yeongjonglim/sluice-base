# SluiceBase Schema Browser — Design

**Date:** 2026-05-07
**Status:** Proposed
**Sub-project:** 4 of 6 (Schema Browser)
**Predecessor:** 3. Server Registry (`2026-05-06-server-registry-design.md`)
**Successor sub-projects:** 5. Query workspace, 6. Approval workflow.

## 1. Purpose & scope

The Server Registry gave us registered database servers. The Schema Browser lets users with `query:execute` explore what is actually in those servers — schemas, tables, and columns — from a sidebar panel that will persist into the query workspace (sub-project 5).

### In scope

- `query:execute` permission added to the `Permissions` catalog.
- `SchemaTree` / `SchemaInfo` / `TableInfo` / `ColumnInfo` domain records in `Core`.
- `GetSchemaAsync` added to `ITargetEngine`; implemented in `PostgresTargetEngine` via `information_schema.columns`.
- `GET /api/schema/{serverId}` endpoint gated on `query:execute`, using the server's read credential.
- `/query` route with a permanent two-panel layout: left schema sidebar, right stub (filled by sub-project 5).
- Server selector dropdown at the top of the sidebar; schema tree below it (eager load on server select).
- TanStack Query caching with 5-minute `staleTime`.
- "Query" navbar link visible to users with `query:execute`.
- Integration tests, Vitest unit tests, and a Playwright E2E spec.

### Out of scope (deferred)

- Constraints, indexes, foreign keys (sub-project 4 extension or later).
- Views, functions, sequences, materialized views (later).
- Column-level search or filtering within the tree.
- URL persistence for selected server / expanded nodes.
- Resizable sidebar panel.
- Manual schema refresh button.
- Query editor and results pane (sub-project 5).
- Write queries and approval workflow (sub-project 6).

### Success criteria

After implementation, with Aspire running:

1. Alice (has `query:execute`) sees a "Query" link in the navbar.
2. Alice navigates to `/query` and sees the server selector dropdown and an empty tree placeholder.
3. Alice selects "Blue" — the schema tree populates with schemas; she can expand to see tables and columns with data types.
4. System schemas (`information_schema`, `pg_catalog`, `pg_toast`) are not shown.
5. Re-selecting "Blue" within 5 minutes does not trigger a new network request (served from TanStack Query cache).
6. Bob (no `query:execute`) is redirected away from `/query` and gets 403 from `GET /api/schema/{id}`.
7. `dotnet test` and `npm run test` pass. `npm run test:e2e` passes the new `query-schema.spec.ts`.

## 2. Architectural decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Schema browser lives in the `/query` route from day one | User confirmed the browser only makes sense as part of the query workspace; building it in its final home avoids a rework seam in sub-project 5. |
| 2 | `ITargetEngine.GetSchemaAsync` as the introspection method | Keeps engine-specific SQL inside the engine implementation; the endpoint stays engine-agnostic. |
| 3 | `information_schema.columns` as the data source | Portable across engines (MySQL, SQL Server also expose it); avoids pg-specific catalog tables. |
| 4 | System schemas excluded at the query layer | `information_schema`, `pg_catalog`, `pg_toast` filtered in the SQL `WHERE` clause — not on the client. |
| 5 | Eager load on server select | One API call returns the full tree; avoids multiple round-trips and lazy-state complexity. Acceptable for v1 database sizes. |
| 6 | 5-minute `staleTime` | Schema rarely changes mid-session; this eliminates redundant re-fetches while keeping data reasonably fresh. |
| 7 | Read credential only | Schema introspection never mutates; write credential is reserved for approved write-query execution (sub-project 6). |
| 8 | Right panel is a stub | Defers query editor work cleanly to sub-project 5 without blocking sub-project 4. |
| 9 | No EF migration | Schema introspection reads directly from target databases, not the metadata DB. |

## 3. Domain model

New file `SluiceBase.Core/Schema/SchemaTree.cs`:

```csharp
namespace SluiceBase.Core.Schema;

public sealed record SchemaTree(IReadOnlyList<SchemaInfo> Schemas);
public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);
public sealed record TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns);
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable);
```

These are pure data records — no domain logic, no EF configuration. Used as the return type from `ITargetEngine.GetSchemaAsync` and serialised directly as the API response.

## 4. `ITargetEngine` extension

Add to `SluiceBase.Core/Targets/ITargetEngine.cs`:

```csharp
Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct);
```

### `PostgresTargetEngine` implementation

Queries `information_schema.columns`, ordered by schema → table → ordinal position. Excludes system schemas in the `WHERE` clause.

```sql
SELECT table_schema, table_name, column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
ORDER BY table_schema, table_name, ordinal_position
```

Results are grouped into the `SchemaTree` record hierarchy in C# (no second DB round-trip).

## 5. API endpoint

New file `SluiceBase.Api/Endpoints/SchemaEndpoints.cs`, added to `EndpointMapper`.

### Endpoint table

| Endpoint | Method | Auth | Antiforgery | Notes |
|---|---|---|---|---|
| `/api/schema/{serverId}` | GET | `query:execute` | No | Returns full `SchemaTree` |

### Handler

```csharp
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
```

### Permission

`query:execute` added to `SluiceBase.Core/Permissions/Permissions.cs` and `PERMISSION_LABELS` in the frontend.

## 6. Frontend

### 6.1 Route & permission guard

New file `routes/_authed/query.tsx` → URL `/query`. Same `beforeLoad` guard pattern:

```tsx
export const Route = createFileRoute("/_authed/query")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryPage,
});
```

### 6.2 Navbar

`_authed.tsx` gains a "Query" `NavLink` (with `IconTerminal2` icon) visible only to users with `query:execute`, placed above the "Servers" link.

### 6.3 Page layout

Two-column `Grid` filling viewport height. Left column fixed at 280px; right column fills remainder.

**Left panel — schema sidebar:**

- Mantine `Select` at the top populated from `useServers()`. Stores selected `serverId` in `useState`.
- When no server is selected: dimmed placeholder text.
- When loading: `Skeleton` lines.
- On error: Mantine inline `Alert`.
- When loaded: schema tree rendered with nested Mantine `NavLink` components:
  - Schema level: expand/collapse via `useState`.
  - Table level: expand/collapse, shows table name.
  - Column level: `{name}` with `<Code>{dataType}</Code>` and a small dimmed `nullable` indicator when `isNullable` is true.

**Right panel — stub:**

```tsx
<Center h="100%">
  <Text c="dimmed">Query editor coming soon</Text>
</Center>
```

Sub-project 5 replaces this entirely.

### 6.4 TanStack Query hook

Added to `api/hooks.ts`:

```ts
export function useSchema(serverId: string | null) {
  return useQuery({
    queryKey: ["schema", serverId] as const,
    queryFn: () => apiRequest<void, SchemaTree>(`/api/schema/${serverId}`),
    enabled: serverId !== null,
    staleTime: 5 * 60 * 1000,
  });
}
```

`useServers()` is reused for the dropdown — no new hook needed.

### 6.5 Permission labels

`"query:execute"` added to `PERMISSION_LABELS` (in the permission admin page): `{ short: "Query", full: "Execute queries" }`.

## 7. Tests

### 7.1 Backend integration tests (`SchemaEndpointTests.cs`)

All tests use the existing `[Collection("Aspire")]` fixture and `KeycloakLoginHelper`:

| Test | Asserts |
|---|---|
| `GetSchema_ReturnsTree_ForBlueServer` | Blue server returns schemas; `public` is present; `information_schema` is absent; at least one table with at least one column with a non-empty `DataType` |
| `GetSchema_Returns403_ForBob` | Bob (no `query:execute`) gets 403 |
| `GetSchema_Returns401_ForAnonymous` | Unauthenticated request returns 401 |
| `GetSchema_Returns404_ForUnknownServer` | Unknown `serverId` returns 404 |

### 7.2 Frontend Vitest (`schema-hooks.test.ts`)

- `useSchema` is disabled (no fetch) when `serverId` is `null`.
- `useSchema` uses query key `["schema", serverId]`.

### 7.3 Playwright E2E (`e2e/query-schema.spec.ts`)

Signed in as Alice throughout:

1. Navigate to `/query` — expect page renders with server selector.
2. Select "Blue" from the dropdown — expect schema tree populates.
3. Expand the `public` schema — expect at least one table visible.
4. Expand a table — expect at least one column with a data type visible.
5. Navigate away and back, re-select "Blue" — tree populates (from cache; no assertion on network, just that it renders).

### 7.4 Out of test scope

- Schema cache invalidation (no invalidation path in v1).
- Large-schema performance.
- Non-Postgres engines.

## 8. Packages, risks, acceptance

### 8.1 New packages

None. `information_schema` queries use the existing `Npgsql` connection; no new NuGet packages. No new frontend packages.

### 8.2 DI registration changes

None beyond `SchemaEndpoints.Map(app)` added to `EndpointMapper.MapAllEndpoints`.

### 8.3 Risks & open questions

- **Large schemas:** Eager loading the entire schema in one call is fine for v1 database sizes. If a target has hundreds of tables, the response could be large. The `staleTime` mitigates repeat fetches. Lazy loading can be introduced later without changing the API contract.
- **`ITargetEngine` interface change:** Adding `GetSchemaAsync` is a breaking change for any other `ITargetEngine` implementations. There are none in v1 beyond `PostgresTargetEngine`, so this is low risk.
- **`information_schema` completeness:** `information_schema.columns` does not include columns from system tables or temporary tables, which is the correct behaviour.

### 8.4 Acceptance criteria

- `dotnet build SluiceBase.slnx` clean (warnings-as-errors).
- `dotnet test SluiceBase.slnx` passes (all prior tests + new schema tests).
- `npm run build` clean (TS strict + ESLint).
- `npm run test` passes (all prior tests + new schema hook tests).
- `aspire run`:
  - Alice sees "Query" in the navbar.
  - Alice navigates to `/query`, selects Blue, sees the schema tree.
  - System schemas are not shown.
  - Bob navigating to `/query` is redirected to `/`.
- `npm run test:e2e` passes `query-schema.spec.ts`.

## 9. References

- Foundations design: `docs/superpowers/specs/2026-05-03-foundations-design.md`
- Permissions design: `docs/superpowers/specs/2026-05-04-permissions-design.md`
- Server Registry design: `docs/superpowers/specs/2026-05-06-server-registry-design.md`
- `ITargetEngine`: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- `PostgresTargetEngine`: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- `IServerConnectionFactory`: `src/SluiceBase.Api/Servers/IServerConnectionFactory.cs`
