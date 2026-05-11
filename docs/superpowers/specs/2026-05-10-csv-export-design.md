---
title: CSV Export for Query Results
date: 2026-05-10
status: approved
---

# CSV Export for Query Results

## Summary

Add a client-side CSV download button to the query results view in `src/frontend/src/routes/_authed/query.tsx`. The export uses data already in the browser — no additional API calls.

## Scope

- **In scope:** Export the exact rows returned by the last executed query.
- **Out of scope:** Re-fetching without a limit, server-side export endpoints, pagination of large results.

## Component Changes

`QueryResults` is the only component modified. No new props are needed.

When `result` is non-null, has no `error`, and has at least one column, the results header row (currently a plain `Text` showing row count and duration) is replaced with a `Group justify="space-between"` containing:

- Left: the existing row count / duration `Text`.
- Right: an "Export CSV" `Button` (small, subtle variant, with a download icon).

The button is absent in all other states: pending, error, no result yet.

## CSV Generation

A pure helper function at the top of `query.tsx`:

```ts
function exportToCsv(columns: string[], rows: string[][], filename: string): void
```

**Algorithm:**
1. Escape each cell value per RFC 4180: if the value contains a comma, double-quote, or newline, wrap it in double-quotes and escape internal double-quotes by doubling them. Null/undefined cells render as an empty string.
2. Build the CSV string: header row from `columns`, then one row per entry in `rows`.
3. Create a `Blob` with `type: "text/csv"`, generate an object URL, programmatically click a hidden `<a>` element with the `download` attribute set, then revoke the URL.

**Filename format:** `query-results-<Date.now()>.csv` — timestamp prevents collisions on repeated exports.

## Edge Cases

| State | Behavior |
|---|---|
| Zero rows, valid columns | Button appears; CSV contains header row only. |
| `result.error` set | Button absent (error branch of `QueryResults`). |
| Null/undefined cell | Rendered as empty string in CSV. |
| Cell contains comma/quote/newline | Wrapped in double-quotes per RFC 4180. |

## Files Affected

- `src/frontend/src/routes/_authed/query.tsx` — add `exportToCsv` helper, update `QueryResults` render.
