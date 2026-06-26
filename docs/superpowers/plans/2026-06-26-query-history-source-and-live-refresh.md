# Query History: Source Indicator + Live Refresh — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface each query's source (UI vs MCP) and its error reason in Query History, allow filtering by source, and live-refresh the page while the user views it.

**Architecture:** The `QuerySource` enum and `QueryLog.Source` column already exist and are populated at execution time. Work is exposure + presentation only: add `Source` to the history API DTO + a `source` filter param, regenerate the CI-gated `openapi.json`/`schema.ts`, then update the frontend hook (types, query string, polling) and the history table (source icon, error tooltip, source filter).

**Tech Stack:** .NET 10 Minimal API + EF Core; React + TypeScript + Mantine + TanStack Query; Vitest; xUnit Aspire integration tests.

## Global Constraints

- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Enums serialize as strings via the `JsonStringEnumConverter` registered in `Program.cs` — `QuerySource` becomes `"Ui"` / `"Mcp"` on the wire.
- `src/SluiceBase.Api/openapi.json` and `src/frontend/src/api/schema.ts` are committed and CI-gated (`git diff --exit-code`). Any API contract change MUST regenerate both.
- Never manually edit EF migrations (none needed here — no schema change).
- Commit messages: single subject line, no body.
- Branch: `feat/query-history-source-live-refresh` (already checked out).
- Integration tests require a healthy Aspire stack and may not run in an automated session — verify with `dotnet build`; CI runs the suite.

---

### Task 1: Expose `Source` and add `source` filter in the history API

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` (DTO `QueryHistoryItem`, projection, `GetHistory` signature + filter)
- Test: `tests/IntegrationTests/QueryHistoryEndpointTests.cs`
- Regenerate: `src/SluiceBase.Api/openapi.json`, `src/frontend/src/api/schema.ts`

**Interfaces:**
- Produces: `QueryHistoryItem` JSON now includes `"source": "Ui" | "Mcp"`. The `GET /api/query/history` endpoint accepts a `source` query param (`Ui`/`Mcp`, case-insensitive); unknown values are ignored (no filter applied), matching `status` behavior.

- [ ] **Step 1: Add `Source` and `Error` to the test DTO and write failing tests**

In `tests/IntegrationTests/QueryHistoryEndpointTests.cs`, extend the private `HistoryItem` record (around line 401) to include the fields the new assertions read:

```csharp
private sealed record HistoryItem(
    string QueryText, string Status, string? DatabaseId, string? DatabaseDisplayName,
    string? UserId, string? UserName, string ExecutedAt, string[] SensitiveColumns,
    string Source, string? Error);
```

Add two tests after `GetHistory_FiltersByStatus` (UI queries run through `/api/query`, which records `QuerySource.Ui`):

```csharp
[Fact]
public async Task GetHistory_UiQuery_HasUiSource()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
    using var _ = session;

    var sql = $"SELECT 1 -- source-ui-{Guid.NewGuid():N}";
    using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new { databaseId, sql });
    (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();

    var resp = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
    var item = Assert.Single(resp!.Items, i => i.QueryText == sql);
    Assert.Equal("Ui", item.Source);
}

[Fact]
public async Task GetHistory_FiltersBySource()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
    using var _ = session;

    var sql = $"SELECT 1 -- source-filter-{Guid.NewGuid():N}";
    using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new { databaseId, sql });
    (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();

    // source=Ui includes the UI-run query
    var ui = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history?source=Ui", ct);
    Assert.Contains(ui!.Items, i => i.QueryText == sql);
    Assert.All(ui.Items, i => Assert.Equal("Ui", i.Source));

    // source=Mcp excludes it
    var mcp = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history?source=Mcp", ct);
    Assert.DoesNotContain(mcp!.Items, i => i.QueryText == sql);
}
```

- [ ] **Step 2: Build the test project to confirm it fails to compile (DTO mismatch / missing param)**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj --configuration Debug`
Expected: FAIL — `QueryHistoryItem` has no `Source` member yet (the production `QueryHistoryItem` record is missing the field the JSON shape now expects), confirming the contract is not in place.

- [ ] **Step 3: Add `Source` to the production DTO and projection**

