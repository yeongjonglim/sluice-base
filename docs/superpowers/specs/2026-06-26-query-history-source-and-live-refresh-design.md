# Query History: Source Indicator + Live Refresh

**Date:** 2026-06-26

## Goal

Improve the Query History page so it:

1. Indicates whether each query was executed through the **UI** or through **MCP**.
2. Lets users **filter** history by source.
3. **Live-refreshes** while the user is viewing the page (polling).

## Background

The backend already records the source on every query:

- `SluiceBase.Core.Queries.QuerySource` is an enum `{ Ui, Mcp }`.
- `QueryLog.Source` is persisted, populated at execution time (UI path passes `QuerySource.Ui`; the MCP path passes `QuerySource.Mcp`).

The gap is purely in exposure and presentation: the `Source` is **not** included in the history API response, the frontend doesn't display or filter on it, and the page does not poll for updates. No database migration is needed — the column already exists.

## Backend changes

File: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`

- Add `QuerySource Source` to the `QueryHistoryItem` record and to the LINQ projection (`q.Source`). It serializes as `"Ui"` / `"Mcp"` via the existing `JsonStringEnumConverter` registered in `Program.cs`.
- Add a `string? source` parameter to `GetHistory`, parsed with
  `Enum.TryParse<QuerySource>(source, ignoreCase: true, out var parsedSource)` — mirroring the existing `status` handling.
- Add a filter clause: `.Where(q => filterSource == null || q.Source == filterSource)`.

### Generated artifacts

Both are committed and CI-gated (`pr-checks.yml` runs `git diff --exit-code` on each):

- Regenerate `src/SluiceBase.Api/openapi.json` (produced by the API build).
- Regenerate `src/frontend/src/api/schema.ts` via `npm run gen:api`.

## Frontend changes

### Data layer — `src/frontend/src/api/hooks.ts`

- Add `source: string;` to the `QueryHistoryItem` interface.
- Add `source?: string;` to the `QueryHistoryFilters` interface.
- Wire `source` into the query string in `useQueryHistory` (same pattern as `status`).
- Add `refetchInterval: 10_000` to the `useQuery` options. Polling is **focus-only**: TanStack Query's default `refetchIntervalInBackground: false` pauses polling when the browser tab is unfocused and auto-resumes on focus.

### Presentation — `src/frontend/src/routes/_authed/query/history.tsx`

**Source indicator (icon beside status):**
The Status cell already renders a `Group` containing the status `Badge` and (conditionally) an `IconShieldLock` for sensitive columns. Add a source icon to that same group, each with a `Tooltip`:

- `Ui` → `IconDeviceDesktop`, tooltip "From UI".
- `Mcp` → `IconPlugConnected`, tooltip "From MCP".

Use a small size (`14`) consistent with the existing shield icon, and a muted/dimmed color so it doesn't compete with the status badge.

**Source filter (dropdown):**
Add a `Select` to the filter `Group`, following the existing Status filter pattern exactly:

- Options: `{ value: "", label: "All sources" }`, `{ value: "Ui", label: "UI" }`, `{ value: "Mcp", label: "MCP" }`.
- Add `source?: string` to the `HistorySearch` type and to `validateSearch`.
- Bind value to `search.source ?? ""`, `onChange` → `setFilter("source", v ?? undefined)`.
- Include `source: search.source` in the `filters` object passed to `useQueryHistory`.

## Out of scope

- No changes to query **execution** — source is already recorded correctly at execution time.
- No database migration — the `Source` column already exists.
- No changes to MCP-side query recording.

## Testing

- **Backend:** extend `tests/IntegrationTests/QueryHistoryEndpointTests.cs` to assert `Source` is returned and that the `source` filter narrows results (e.g. seed one `Ui` and one `Mcp` log, filter `source=Mcp`, expect one item).
- **Frontend:** extend `src/frontend/src/api/__tests__/query-history-hooks.test.ts` to assert the `source` param is appended to the request URL when set.
