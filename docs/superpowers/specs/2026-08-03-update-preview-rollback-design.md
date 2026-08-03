# Update Request — Preview (Rollback) & Result Return

**Date:** 2026-08-03
**Issue:** [#202](https://github.com/yeongjonglim/sluice-base/issues/202)
**Route (frontend):** `src/frontend/src/routes/_authed/update/$id.tsx`
**Scope:** Backend engine seam + new preview endpoint + new append-only event
entity + migration; frontend result display and timeline on the update-detail
page. Reuses the multi-result `ResultTabs` / `ResultGrid` components and the
existing sensitive-column checker **as-is**.

## Goal

Let an approved-or-pending update request be **previewed**: run its SQL as one
transaction, capture **every** result set it produces, return them to the
frontend, and then **roll back** — no commit, no status change. Each preview is
recorded on the request's timeline (metadata only). Sensitive-column policy
applies to the returned data exactly as it does on the read path.

## Explicitly out of scope

- **In-place editing / auto-revoke / `Edited` events.** Deferred. The event log
  introduced here is designed to absorb them later with no rework.
- **Fixing the `*` / CTE over-block in `SqlColumnChecker`.** Tracked separately.
  Preview inherits the checker's current (conservative) behaviour.
- **Surfacing result sets from the commit (`execute`) path.** `execute` keeps its
  current external behaviour; see §3.

## Background — current state

- `ITargetEngine.ExecuteUpdateAsync(conn, sql, ct) → Task<int>` runs the SQL in a
  transaction and **commits unconditionally**, returning affected rows only
  (`PostgresTargetEngine.cs:670`).
- `UpdateEndpoints.Execute` (Approved → Executed) records affected rows into
  columns on `UpdateRequest`; no result set is surfaced.
- The timeline (`update/$id.tsx:179`) is reconstructed from **single-valued
  columns** (`reviewedAt`, `executedAt`, …) — it cannot represent an event that
  can occur more than once.
- Reads return a `QueryData` grid and gate sensitive columns via
  `SqlColumnChecker.FindBlockedColumns` + `UserColumnBypass`, returning a
  `sensitive_columns` 403 problem on a hit (`QueryEndpoints.cs:44`).

## Design

### 1. Engine seam — result sets + commit flag

Replace

```csharp
Task<int> ExecuteUpdateAsync(string connectionString, string sql, CancellationToken ct);
```

with

```csharp
Task<UpdateExecutionResult> ExecuteUpdateAsync(
    string connectionString, string sql, bool commit, CancellationToken ct);

public sealed record UpdateExecutionResult(
    IReadOnlyList<QueryData> ResultSets,
    int AffectedRows);
```

**Postgres implementation.** Open connection + transaction (as today), then
`ExecuteReaderAsync`, looping `reader.NextResultAsync()` to collect **each**
result set into a `QueryData`. This is the native single-transaction
multi-result path the read playground deliberately avoided — correct *here*
because a preview is one governed SQL blob run atomically so it can be rolled
back. Reuse the existing row-reading/formatting logic from `ExecuteQueryAsync`
(including the `interval` / `interval[]` handling) by extracting a shared
`ReadResultSet(reader, ct)` helper so both paths format values identically.
Capture `reader.RecordsAffected` for `AffectedRows`. Finally `CommitAsync` when
`commit` is true, else `RollbackAsync`.

Statements with no result set (a plain `UPDATE` without `RETURNING`) contribute
no `QueryData`; `AffectedRows` is still captured.

### 2. Preview endpoint — `POST /api/update/{id}/preview`

A **dedicated** endpoint (not a flag on `/execute`) because preview and execute
diverge on authorization, allowed states, and side-effects. It is a sub-route of
the existing `/api/update` group, so no AppHost gateway change is needed (the
gateway forwards `/api` wholesale).

Response:

```csharp
public sealed record UpdatePreviewResponse(
    IReadOnlyList<QueryData> ResultSets,
    int AffectedRows,
    int DurationMs,
    string? Error);
```

Request handling, in order:

1. **Load** the request (tracked). 404 if missing.
2. **State guard:** allowed only in `Pending` or `Approved`. Otherwise 409.
3. **Server guard:** database exists, not disabled, `CanWrite`. Otherwise 409.
4. **Authorization:**
   ```
   canPreview =
         user.Id == request.SubmitterId              // submitter can preview their own, even with only update:submit
      || resolver.HasDatabasePermission(user, db, update:approve)
      || resolver.HasDatabasePermission(user, db, update:execute)
   ```
   Otherwise 403 Forbidden.
5. **Sensitive-column gate:** compute the *previewer's* blocked columns (the
   database's `SensitiveColumn`s minus that user's `UserColumnBypass`), run
   `SqlColumnChecker.FindBlockedColumns(request.SqlText, blocked)`. On any hit,
   return the **same** `sensitive_columns` 403 problem the read endpoint returns.
   The SQL does **not** run and **no** event is written (nothing happened).
