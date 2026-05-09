# Update Approval Workflow Design

**Date:** 2026-05-09
**Status:** Approved
**Scope:** Full lifecycle for submitting, approving, and executing mutating SQL queries

---

## Overview

Users with `update:submit` permission can submit mutating SQL queries (INSERT/UPDATE/DELETE) for review. Users with `update:approve` can approve or reject them. Users with `update:execute` can execute approved requests against the server's write credentials. All requests and their outcomes are stored and visible to anyone holding any `update:*` permission.

---

## State Machine

Managed via the **Stateless** NuGet package (to be added to `SluiceBase.Api`).

```
              ┌──────────┐
              │ pending  │
              └──────────┘
           /      |       \
      approve   cancel   reject
         /        |         \
  ┌──────────┐ ┌───────────┐ ┌──────────┐
  │ approved │ │ cancelled │ │ rejected │
  └──────────┘ └───────────┘ └──────────┘
       │
    execute
       │
  ┌──────────┐
  │ executed │
  └──────────┘
```

Valid transitions:
- `pending` → `approved` (trigger: Approve, permission: `update:approve`)
- `pending` → `rejected` (trigger: Reject, permission: `update:approve`)
- `pending` → `cancelled` (trigger: Cancel, permission: `update:submit`)
- `approved` → `executed` (trigger: Execute, permission: `update:execute`)

`rejected`, `cancelled`, and `executed` are terminal states. Invalid transitions return `409 Conflict`.

---

## Data Model

New migration adds `update_request`:

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK, v7 |
| `server_id` | `uuid` | FK → `server`, SET NULL on delete |
| `submitter_id` | `uuid` | FK → `user`, SET NULL on delete |
| `sql_text` | `text` | The mutating SQL as submitted |
| `reason` | `text` | Required free-text from submitter |
| `status` | `varchar(16)` | `pending`, `approved`, `rejected`, `cancelled`, `executed` |
| `reviewer_id` | `uuid` | FK → `user`, SET NULL on delete — set on approve/reject |
| `review_note` | `text` | Nullable — required when approving or rejecting |
| `executor_id` | `uuid` | FK → `user`, SET NULL on delete — set on execute |
| `submitted_at` | `timestamptz` | |
| `reviewed_at` | `timestamptz` | Nullable |
| `executed_at` | `timestamptz` | Nullable |
| `exec_success` | `bool` | Nullable — set after execution attempt |
| `exec_duration_ms` | `int` | Nullable |
| `exec_affected_rows` | `int` | Nullable — null on failure |
| `exec_error` | `text` | Nullable — null on success |

All three user FKs use SET NULL on delete so history survives user removal. `server_id` also uses SET NULL on delete.

---

## API Endpoints

All routes under `/api/update`. Access requires at least one `update:*` permission for reads; specific permissions for write actions.

| Method | Route | Permission | Description |
|---|---|---|---|
| `POST` | `/api/update` | `update:submit` | Submit a new request |
| `GET` | `/api/update` | any `update:*` | List all requests, newest first |
| `GET` | `/api/update/{id}` | any `update:*` | Get a single request |
| `POST` | `/api/update/{id}/approve` | `update:approve` | Approve with a required note |
| `POST` | `/api/update/{id}/reject` | `update:approve` | Reject with a required note |
| `POST` | `/api/update/{id}/cancel` | `update:submit` | Cancel a pending request |
| `POST` | `/api/update/{id}/execute` | `update:execute` | Execute an approved request |

### Request Bodies

**Submit:** `{ "serverId": "uuid", "sqlText": "...", "reason": "..." }`

**Approve / Reject:** `{ "note": "..." }`

**Cancel / Execute:** no body.

### Responses

**List** returns a lightweight summary per item: `id`, `serverName`, `submitterName`, `reason` (full text), `status`, `submittedAt`. No `sqlText` to keep payloads light.

**Detail** returns the full record including `sqlText`, `reason`, reviewer info, `reviewNote`, executor info, and all execution result fields.

**State guard violations** return `409 Conflict` with a descriptive message.

**Submit** returns `201 Created` with the detail shape. All action endpoints (`approve`, `reject`, `cancel`, `execute`) return `200 OK` with the updated detail shape.

---

## Execution Flow

When `POST /api/update/{id}/execute` is called:

