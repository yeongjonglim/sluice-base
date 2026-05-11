# Query History Design

**Date:** 2026-05-11  
**Status:** Approved

## Overview

Add a query history view at `/query/history` where users can see their own past queries, and users with the `query:audit` permission can see all users' queries with an additional user filter. Each row has a "Copy SQL" clipboard button. Filters (date range, database, status, user) are reflected in URL search params.

---

## Architecture & Data Flow

A single new endpoint `GET /api/query/history` is added to the existing `QueryEndpoints`. It accepts optional query params: `from`, `to`, `databaseId`, `status`. Authorization logic:

- **Without `query:audit`**: results are implicitly filtered to the current user's queries.
- **With `query:audit`**: no implicit user filter; all matching entries are returned.

Results are capped at 100 entries ordered by `executedAt DESC`.

A new `query:audit` permission is added to `Permissions.All` (backend constant + `Permissions.cs`) and to the `Permission` union type in the frontend (`permission.ts`).

The frontend restructures the query route to mirror the existing `update` pattern:
- `query.tsx` → `query/index.tsx` (identical content, no logic changes)
- `query/history.tsx` added alongside it

The `/query` editor route is **not** changed. Filter state on `/query/history` lives entirely in URL search params (shareable/bookmarkable). Load-into-editor is replaced by a per-row "Copy SQL" clipboard button.

---

## Components

### Backend

**`Permissions.cs`**
- Add `public const string QueryAudit = "query:audit";`
- Add `QueryAudit` to `Permissions.All`

**`QueryEndpoints.cs` — `GetHistory` handler**
- Route: `GET /api/query/history`, requires `query:execute`
- Query params: `DateTimeOffset? from`, `DateTimeOffset? to`, `string? databaseId`, `string? status`
- Joins `QueryLogs` with `Databases` (for `databaseDisplayName`) and `Users` (for `userName`)
- Without `query:audit`: adds `WHERE userId = currentUser.Id`
- With `query:audit`: no implicit user filter
- Returns `QueryHistoryResponse` with a list of `QueryHistoryItem`

**`QueryHistoryItem` fields:**
`id`, `databaseId`, `databaseDisplayName`, `queryText`, `status`, `executedAt`, `durationMs`, `rowCount`, `error`, `userId`, `userName`

**`QueryHistoryEndpointTests.cs`** (new integration test file) covering:
- Own-user results returned correctly without `query:audit`
- All-users results returned with `query:audit`
- Each filter param (`status`, `databaseId`, `from`/`to`) narrows results correctly
- Invalid date range (`from` after `to`) returns `400`

### Frontend

**`permission.ts`**
- Add `"query:audit"` to the `Permission` union type

**`hooks.ts`**
- New `useQueryHistory(filters: QueryHistoryFilters)` hook via TanStack Query
- `GET /api/query/history` with filters serialised as query params
- Cache key includes all filter values so any filter change triggers a fresh fetch

**`query/index.tsx`** (renamed from `query.tsx`)
- No logic changes; file move only

**`query/history.tsx`**
- `beforeLoad`: redirects to `/` if user lacks `query:execute` (same pattern as existing query page)
- Reads and writes filters via TanStack Router `useSearch` / `navigate`
- Filter bar:
  - Date range: two date inputs (`from`, `to`)
  - Database: dropdown populated from `useServers`
  - Status: select with options All / Success / Error / Timeout
  - User filter: free-text input applied client-side against `userName`, visible only when user has `query:audit` (no backend round-trip needed since results are capped at 100)
- Results table columns: Status badge, Database, SQL preview (truncated, monospace), Executed At, Duration (ms), Rows, Copy button
- Copy button: uses browser Clipboard API, shows a brief Mantine notification on success
- Loading / error / empty states consistent with existing pages

**`_authed.tsx`**
- `canQuery` check already present
- Single "Query" `NavLink` becomes two nested entries under a parent `NavLink`:
  - "Query" → `/query`, active when `pathname === "/query"`
  - "History" → `/query/history`, active when `pathname === "/query/history"`
- Both visible when `canQuery` is true

**Frontend unit tests** (`src/api/__tests__/query-history-hooks.test.ts`):
- `useQueryHistory` hook: mock API, verify cache key includes all filter values, verify params are serialised correctly

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| Unauthenticated request | `401` → existing global redirect to `/login` |
| `from` after `to` | `400 Bad Request` |
| Clipboard API unavailable | Silent no-op (no notification shown) |
| Fetch failure on history page | Inline error state in table area |

---

## Out of Scope

- Pagination beyond the 100-entry cap
- Deleting or hiding history entries
- Re-running queries directly from history (user copies SQL manually)