6. **Run** the engine with `commit: false`, under the existing
   `Query:TimeoutSeconds` linked-CTS pattern used by `Execute`.
7. **Record** an `UpdateRequestEvent` of type `Previewed` (metadata only — see
   §4). Written for both success and SQL-error outcomes; row data is **never**
   persisted.
8. **Return** `200` with the result sets (one-time display). On SQL error or
   timeout, return `200` with empty `ResultSets` and `Error` set, so the grid
   area renders the error inline (matching how `ResultGrid` shows query errors).

The endpoint **never** transitions state.

### 3. Execute endpoint — engine signature only

`Execute` now calls the engine with `commit: true` and reads `AffectedRows` off
`UpdateExecutionResult` (ignoring `ResultSets`). Everything else is unchanged:
it still records affected rows into `UpdateRequest` columns, transitions to
`Executed`, and returns the existing detail response. Because `execute` does not
surface result sets, it needs no new sensitive-column gate. Surfacing the
committed result grid is a deliberate future enhancement, not part of this cut.

### 4. `UpdateRequestEvent` — append-only log

A **general** event entity (not a preview-specific table), so the deferred edit
work slots in as new event types with no schema rework. Only `Previewed` events
are written in this cut; existing transitions stay column-derived and the
timeline **merges** the two (additive model).

```csharp
public sealed class UpdateRequestEvent
{
    public UpdateRequestEventId Id { get; private set; }
    public UpdateRequestId RequestId { get; private set; }
    public UpdateRequestEventType Type { get; private set; }   // Previewed (+ future: Submitted/Approved/Rejected/Cancelled/Executed/Edited)
    public UserId? ActorId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string? Note { get; private set; }                  // reason / review note / cancel note / edit rationale — null for Previewed
    public bool? Success { get; private set; }
    public int? DurationMs { get; private set; }
    public int? AffectedRows { get; private set; }
    public int? ResultSetCount { get; private set; }
    public string? Error { get; private set; }

    // EF navigations
    public UpdateRequest? Request { get; private set; }
    public User? Actor { get; private set; }

    public static UpdateRequestEvent Preview(
        UpdateRequestId requestId, Actioned by,
        bool success, int durationMs, int affectedRows, int resultSetCount, string? error) => new() { … };
}

public enum UpdateRequestEventType { Previewed }
```

- **Fit for the deferred full migration.** The metric columns are all nullable,
  so lifecycle events (Submit/Approve/Reject/Cancel/Execute) degrade to null on
  them; the general `Note` column carries their reason / review note / cancel
  note. With those, every current and near-future event type fits this one table
  — typed nullable columns per type, mirroring how `UpdateRequest` already models
  its own lifecycle (rather than a JSON payload blob). The **only** field a future
  event needs that this schema lacks is `SqlSnapshot` for the `Edited` event; it
  is edit-specific (snapshot-after vs. before/after is a real design choice) and
  is added with the edit feature, which needs a migration regardless. `Previewed`
  leaves `Note` null.
- `UpdateRequestEventId` is a Vogen id (`FromNewVersion7Guid`), matching the
  other ids.
