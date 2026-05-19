# Column-Based Query Authorization Design

**Date:** 2026-05-19
**Status:** Approved
**Issue:** #16

## Overview

Currently any user with `query:execute` on a database can query any column in any table. This feature introduces column-level sensitivity: admins can mark specific columns as globally sensitive, blocking all users by default. Trusted users can be granted a persistent bypass. All access attempts against sensitive columns are logged.

A future follow-on (Step 2) will add an approval workflow where non-bypass users can submit a sensitive column query request for an approver to review. This design covers Step 1 only.

## Mental Model

Sensitive columns are a database-level concept, not a per-user concept. A column is either sensitive or it is not. The bypass grant is the exception — it gives a specific user standing permission to query a sensitive column without per-query approval. Users without a bypass are blocked at query time with a structured 403 response that lists the offending columns.

## Data Model

### `sensitive_column`

Globally marks a column as sensitive on a specific database.

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | |
| `database_id` | UUID FK → `server_database` | no cascade |
| `schema_name` | varchar(128) | |
| `table_name` | varchar(128) | |
| `column_name` | varchar(128) | |
| `marked_at` | timestamptz | |
| `marked_by_id` | UUID FK → `user` | SET NULL on delete |

**Unique constraint:** `(database_id, schema_name, table_name, column_name)`

### `user_column_bypass`

Grants a trusted user persistent access to a specific sensitive column without per-query approval.

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | |
| `user_id` | UUID FK → `user` | CASCADE delete |
| `sensitive_column_id` | UUID FK → `sensitive_column` | CASCADE delete |
| `granted_at` | timestamptz | |
| `granted_by_id` | UUID FK → `user` | SET NULL on delete |

**Unique constraint:** `(user_id, sensitive_column_id)`

### Migration

No seed data. Zero rows in both tables means no columns are currently sensitive — consistent with existing open-access behaviour. No regressions for existing users.

## SQL Parsing & Enforcement

**Library:** `SqlParser-cs` NuGet package — open-source .NET PostgreSQL-dialect SQL parser producing a full AST.

**Enforcement point:** `POST /api/query`, after the existing `query:execute` database role check, before query execution.

**Fast path:** If `sensitive_column` has zero rows for the target database, skip all parsing entirely.

**Algorithm:**

1. Fetch all `sensitive_column` rows for the database. Fetch the current user's `user_column_bypass` rows for those columns.
2. Parse the SQL into an AST via SqlParser-cs.
3. Walk all clauses — SELECT, WHERE, JOIN ON, GROUP BY, ORDER BY, HAVING, CTEs, subqueries — collecting every column reference. Full clause coverage is the industry standard (BigQuery, Snowflake, Databricks); a restricted column used in a WHERE filter leaks information just as surely as one in a SELECT list.
4. Build an alias map from FROM/JOIN: `u` → `(public, users)`. Resolve qualified references (`u.email` → `users.email`) via this map.
5. Unqualified column references (no table qualifier) are checked conservatively against all tables in the current FROM scope. If the column name matches a sensitive column on any in-scope table, it is treated as a hit.
6. `SELECT *` on a table that has sensitive columns: fetch the live schema for that table (same code path as the schema browser), expand `*` to the full column list, then check each column.
7. Collect all sensitive column hits that have no corresponding bypass. If any: log the attempt and return 403. If none: proceed to execution.

**Query logging:** Blocked queries are written to `query_log` with a new `QueryLogStatus.Blocked` status. The `Error` field records the restricted columns (e.g. `"Sensitive columns: public.users.email, public.users.ssn"`). This gives auditors a full record of access attempts and provides useful context for Step 2 approval decisions.

**Error response (HTTP 403, Problem Details):**

```json
{
  "status": 403,
  "title": "Sensitive columns",
  "type": "sensitive_columns",
  "extensions": {
    "columns": [
      { "schema": "public", "table": "users", "column": "email" }
    ]
  }
}
```

## Backend Endpoints

All endpoints in a new `SensitiveColumnEndpoints.cs`, gated by `permission:manage`.

### Sensitive column management

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/admin/database/{databaseId}/sensitive-column` | List sensitive columns with bypass grants embedded |
| `POST` | `/api/admin/database/{databaseId}/sensitive-column` | Mark a column as sensitive |
| `DELETE` | `/api/admin/database/{databaseId}/sensitive-column/{id}` | Unmark a column (cascades to bypass grants) |

**POST body:** `{ schemaName, tableName, columnName }`

**GET response:**
```json
{
  "columns": [
    {
      "id": "...",
      "schemaName": "public",
      "tableName": "users",
      "columnName": "email",
      "markedAt": "...",
      "markedById": "...",
      "bypasses": [
        { "id": "...", "userId": "...", "userEmail": "...", "userName": "...", "grantedAt": "...", "grantedById": "..." }
      ]
    }
  ]
}
```

### Bypass grant management

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass` | Grant bypass to a user |
| `DELETE` | `/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/{userId}` | Revoke bypass |

**POST body:** `{ userId }`

No user-first variant. Sensitive columns are a database-level concept; the natural entry point is always the database.

## Schema Endpoint Changes

`ColumnInfo` gains a new field: `IsRestricted: bool`.

The schema endpoint (`GET /api/schema/{databaseId}`) already has user context and metadata DB access. After fetching the live schema from the target database, it queries `sensitive_column` and `user_column_bypass` for the current user and annotates each column: `IsRestricted = true` when the column is sensitive and the user has no bypass.

## Frontend

### Query workspace — schema sidebar

Restricted columns (`isRestricted: true`) render with a lock icon and dimmed text. Non-restricted columns are unchanged.

**Generate query button** (`handleTableClick`): filters restricted columns out of the generated SELECT list. If all columns on a table are restricted, the button is disabled (no snippet can be generated).

### Query workspace — 403 error rendering

When `executeQuery.isError` is true:
- If `error instanceof ApiError && error.status === 403 && error.body?.type === "sensitive_columns"`: render a **"Query blocked — restricted columns"** alert listing each `schema.table.column`.
- All other cases: keep the existing generic "Request failed" message.

### Admin: Sensitive Columns tab

A new **Sensitive Columns** tab on the existing `/access` page.

- Left panel: server → database tree (shared with the existing "By Database" tab).
- Right panel on database selection: lists sensitive columns for that database. Each column shows its bypass grants. Actions: **Add bypass** (user picker), **Revoke** per bypass, **Remove** to unmark the column entirely.
- **Mark column as sensitive** button: opens a modal with a live schema tree (schema → table → columns as checkboxes). Confirming fires one POST per selected column. The schema data is fetched from the existing schema endpoint.

## Out of Scope

- Step 2: sensitive query request and approval workflow (deferred to a follow-on issue).
- Row-level restrictions.
- Sensitivity labels or classification tiers (columns are sensitive or not, no gradations).
- Server-level sensitive column configuration.
