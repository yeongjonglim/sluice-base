# Query History — CodeMirror SQL Display

**Date:** 2026-05-11  
**Status:** Approved  
**File:** `src/frontend/src/routes/_authed/query/history.tsx`

## Goal

Replace the truncated `<Code>` SQL preview in the query history table with a full, read-only CodeMirror SQL editor. Move the copy-to-clipboard button to sit beside the editor rather than in a separate trailing column.

## Architecture

Single-file change to `history.tsx`. No new files, no API changes, no hook changes.

## Component Changes

### Table header

Merge the existing SQL `<Table.Th>` and the empty trailing `<Table.Th>` into one:

```tsx
<Table.Th>SQL</Table.Th>
```

### Table style

Remove `whiteSpace: "nowrap"` from the `<Table>` style so rows can auto-expand vertically to fit multi-line SQL.

### `HistoryRow`

1. Remove `sqlPreview` truncation — use `item.queryText` directly.
2. Remove the separate copy `<Table.Td>`.
3. Replace the SQL `<Table.Td>` with a merged cell containing:
   - A `<CodeMirror>` component (`flex: 1`) with:
     - `value={item.queryText}`
     - `readOnly` and `editable={false}`
     - `height="auto"` (rows expand to fit full SQL, no scroll)
     - `extensions={[sql()]}`
     - Theme-aware: `githubDark` / `githubLight` via `useComputedColorScheme`
     - `basicSetup={{ lineNumbers: false, foldGutter: false }}`
   - The existing `<ActionIcon>` copy button pinned to the right, inside a `<Group align="flex-start" wrap="nowrap">`

### New imports

```ts
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { useComputedColorScheme } from "@mantine/core";
```

All packages are already installed (`@uiw/react-codemirror`, `@codemirror/lang-sql`, `@uiw/codemirror-themes-all`).

## Data Flow

No data flow changes. `item.queryText` (already present on `QueryHistoryItem`) is passed directly to the CodeMirror `value` prop.

## Error Handling

No new error cases. CodeMirror renders an empty editor gracefully if `queryText` is an empty string.

## Testing

Verify manually:
- Single-line SQL renders on one line, row is compact
- Multi-line SQL expands the row to fit all lines without a scrollbar
- Copy button copies the full SQL text (not truncated)
- Theme switches correctly with light/dark mode
- Audit users see the User column unaffected