- New `DbSet<UpdateRequestEvent> UpdateRequestEvents`, an EF configuration under
  `Data/Configurations/`, and a regenerated branch migration (never hand-edited;
  analyzer warnings suppressed via the `.editorconfig` `Migrations` section; the
  branch's own migration is squashed rather than stacked).
- `Get` (detail) includes the request's events, ordered by `At`, in
  `UpdateRequestDetailResponse` as `Array<UpdateRequestEventItem>` with the
  actor's display name resolved.

### 5. Frontend — `update/$id.tsx`

- **Preview button:** visible when `canPreview` (submitter-of-record, or holds
  `update:approve` / `update:execute`) **and** status ∈ {`Pending`, `Approved`}.
- **Result display:** on click, `POST /preview` via a new `usePreviewUpdate`
  hook; render the returned grids with the reused `ResultTabs` / `ResultGrid`.
  Handle the `sensitive_columns` 403 with the same blocked-columns UI reads use.
  Inline `Error` renders in the grid area.
- **Timeline:** merge `Previewed` events from the detail response with the
  existing derived events, sorted by timestamp. A preview item shows
  *actor · time · "Previewed" · N rows affected · M result sets* (or the error /
  duration on failure).
- All new array types use `Array<T>` per the ESLint rule.

## Data flow (preview)

```
Preview click → POST /api/update/{id}/preview
  → load + state ∈ {Pending, Approved} + server writable
  → authz: submitter-of-record | update:approve | update:execute
  → sensitive-column gate (SqlColumnChecker vs previewer's blocked cols)
        → hit → 403 sensitive_columns   (no run, no event)
  → engine.ExecuteUpdateAsync(conn, sql, commit:false)
        → txn: ExecuteReader → loop NextResult → Array<QueryData>,
          RecordsAffected, ROLLBACK
  → write UpdateRequestEvent(Previewed, metadata only)
  → 200 { resultSets, affectedRows, durationMs, error? }
Frontend → ResultTabs / ResultGrid (one-time) + timeline shows Previewed event
```

## Error handling

| Case | Result |
|---|---|
| Request missing | 404 |
| Status ∉ {Pending, Approved} | 409 Conflict |
| Server deleted / not writable | 409 Conflict |
| Not authorized to preview | 403 Forbidden |
| Sensitive columns hit | 403 `sensitive_columns` problem; no run, no event |
| SQL error | 200, empty `ResultSets`, `Error` set; event Success=false |
| Timeout (`Query:TimeoutSeconds`) | 200, `Error` = "timed out …"; event Success=false |
| Success | 200 with result sets; event Success=true |

## Testing

- **Engine (Testcontainers):** multi-result-set capture; `RETURNING` captured;
  `AffectedRows` correct; **rollback leaves the database unchanged**; commit path
  still writes.
- **Endpoint:** authorization matrix (submitter-only-own vs approve vs execute vs
  none); state guard; sensitive-column block (no event written); SQL-error and
  timeout produce a `Previewed` event with `Success=false`; success writes the
  event; **no** state transition in any case.
- **Frontend:** preview-button visibility per `canPreview` × status; grids
  render; blocked UI; timeline shows preview events; `usePreviewUpdate` hook.
- **Contract:** OpenAPI + `schema.ts` regenerated (CI-gated) for the new endpoint
  and response types.

## Decisions recorded

- **Rollback model:** preview-then-commit via a dedicated `/preview` endpoint; no
  transaction held open across HTTP round-trips.
- **Event store:** general `UpdateRequestEvent` log (not a preview-specific
  table), additive — only `Previewed` written now; existing events stay
  column-derived; timeline merges.
- **Sensitive columns:** reuse the read-path checker as-is (block, not redact);
  the `*` / CTE over-block fix is out of scope here.
- **Preview permission:** submitter-of-record **or** `update:approve` **or**
  `update:execute`.
- **Result data:** one-time display in the response; only metadata is persisted.
- **Execute:** engine signature changes only; no result grid, no new gate.