In `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`, add `QuerySource Source` to the `QueryHistoryItem` record (after `SensitiveColumns`):

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
    string? UserName,
    string[] SensitiveColumns,
    QuerySource Source);
```

Add `q.Source` as the final argument in the `.Select(q => new QueryHistoryItem(...))` projection (after `q.SensitiveColumns`):

```csharp
            q.SensitiveColumns,
            q.Source
        ))
```

- [ ] **Step 4: Add the `source` filter to `GetHistory`**

Add the parameter to the `GetHistory` signature (next to `string? status`):

```csharp
        string? status,
        string? source,
```

Parse it alongside `filterStatus` (after the `filterStatus` block, ~line 81):

```csharp
        QuerySource? filterSource = source is not null
            && Enum.TryParse<QuerySource>(source, ignoreCase: true, out var parsedSource)
            ? parsedSource
            : null;
```

Add the where-clause to the `query` chain (after the `filterStatus` clause, ~line 95):

```csharp
            .Where(q => filterSource == null || q.Source == filterSource);
```

(Insert it before the `if (sensitiveFilterAny)` block; keep the existing trailing `;` placement consistent.)

- [ ] **Step 5: Regenerate the OpenAPI document and the frontend schema**

Run:
```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Debug
cd src/frontend && npm run gen:api && cd ../..
```
Expected: `src/SluiceBase.Api/openapi.json` now lists `source` (and the `QuerySource` schema); `src/frontend/src/api/schema.ts` updates to match. Confirm with `git status` showing both files modified.

- [ ] **Step 6: Build everything to confirm compilation**

Run: `dotnet build SluiceBase.sln --configuration Debug`
Expected: PASS (the integration tests now compile against the updated DTO). Full test execution happens in CI per the Global Constraints.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/QueryEndpoints.cs src/SluiceBase.Api/openapi.json src/frontend/src/api/schema.ts tests/IntegrationTests/QueryHistoryEndpointTests.cs
git commit -m "Expose query source and add source filter to history API"
```

---

### Task 2: Frontend hook — source field, source filter param, live polling

**Files:**
- Modify: `src/frontend/src/api/hooks.ts:920-963`
- Test: `src/frontend/src/api/__tests__/query-history-hooks.test.ts`

**Interfaces:**
- Consumes: `QueryHistoryItem` JSON with `source` from Task 1.
- Produces: `QueryHistoryItem.source: string`; `QueryHistoryFilters.source?: string`; `useQueryHistory` appends `source` to the URL and polls every 10s while the tab is focused.

- [ ] **Step 1: Write the failing test for the `source` URL param**

Add to `src/frontend/src/api/__tests__/query-history-hooks.test.ts` inside the `describe("useQueryHistory", …)` block:

```typescript
it("appends source filter to the URL", async () => {
  vi.mocked(apiRequest).mockResolvedValue({ items: [] });
  const { result } = renderHook(() => useQueryHistory({ source: "Mcp" }), { wrapper });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  expect(apiRequest).toHaveBeenCalledWith("/api/query/history?source=Mcp");
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npx vitest run src/api/__tests__/query-history-hooks.test.ts`
Expected: FAIL — `source` is not a known property of `QueryHistoryFilters` (type error) / the param is not appended.

- [ ] **Step 3: Add `source` to the interfaces and wire the param + polling**

In `src/frontend/src/api/hooks.ts`, add to the `QueryHistoryItem` interface (after `sensitiveColumns`):

```typescript
  sensitiveColumns: Array<string>;
  source: string;
}
```

Add to the `QueryHistoryFilters` interface (after `status`):

```typescript
  status?: string;
  source?: string;
```

In `useQueryHistory`, append the param (after the `status` block) and add `refetchInterval`:

```typescript
      if (filters.status) params.set("status", filters.status);
      if (filters.source) params.set("source", filters.source);
```

Add `refetchInterval` to the `useQuery` options object (after `queryFn`):

```typescript
    refetchInterval: 10_000,
```

