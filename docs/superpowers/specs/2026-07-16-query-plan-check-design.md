# Query Plan Check & Performance Inspection — Design

**Date:** 2026-07-16
**Status:** Approved (design), pending implementation plan
**Engine scope:** PostgreSQL only (MongoDB shelved — Phase 2 reverted from main)

## Problem

Users of the query playground can accidentally run heavy queries that strain or
crash the target database. There is no way to preview a query's cost before
running it, and no way to inspect a specific query's execution performance.

We want two capabilities:

1. **Safety guardrail** — surface a query's estimated cost *before* (or alongside)
   execution so users notice an accidental heavy query.
2. **Performance inspection** — let users deliberately check a specific query's
   plan and, optionally, real execution timings. This information must be
   **well visible**.

## Decisions (locked)

- **Two entry points:** a manual **Explain** button *and* an automatic
  **advisory estimate** shown on normal runs.
- **No threshold / no blocking yet.** The advisory is informational. The seam is
  placed so a threshold-based gate can be added later without rework. We defer
  threshold design until we observe real usage.
- **Depth:** estimate-only (`EXPLAIN`) by default; explicit **"Explain with
  timings"** (`EXPLAIN ANALYZE`, which executes the query) behind a deliberate
  click.
- **Display:** parsed **summary badges** + a collapsible **raw plan** panel.
  (Not a full interactive plan-tree — deferred.)
- **PostgreSQL only**, implemented behind the existing `ITargetEngine`
  abstraction.
- **Session-only.** No persistence, no DB migration. Plan results are discarded
  on navigate/refresh.

## Architecture

### 1. Core abstraction (`SluiceBase.Core`)

Add one method to `ITargetEngine` and two result records:

```csharp
// ITargetEngine.cs
Task<QueryPlan> ExplainAsync(
    string connectionString, string sql, bool analyze, CancellationToken ct);

// SluiceBase.Core/Queries
public sealed record QueryPlan(string PlanJson, QueryPlanSummary Summary);

public sealed record QueryPlanSummary(
    double TotalCost,
    double EstimatedRows,
    string RootNode,
    bool HasSeqScan,
    double? ActualTotalMs);   // populated only when analyze == true
```

`PlanJson` is the verbatim `EXPLAIN (FORMAT JSON)` output, rendered in the raw
panel. `QueryPlanSummary` is parsed server-side from the root plan node so the
contract is typed for OpenAPI/`schema.ts` and so a future threshold check has
structured numbers to read.

### 2. Postgres engine (`PostgresTargetEngine.ExplainAsync`)

- Wrap in the same `SET TRANSACTION READ ONLY` transaction as
  `ExecuteQueryAsync`, then **roll back** (never commit). This guarantees even an
  `EXPLAIN ANALYZE` of a write statement fails safely rather than mutating data.
- Build `EXPLAIN (FORMAT JSON[, ANALYZE true, BUFFERS true])` + the user SQL.
  `ANALYZE`/`BUFFERS` are added only when `analyze == true`.
- Execute, read the single JSON cell, parse the root plan node into
  `QueryPlanSummary` (`Total Cost`, `Plan Rows`, `Node Type`, presence of any
  `Seq Scan` node; `Actual Total Time` when analyzed).
- Non-explainable statements (`SET`, `BEGIN`, DDL, etc.) surface as a soft
  "not explainable" result rather than a hard error, so the UI can note it and
  move on.

### 3. Service + endpoint (`SluiceBase.Api`)

- **`QueryService.ExplainAsync(user, databaseId, sql, analyze, ct)`** reuses the
  existing **permission** check (`query:execute` on the database) and the
  **sensitive-column** check. The sensitive-column block applies to *both*
  estimate and analyze: `EXPLAIN` leaks index names and row estimates, and
  `ANALYZE` executes the query outright. Behaviour mirrors the existing
  `ExecuteAsync` sensitive path (Blocked outcome).
- **New endpoint** `POST /api/query/explain` with body
  `{ databaseId, sql, analyze }` returning `QueryPlanResponse`. Outcome mapping
  mirrors `/api/query`: NotFound / Forbid / Blocked-sensitive (403 problem with
  `columns`) / BadRequest / Ok.

- **Automatic advisory (inline, best-effort):** in
  `QueryService.ExecuteAsync`, before executing each statement, run the
  estimate-only `EXPLAIN` and attach a nullable `QueryPlanSummary` to the
  statement's `QueryResponse`. Properties:
  - One HTTP request — the estimate travels atomically with the result.
  - Plan-only, so cheap (no execution); failures are swallowed (estimate = null)
    and never fail the real query.
  - Gated by a `Query:AutoExplain` config flag (default **on**) so operators can
    disable it.
  - This placement puts a future threshold gate in the right spot: between the
    pre-flight estimate and execution.

  *Alternative considered and rejected:* the frontend calls `/api/query/explain`
  separately just before Run. Rejected — two round-trips, splits the logic
  across client/server, and the query runs regardless of the estimate.

### 4. Frontend (query playground)

Builds on this branch's multi-result work (`ResultTabs`, `ResultGrid`,
`useQueryRuns`, statement provenance).

- **`useExplainQuery`** mutation hook calling `POST /api/query/explain`.
- **Explain split-button** beside Run: primary action "Explain" (estimate-only)
  with a menu item "Explain with timings (runs the query)" for `ANALYZE`. It
  respects the same **selection / cursor / all** statement scoping as Run.
- **Plan view** per statement, reusing the `ResultTabs` provenance pattern:
  - Summary badges: estimated rows, estimated cost, root node type, ⚠ seq-scan
    indicator; plus actual time / actual rows when analyzed.
  - Collapsible, pretty-printed raw-plan panel (the `PlanJson`).
- **Advisory strip** above the result grid on a normal run when a
  `QueryPlanSummary` is attached. Neutral/informational styling now; when
  thresholds arrive, this strip upgrades to a warning.
- When the engine reports "not explainable," the plan view shows a brief note
  instead of badges.

## Testing

- **Engine (Testcontainers):** estimate returns cost/rows; `ANALYZE` returns
  actual timings; read-only transaction blocks a write statement's `ANALYZE`;
  summary parser unit test against a fixed `EXPLAIN (FORMAT JSON)` fixture.
- **Endpoint integration:** 200 with plan; 403 sensitive-column; 403 forbidden
  (no `query:execute`); 400 on invalid SQL.
- **Frontend:** `useExplainQuery` hook test; plan-summary and advisory-strip
  render tests; statement-scoping test for the Explain button.

## Verification / follow-ups during implementation

- **Gateway route** (`AppHost` YARP): `/api/query/explain` sits under the
  existing `/api/query` prefix — confirm the route is prefix-based; if it is
  exact-match, add the new route so requests don't fall through to Vite.
- **CI contract gate:** regenerate `openapi.json` and `schema.ts` after adding
  the endpoint.

## Explicitly out of scope (deferred)

- Threshold-based warnings or hard blocking (revisit after observing usage).
- Persisting plans to query history / `QueryLog`.
- Full interactive plan-tree visualization.
- MongoDB `explain()` support (feature shelved).
