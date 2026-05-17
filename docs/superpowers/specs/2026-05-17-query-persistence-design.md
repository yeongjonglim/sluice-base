# Query Editor Session Persistence

**Date:** 2026-05-17  
**Status:** Approved

## Problem

The query editor page loses all state on page refresh: the selected database, the query text, and any expanded schema/table nodes in the sidebar all reset to their defaults. Users want their current working state restored after a refresh.

## Goal

Persist the query editor page's visible state across page refreshes within the same browser tab session. State does not need to survive closing the browser or carry across tabs.

## Scope

Four pieces of state to persist:

| State | Default |
|---|---|
| Selected database ID | `null` |
| Editor content (SQL text) | `""` |
| Expanded schema names | `[]` |
| Expanded table keys | `[]` |

No per-database memory. State is a flat snapshot of whatever is currently on the page.

## Design

### `useSessionState` hook

New file: `src/frontend/src/utils/useSessionState.ts`

```ts
useSessionState<T>(key: string, defaultValue: T): [T, Dispatch<SetStateAction<T>>]
```

- Lazy-initializes state from `sessionStorage` on mount (avoids reading on every render).
- Writes back to `sessionStorage` via `useEffect` on every state change.
- JSON parse errors fall back to `defaultValue` silently.
- Generic `T` must be JSON-serializable (no Sets, Dates, etc.).

### Session keys

All keys are namespaced under `"sluice:query:"`:

| Key | Type |
|---|---|
| `"sluice:query:db"` | `string \| null` |
| `"sluice:query:editor"` | `string` |
| `"sluice:query:expandedSchemas"` | `string[]` |
| `"sluice:query:expandedTables"` | `string[]` |

### Changes to `QueryPage`

Replace the two `useState` calls for `selectedDatabaseId` and `editorContent` with `useSessionState` using the keys above. No other changes to `QueryPage`.

### Changes to `SchemaSidebar`

Replace the two `useState<Set<string>>` calls with `useSessionState<string[]>`. Update toggle logic:

- `set.has(x)` → `arr.includes(x)`
- `set.add(x)` → `[...arr, x]`
- `set.delete(x)` → `arr.filter(v => v !== x)`

No other changes to `SchemaSidebar`.

## Out of Scope

- Per-database expanded state memory (not needed)
- Cross-tab synchronization (sessionStorage is per-tab by design)
- Persisting query results
- Panel size persistence