(TanStack Query's default `refetchIntervalInBackground: false` keeps polling focus-only — no extra option needed.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/api/__tests__/query-history-hooks.test.ts`
Expected: PASS (all existing tests plus the new one).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/hooks.ts src/frontend/src/api/__tests__/query-history-hooks.test.ts
git commit -m "Add source filter and live polling to query history hook"
```

---

### Task 3: History table — source icon, error tooltip, source filter dropdown

**Files:**
- Modify: `src/frontend/src/routes/_authed/query/history.tsx`

**Interfaces:**
- Consumes: `QueryHistoryItem.source` and `QueryHistoryFilters.source` from Task 2.
- Produces: no downstream consumers (leaf presentation change).

- [ ] **Step 1: Add `source` to the route search type and validation**

In `src/frontend/src/routes/_authed/query/history.tsx`, add to the `HistorySearch` type (after `status`):

```typescript
type HistorySearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
  source?: string;
  sensitiveColumn?: Array<string>;
};
```

Add to `validateSearch` (after the `status` line):

```typescript
    status: typeof search.status === "string" ? search.status : undefined,
    source: typeof search.source === "string" ? search.source : undefined,
```

Add a `SOURCE_OPTIONS` constant next to `STATUS_OPTIONS`:

```typescript
const SOURCE_OPTIONS = [
  { value: "", label: "All sources" },
  { value: "Ui", label: "UI" },
  { value: "Mcp", label: "MCP" },
];
```

- [ ] **Step 2: Pass `source` through the filters and add the filter dropdown**

In `QueryHistoryPage`, add `source` to the `filters` object (after `status`):

```typescript
    status: search.status,
    source: search.source,
```

Add a `Select` to the filter `Group`, immediately after the Status `Select` (before the `MultiSelect`):

```tsx
        <Select
          label="Source"
          size="sm"
          data={SOURCE_OPTIONS}
          value={search.source ?? ""}
          onChange={(v) => setFilter("source", v ?? undefined)}
          style={{ width: 140 }}
        />
```

- [ ] **Step 3: Add the source icon and error tooltip to the status cell**

Update the imports — add `IconDeviceDesktop` and `IconPlugConnected` to the `@tabler/icons-react` import:

```typescript
import { IconCopy, IconDeviceDesktop, IconPlugConnected, IconShieldLock } from "@tabler/icons-react";
```

In `HistoryRow`, replace the status `<Group>` block so the badge gets an error tooltip and the group gets a source icon:

```tsx
      <Table.Td>
        <Group gap={4} justify="center">
          {item.status === "Error" && item.error ? (
            <Tooltip label={item.error} multiline>
              <Badge color={STATUS_COLOR[item.status] ?? "gray"} size="sm">
                {item.status}
              </Badge>
            </Tooltip>
          ) : (
            <Badge color={STATUS_COLOR[item.status] ?? "gray"} size="sm">
              {item.status}
            </Badge>
          )}
          {item.source === "Mcp" ? (
            <Tooltip label="From MCP">
              <IconPlugConnected size={14} color="var(--mantine-color-dimmed)" />
            </Tooltip>
          ) : (
            <Tooltip label="From UI">
              <IconDeviceDesktop size={14} color="var(--mantine-color-dimmed)" />
            </Tooltip>
          )}
          {item.sensitiveColumns.length > 0 && (
            <Tooltip label={item.sensitiveColumns.join(", ")} multiline>
              <IconShieldLock size={14} color="var(--mantine-color-yellow-6)" />
            </Tooltip>
          )}
        </Group>
      </Table.Td>
```

- [ ] **Step 4: Typecheck, lint, and build**

Run: `cd src/frontend && npx tsc --noEmit && npm run lint && npm run build`
Expected: PASS — no type errors, no `array-type` violations, build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/routes/_authed/query/history.tsx
git commit -m "Show query source icon, error tooltip, and source filter in history"
```

---

## Self-Review

- **Spec coverage:** Source in DTO + `source` filter param (Task 1); source field/filter/polling in hook (Task 2); source icon, error tooltip, source filter dropdown (Task 3); openapi/schema regen (Task 1 Step 5); backend + frontend tests (Tasks 1 & 2). All spec sections mapped.
- **Placeholder scan:** No TBDs; every code step shows concrete code and commands.
- **Type consistency:** `QuerySource`→`"Ui"`/`"Mcp"` used consistently across DTO, hook (`source: string`), filter values (`"Ui"`/`"Mcp"`), and UI checks (`item.source === "Mcp"`). `source` param name consistent end to end.
