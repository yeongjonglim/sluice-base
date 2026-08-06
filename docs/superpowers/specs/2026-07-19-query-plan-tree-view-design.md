# Query Plan Tree Visualization — Design

**Date:** 2026-07-19
**Status:** Approved (design), pending implementation plan
**Depends on:** the explain feature from `feat/query-plan-check` (PR #177) — `PlanView`, `ExplainEntry`, and the full `planJson` already returned by `/api/query/explain`.
**Supersedes:** the "Full interactive plan-tree visualization — deferred" item in `2026-07-16-query-plan-check-design.md`.

## Problem

The current plan display (`PlanView`) shows summary badges plus a collapsible raw
`EXPLAIN (FORMAT JSON)` blob. The raw JSON is hard to read and does not surface
where a query actually spends its cost/time. Users asked for a more visual
rendering.

## Prior art (what established tools converge on)

Studied PEV2 / explain.dalibo.com, explain.depesz.com, pgMustard, and pgAdmin.
The consistent pattern across them:

- A **collapsible tree** mirroring the plan structure; large plans collapse their
  cheap subtrees by default.
- Per node: node type + object (relation/index), estimated cost, **estimated vs
  actual rows**, and **self-time + loops** when `ANALYZE`'d.
- A **selectable heat metric** (PEV2's signature): switch node coloring/bars
  between time / rows / cost.
- **Highlight the single most expensive node** by the selected metric.
- Flag **row mis-estimates** (actual vs estimate) — pgMustard's top diagnostic —
  and sequential scans.
- The one derived metric they all compute is **self-time**: Postgres reports
  *inclusive* times, per loop, so the real culprit only emerges after subtracting
  children × loops.

We deliberately do **not** copy pgMustard's automated advice/tip-scoring engine
or PEV2's pan/zoom node graph — both are large separate efforts.

## Decisions (locked)

- **Frontend-only.** The backend already returns the full `planJson`; no API,
  contract, or engine change.
- Node tree becomes the **primary** plan view; the existing raw-JSON panel stays
  as a **secondary** collapsible toggle.
- **Heat metric toggle**: Time / Rows / Cost. Default **Time** when the plan has
  actual timings (`ANALYZE`), otherwise **Cost**. (Rows always available.)
- **Self-time** is the ANALYZE time metric (inclusive − children, loop-adjusted),
  computed in a well-tested pure helper.
- Highlight the single hottest node by the active metric.
- Two per-node flags: **row mis-estimate** (actual ÷ estimate ≥ 10× in either
  direction, ANALYZE only) and **sequential scan**.
- Auto-collapse subtrees whose weight (by the active metric) is negligible on
  large plans; everything expandable/collapsible by click.
- Session-only, consistent with the parent feature — no persistence.

## Architecture

All changes are under `src/frontend/`.

### 1. Plan parsing + derived metrics (pure, testable)

New `src/frontend/src/utils/queryPlan.ts`:

- `parsePlan(planJson: string): PlanNode | null` — parse the `EXPLAIN (FORMAT
  JSON)` document (array with one root object holding `Plan` and, when analyzed,
  top-level `Execution Time` / `Planning Time`). Returns a normalized tree.
- `PlanNode` normalized shape (camel-cased from Postgres' spaced keys):
  ```ts
  interface PlanNode {
    nodeType: string;              // "Seq Scan", "Hash Join", ...
    object: string | null;        // relation/index/function name if any
    totalCost: number;            // inclusive planner cost
    planRows: number;             // estimated rows (per loop)
    actualTotalTimeMs: number | null;  // inclusive, per loop
    actualRows: number | null;    // per loop
    actualLoops: number | null;
    selfTimeMs: number | null;    // derived: see below
    isSeqScan: boolean;
    misestimateFactor: number | null; // derived: max(actual/est, est/actual)
    children: Array<PlanNode>;
  }
  ```
- **Self-time derivation** (`computeSelfTimes`): a node's inclusive total time is
  `actualTotalTimeMs × actualLoops`; its self-time is that minus the sum of each
  child's inclusive total time (`child.actualTotalTimeMs × child.actualLoops`),
  floored at 0. Estimate-only plans leave `selfTimeMs` null.
- **Mis-estimate** (`misestimateFactor`): ANALYZE only; `null` when no actuals.
  `max(actualRows, 1) / max(planRows, 1)` and its reciprocal, take the larger;
  the flag renders when the factor ≥ 10.
- Metric accessors: `nodeWeight(node, metric)` returning the value used for heat
  bar + hottest-node selection — `metric: "time" | "rows" | "cost"` maps to
  `selfTimeMs` (fallback 0), `actualRows ?? planRows`, `totalCost`.

Keeping this file pure (no React) makes the self-time/mis-estimate math directly
unit-testable, which is where correctness risk lives.

### 2. Components

- `PlanNodeRow.tsx` — one node's row: indentation + expand/collapse chevron
  (when it has children), node type + object, a proportional **heat bar** (width
  = node weight ÷ max node weight for the active metric; color ramps
  green→orange→red by share), the numeric metrics (cost + est rows always;
  self-time, actual rows, loops when analyzed), and flags (⚠ seq scan; "est N vs
  actual M (K×)" mis-estimate). The hottest node (max weight) gets a red accent.
- `PlanTree.tsx` — owns the parsed tree + collapse state + the active metric
  toggle (a small segmented control: Time / Rows / Cost, Time disabled/hidden when
  the plan has no actuals). Renders the flattened visible rows. Auto-collapses
  nodes below a weight threshold when the node count exceeds a limit (e.g. > 25
  nodes), still expandable.
- `PlanView.tsx` (modify) — success branch renders `PlanSummaryBadges` (kept as
  the at-a-glance header) + `PlanTree`, then the existing raw-JSON `Collapse` as a
  secondary toggle below. Pending/blocked/error branches unchanged. If
  `parsePlan` returns null (unparseable), fall back to just the raw-JSON panel so
  the view never breaks.

### 3. Layout / behavior

- The tree scrolls within its pane (`overflow: auto`); long object names extend
  horizontally inside the scroll container, matching the existing result-grid
  scrolling conventions.
- Switching the metric toggle recolors bars and re-picks the hottest node without
  refetching (all data is client-side).
- Theme-aware colors via Mantine tokens; the heat ramp works in light and dark.

## Testing

Pure helper (`queryPlan.ts`) carries the correctness load:

- `parsePlan`: nested tree from a realistic estimate-only JSON and an
  ANALYZE JSON fixture; unparseable input → null.
- `computeSelfTimes`: parent with children, verifying self = inclusive −
  Σ(child inclusive), and loop-adjustment (a child with `loops > 1`); floored at 0
  when rounding would go negative.
- `misestimateFactor`: ≥10× over- and under-estimate both flagged; within 10× not
  flagged; null when no actuals.
- `nodeWeight`: correct field per metric, and the estimate-only fallback.

Component tests:

- `PlanTree`: renders a multi-node tree; the hottest node is marked; the metric
  toggle switches which node is hottest (e.g. costliest-by-cost differs from
  slowest-by-time); collapse hides a subtree's rows; Time toggle absent for an
  estimate-only plan.
- `PlanNodeRow`: seq-scan flag renders; mis-estimate flag shows "N× "; heat bar
  width reflects relative weight.
- `PlanView`: success renders the tree + raw toggle; unparseable plan falls back
  to raw-only; pending/blocked/error unchanged.

## Explicitly out of scope (deferred)

- Automated advice / tip-scoring (pgMustard style).
- Pan/zoom graph layout (PEV2 graph mode).
- Buffers/I/O heat metric (add later if `BUFFERS` output proves useful).
- Any backend/threshold/persistence change.
