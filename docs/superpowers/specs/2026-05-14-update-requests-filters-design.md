# Update Requests Filter Support

**Date:** 2026-05-14
**Status:** Approved

## Summary

Add filter support to the Update Requests list page (`/_authed/update/`) mirroring the pattern used by Query History. Filters for status, database, date range, and submitter are added. The first three are server-side (query params sent to the API); submitter is client-side text search on the fetched list.

## Backend

### `GET /api/update` query parameters

Add four optional query parameters to the `List` handler in `UpdateEndpoints.cs`:

| Param | Type | Description |
|---|---|---|
| `status` | `UpdateRequestStatus?` | Filter to a single status value |
| `databaseId` | `DatabaseId?` | Filter to a specific database |
| `from` | `DateTimeOffset?` | Include only requests submitted on or after this date |
| `to` | `DateTimeOffset?` | Include only requests submitted on or before this date |

Each parameter is applied as an additional `.Where()` clause on the EF query before `.ToListAsync()`. No changes to the response shape.

The `ListUpdates` operation in `schema.ts` is updated from `query?: never` to expose these four params. Regenerate `schema.ts` after the backend change (via `openapi-typescript` or the project's existing generation script).

## Frontend — API hook

In `src/frontend/src/api/hooks.ts`:

- Add `UpdateRequestFilters` type: `{ from?: string; to?: string; databaseId?: string; status?: string }`
- Update `useUpdateRequests(filters?: UpdateRequestFilters)` to build a query string from the filters and append it to the `/api/update` URL
- Change query key to `["update", "list", filters]` so React Query caches per filter combination

Pattern is identical to `useQueryHistory`.

## Frontend — Route

In `src/frontend/src/routes/_authed/update/index.tsx`:

### Route search params

Add `validateSearch` returning `UpdateListSearch`:

```ts
type UpdateListSearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
};
```

### Filter bar

A `<Group>` of controls above the table, matching query history layout:

| Control | Type | Binds to |
|---|---|---|
| From | `TextInput type="date"` | `search.from` (URL param) |
| To | `TextInput type="date"` | `search.to` (URL param) |
| Database | `Select` via `useCatalogServer` | `search.databaseId` (URL param) |
| Status | `Select` with 6 options (All + 5 statuses) | `search.status` (URL param) |
| Submitter | `TextInput` placeholder "Filter by name…" | `submitterSearch` (local state) |

Status options: All statuses, Pending, Approved, Rejected, Cancelled, Executed.

Changing any URL-bound filter calls `navigate` with the new value (or `undefined` to clear), identical to query history's `setFilter` helper.

### Client-side submitter filter

```ts
const displayedRequests = submitterSearch
  ? allRequests.filter((r) =>
      (r.submitterName ?? "").toLowerCase().includes(submitterSearch.toLowerCase())
    )
  : allRequests;
```

Visible to all update users (no permission gate).

### Hook call

```ts
const filters: UpdateRequestFilters = {
  from: search.from,
  to: search.to,
  databaseId: search.databaseId,
  status: search.status,
};
const requests = useUpdateRequests(filters);
```

## Out of scope

- Pagination
- Sorting controls
- Submitter filter as a URL search param / server-side