1. Load the request; return `404` if not found.
2. Fire the `Execute` trigger via the Stateless machine; return `409` if the current state is not `approved`.
3. Verify the server still exists and has write credentials configured; return `409` with a clear message if not.
4. Decrypt write credentials via `IServerConnectionFactory` using `CredentialKind.Write`.
5. Execute the SQL with a timeout (`Query:TimeoutSeconds` setting, default 30s). No transaction wrapper — the SQL has been reviewed and is executed as-is.
6. Capture affected rows from the Npgsql command result.
7. Persist the updated request: `status = executed`, populate `executor_id`, `executed_at`, and execution result fields (`exec_success`, `exec_duration_ms`, `exec_affected_rows` or `exec_error`).
8. Return the updated detail.

On timeout or SQL error the request is still marked `executed` with `exec_success = false`. It does not revert to `approved` — a new request must be submitted to retry.

---

## Frontend

### Navigation

A "Updates" nav link in the sidebar is shown to users holding any `update:*` permission (checked against the `me` response, consistent with the "Query" nav link pattern).

### Routes

| Route | Description |
|---|---|
| `/update` | List page — all requests |
| `/update/new` | New request form |
| `/update/$id` | Detail page |

The `/update` and `/update/$id` routes redirect to `/` if the user has no `update:*` permissions (i.e., none of `update:submit`, `update:approve`, `update:execute`). The `/update/new` route additionally redirects if the user lacks `update:submit` specifically.

### List page (`/update`)

A Mantine `Table` showing all requests newest-first. Columns: status badge (colour-coded), server name, submitter name, reason (truncated), submitted timestamp. Clicking a row navigates to `/update/$id`. Users with `update:submit` see a "New Request" button in the page header.

### New request form (`/update/new`)

Only accessible to users with `update:submit`. Contains:
- Server selector — only servers with write credentials configured (`hasWriteCredential = true`)
- CodeMirror SQL editor (same setup as query workspace: `@uiw/react-codemirror` + `@codemirror/lang-sql`, light/dark theme)
- Required reason textarea
- Submit button — posts and redirects to the new request's detail page on success

### Detail page (`/update/$id`)

Layout (top to bottom):
1. **SQL block** — read-only CodeMirror displaying `sqlText`
2. **Metadata** — server name, submitter name + reason, submitted timestamp
3. **Review section** — shown once `status` is not `pending`: reviewer name, review note, reviewed timestamp
4. **Execution section** — shown once `status` is `executed`: executor name, executed timestamp, success/failure badge, duration, affected rows (or error message)
5. **Action area** — one action shown at a time based on state + current user permissions:
   - `pending` + has `update:approve`: Approve and Reject buttons, each opening a modal with a required note field
   - `pending` + has `update:submit` (any submitter, not just the original): Cancel button
   - `approved` + has `update:execute`: Execute button with a confirmation dialog
   - Terminal states: no action buttons

### Hooks

New hooks in `hooks.ts`:
- `useUpdateRequests()` — `useQuery` for the list
- `useUpdateRequest(id)` — `useQuery` for a single request
- `useSubmitUpdate()` — `useMutation`, invalidates list on success
- `useApproveUpdate()` — `useMutation`, invalidates list and detail on success
- `useRejectUpdate()` — `useMutation`, invalidates list and detail on success
- `useCancelUpdate()` — `useMutation`, invalidates list and detail on success
- `useExecuteUpdate()` — `useMutation`, invalidates list and detail on success

---

## Testing

### Backend

Integration tests (following existing patterns in the test project):

- State machine guard tests: each invalid transition returns `409` (approve an executed request, cancel an approved request, execute a pending request, etc.)
- Full happy-path flow: `pending → approved → executed` with correct result fields
- Execution failure path: SQL error marks request `executed` with `exec_success = false` and `exec_error` set
- Cancel guard: only works when `pending`
- Missing write credential: `409` when executing against a server with no write credentials configured

### Frontend

Vitest unit tests for all seven hooks — happy path and error/failure cases.

E2E (Playwright) is out of scope for this iteration given existing specs are already flaky.

---

## Dependencies

- **Stateless** NuGet package — add to `SluiceBase.Api.csproj`
- No new frontend packages required

---

## Out of Scope

- Email or in-app notifications when a request changes state
- Filtering or searching the request list
- Pagination of the request list
- Re-submit / edit a rejected or cancelled request (new submission required)
- Row-level permission scoping (submitters see only their own requests)
