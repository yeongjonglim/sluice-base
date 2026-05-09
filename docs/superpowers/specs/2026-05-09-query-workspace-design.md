# Query Workspace Design

**Date:** 2026-05-09  
**Status:** Approved  
**Scope:** `/query` route — editor, execution, and logging

---

## Overview

Extend the existing `/query` route (which already has a working schema sidebar) with a functional SQL query workspace. Users with `query:execute` permission can explore the schema, build SELECT queries with syntax highlighting, execute them against a registered server, and see results inline. All queries are logged to a database table for future auditing and history surfaces.

---

## Constraints

- **Read-only queries only** — no INSERT/UPDATE/DELETE for this iteration. Write operations go through the separate approval workflow (`update:submit/approve/execute`).
- **No server-side row cap** — users control result size via LIMIT. Generated snippets always include LIMIT as a convention.
- **Single editor pane** — no tabs. Users can open multiple browser tabs if they want parallel workspaces.
- **No editor state persistence** — refreshing clears the editor. localStorage or server-side persistence is a future enhancement.

---

## Layout

The right panel (currently a placeholder) splits vertically into two scrollable zones within the existing sidebar+main layout:

```
┌─────────────────────────────────────────────────────────┐
│  [Server dropdown]                                       │  ← existing sidebar header
├──────────────────┬──────────────────────────────────────┤
│                  │  [CodeMirror editor]                  │
│  Schema sidebar  │  ─────────────────────────────────── │
│  (existing)      │  [Run ▶]  Ctrl+Enter / Cmd+Enter      │
│                  │  ─────────────────────────────────── │
│                  │  [Results table / error / empty state]│
└──────────────────┴──────────────────────────────────────┘
```

The entire right panel scrolls as one continuous column so that large result sets can fill the full viewport. The editor has a fixed height (`300px`); the results area grows with content.

---

## Query Editor

**Library:** CodeMirror 6 via `@uiw/react-codemirror` + `@codemirror/lang-sql`  
**Theme:** Follows Mantine's color scheme (light/dark) using a matching theme from `@uiw/codemirror-themes`

### Behaviours

- SQL syntax highlighting using the standard SQL dialect
- **Append on table click** — clicking any table in the schema sidebar appends to the end of the current editor content (with a leading newline if non-empty):
  ```sql
  SELECT col1, col2, col3
  FROM schema_name.table_name
  LIMIT 100;
  ```
  This never replaces existing content — the editor is a free workspace.
- **Run shortcut** — `Ctrl+Enter` (Windows/Linux) and `Cmd+Enter` (Mac) via a CodeMirror keymap extension, in addition to the Run button

---

## Backend Execution Endpoint

### Route

`POST /api/query`  
Requires: `query:execute` permission

### Request

```json
{
  "serverId": "uuid",
  "sql": "SELECT id, name FROM public.users LIMIT 10"
}
```

### Execution Flow

1. Look up the server record; return 404 if not found or disabled. Return 400 with a clear message if the server has no read-only credentials configured.
2. Decrypt the server's **read-only credentials** (`encrypted_read_username` / `encrypted_read_password`)
3. Open a Npgsql connection using the read-only DB user
4. Begin a read-only transaction via Npgsql's `BeginTransactionAsync` API (not string concatenation — transaction wrapper is never part of the user-controlled SQL string)
5. Execute the user's SQL with a configurable timeout (default: 30s, configurable via `appsettings.json` under `Query:TimeoutSeconds`)
6. Read all result rows into a column-oriented structure
7. Commit and close the connection
8. Write a row to `query_log` (always — success and failure), including `duration_ms` and `row_count`
9. Return the response

### Security

- **Read-only DB user** is the primary defence — the PostgreSQL user has only `SELECT` privilege and physically cannot write regardless of SQL content
- **Read-only transaction** via Npgsql API provides a second layer; the transaction wrapper is never part of the user-controlled string, preventing semicolon injection from breaking out of it
- **Npgsql extended query protocol** rejects multiple statements in a single command at the protocol level
- **Timeout** prevents resource exhaustion from long-running queries

PostgreSQL's MVCC means readers and writers never block each other by default — no `NOLOCK` equivalent is needed. The read-only transaction relies on standard `READ COMMITTED` isolation, which gives consistent (non-dirty) reads without holding locks that interfere with writers.

### Success Response

```json
{
  "columns": ["id", "name", "email"],
  "rows": [[1, "Alice", "alice@example.com"], [2, "Bob", "bob@example.com"]],
  "rowCount": 2,
  "durationMs": 142
}
```

### Error Response

Errors (SQL error, timeout, validation) return `200 OK` with an `error` field. HTTP error codes are reserved for infrastructure failures (server not found → 404, permission denied → 403).

```json
{
  "error": "ERROR: column \"naem\" does not exist",
  "durationMs": 38
}
```

---

## Query Log Table

New migration adds `query_log`:

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | Primary key |
| `user_id` | `uuid` | FK → `user`, SET NULL on delete |
| `server_id` | `uuid` | FK → `server`, SET NULL on delete |
| `query_text` | `text` | Full SQL as submitted |
| `status` | `varchar(16)` | `success`, `error`, `timeout` |
| `executed_at` | `timestamptz` | When execution started |
| `duration_ms` | `int` | Nullable — null if connection failed before execution |
| `row_count` | `int` | Nullable — null on error/timeout |
| `error` | `text` | Nullable — null on success |

Both `user_id` and `server_id` use SET NULL on delete so log history survives user or server removal.

The table is not surfaced in the UI in this iteration but is written on every query execution.

---

## Frontend Results Display

A `useExecuteQuery` hook wraps `POST /api/query` using TanStack React Query's `useMutation`, consistent with the pattern in `hooks.ts`.

### Result States

| State | Display |
|---|---|
| **Empty** | Placeholder text: "Run a query to see results." |
| **Loading** | Mantine skeleton/spinner; Run button disabled to prevent double-submission |
| **Success** | Horizontally scrollable Mantine `Table` with column headers and rows. Status line above: `2 rows · 142 ms` |
| **Error** | Mantine `Alert` (danger variant) with the error message. Status line: `Error · 38 ms` |

Rows are rendered as strings without type-specific formatting in this iteration.

---

## Out of Scope (Future)

- Editor state persistence (localStorage or server-side)
- Query history UI surfacing `query_log`
- Write queries (`update:submit` workflow)
- Resizable editor/results splitter
- Multiple editor tabs
- Per-server row limit configuration
- SQL autocompletion from schema metadata
