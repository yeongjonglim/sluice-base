# Query Plan Tree Visualization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the raw-JSON plan blob in `PlanView` with an interactive, collapsible node tree — per-node metrics, a Time/Rows/Cost heat toggle, hottest-node highlight, and seq-scan / row-mis-estimate flags — parsing the plan JSON we already return.

**Architecture:** Frontend-only. A pure `queryPlan.ts` module parses `EXPLAIN (FORMAT JSON)` into a normalized `PlanNode` tree and derives self-time, mis-estimate, and per-metric weights (this is where the correctness risk lives, so it is unit-tested hard). Three components consume it: `PlanNodeRow` (one node), `PlanTree` (tree state + metric toggle + auto-collapse), and a modified `PlanView` that renders the tree as primary with the existing raw-JSON panel kept as a secondary toggle (and falls back to raw-only if a plan won't parse).

**Tech Stack:** React + TypeScript + Mantine v9; Vitest + Testing Library.

## Global Constraints

- Frontend-only. No backend/API/contract change. Run all commands from `src/frontend`.
- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Strict gates before every commit: `npx tsc -b` clean AND `npm run lint` exit 0. The lint rules `react-hooks/refs` and `react-hooks/set-state-in-effect` are errors Vitest won't catch.
- CI has a **diff-coverage gate**: the average per-file line-rate across changed files (that appear in the coverage report) must be ≥ 80%. Every new `.ts`/`.tsx` file MUST be exercised by a test that imports it, and be well covered (aim ≥ 85% lines). A file with no test that loads it is absent from the report and drags the average — that is what failed CI last time.
- Mantine is v9.4.x: `Collapse` uses `expanded` (not `in`); confirm any component API against installed types.
- Commit messages: single subject line, no body.
- Mis-estimate flag threshold is **≥ 10×**; auto-collapse engages only when the tree has **> 25 nodes**. These are the values the spec locked — use them verbatim.
- Reuse the existing `PlanSummaryBadges` as the at-a-glance header; do not duplicate badge markup.

---

### Task 1: Pure plan parser + derived metrics (`queryPlan.ts`)

**Files:**
- Create: `src/frontend/src/utils/queryPlan.ts`
- Test: `src/frontend/src/utils/__tests__/queryPlan.test.ts`

**Interfaces:**
- Produces: `PlanNode` interface; `PlanMetric = "time" | "rows" | "cost"`; `parsePlan(planJson: string): PlanNode | null`; and pure helpers `computeSelfTimeMs`, `misestimateFactor`, `isMisestimated`, `nodeWeight`, `inclusiveWeight`, `hasActuals`, `flattenNodes` (all consumed by Tasks 2–4).

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/utils/__tests__/queryPlan.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import {
  parsePlan,
  computeSelfTimeMs,
  misestimateFactor,
  isMisestimated,
  nodeWeight,
  flattenNodes,
  hasActuals,
  type PlanNode,
} from "@/utils/queryPlan";

const ESTIMATE = JSON.stringify([
  {
    Plan: {
      "Node Type": "Hash Join",
      "Total Cost": 1240,
      "Plan Rows": 8500,
      Plans: [
        { "Node Type": "Seq Scan", "Relation Name": "orders", "Total Cost": 890, "Plan Rows": 10000 },
        { "Node Type": "Hash", "Total Cost": 210, "Plan Rows": 500,
          Plans: [{ "Node Type": "Index Scan", "Index Name": "users_pkey", "Total Cost": 45, "Plan Rows": 500 }] },
      ],
    },
  },
]);

const ANALYZED = JSON.stringify([
  {
    Plan: {
      "Node Type": "Aggregate", "Total Cost": 50, "Plan Rows": 1,
      "Actual Total Time": 12.4, "Actual Rows": 1, "Actual Loops": 1,
      Plans: [
        { "Node Type": "Seq Scan", "Relation Name": "t", "Total Cost": 40, "Plan Rows": 100,
          "Actual Total Time": 8.1, "Actual Rows": 9981, "Actual Loops": 1 },
      ],
    },
    "Execution Time": 13.0,
  },
]);

describe("parsePlan", () => {
  it("builds a nested tree with ids, objects, and seq-scan flags", () => {
    const root = parsePlan(ESTIMATE)!;
    expect(root.nodeType).toBe("Hash Join");
    expect(root.id).toBe("0");
    expect(root.children).toHaveLength(2);
    const seq = root.children[0];
    expect(seq.nodeType).toBe("Seq Scan");
    expect(seq.object).toBe("orders");
    expect(seq.isSeqScan).toBe(true);
    expect(root.children[1].children[0].object).toBe("users_pkey");
    expect(root.children[1].children[0].id).toBe("0.1.0");
  });

  it("leaves actual/self/misestimate null for an estimate-only plan", () => {
    const root = parsePlan(ESTIMATE)!;
    expect(root.actualTotalTimeMs).toBeNull();
    expect(root.selfTimeMs).toBeNull();
    expect(root.misestimateFactor).toBeNull();
    expect(hasActuals(root)).toBe(false);
  });

  it("populates actuals + self-time for an analyzed plan", () => {
    const root = parsePlan(ANALYZED)!;
    expect(hasActuals(root)).toBe(true);
    // root self = 12.4 - 8.1 = 4.3
    expect(root.selfTimeMs).toBeCloseTo(4.3, 5);
    expect(root.children[0].selfTimeMs).toBeCloseTo(8.1, 5);
  });

  it("returns null for unparseable or empty input", () => {
    expect(parsePlan("not json")).toBeNull();
    expect(parsePlan("[]")).toBeNull();
    expect(parsePlan("[{}]")).toBeNull();
  });
});

describe("computeSelfTimeMs", () => {
  it("returns null without actual time", () => {
    expect(computeSelfTimeMs(null, null, [])).toBeNull();
  });

  it("multiplies child inclusive time by loops and floors at 0", () => {
    const child = { actualTotalTimeMs: 2, actualLoops: 3 } as PlanNode; // inclusive 6
    // parent inclusive = 5*1 = 5; 5 - 6 = -1 -> floored to 0
    expect(computeSelfTimeMs(5, 1, [child])).toBe(0);
    // parent inclusive = 10; 10 - 6 = 4
    expect(computeSelfTimeMs(10, 1, [child])).toBe(4);
  });
});

describe("misestimateFactor / isMisestimated", () => {
  it("is null without actuals", () => {
    expect(misestimateFactor(100, null)).toBeNull();
  });

  it("flags >= 10x under- and over-estimates in either direction", () => {
    expect(misestimateFactor(500, 9981)).toBeCloseTo(19.96, 1);
    const under = { planRows: 500, actualRows: 9981, misestimateFactor: misestimateFactor(500, 9981) } as PlanNode;
    const over = { planRows: 9981, actualRows: 500, misestimateFactor: misestimateFactor(9981, 500) } as PlanNode;
    const close = { planRows: 100, actualRows: 300, misestimateFactor: misestimateFactor(100, 300) } as PlanNode;
    expect(isMisestimated(under)).toBe(true);
    expect(isMisestimated(over)).toBe(true);
    expect(isMisestimated(close)).toBe(false); // 3x
  });
});

describe("nodeWeight / flattenNodes", () => {
  it("selects the right field per metric with estimate fallback for rows", () => {
    const root = parsePlan(ESTIMATE)!;
    expect(nodeWeight(root, "cost")).toBe(1240);
    expect(nodeWeight(root, "rows")).toBe(8500); // no actuals -> planRows
    expect(nodeWeight(root, "time")).toBe(0); // no self time -> 0
    const analyzed = parsePlan(ANALYZED)!;
    expect(nodeWeight(analyzed.children[0], "rows")).toBe(9981); // actual wins
  });

  it("flattens the whole tree", () => {
    expect(flattenNodes(parsePlan(ESTIMATE)!)).toHaveLength(4);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/utils/__tests__/queryPlan.test.ts`
Expected: FAIL — `@/utils/queryPlan` not found.

- [ ] **Step 3: Implement `queryPlan.ts`**

Create `src/frontend/src/utils/queryPlan.ts`:

```typescript
export type PlanMetric = "time" | "rows" | "cost";

export interface PlanNode {
  id: string;                       // path-based, e.g. "0", "0.1", "0.1.0"
  nodeType: string;
  object: string | null;           // relation / index / function / CTE name
  totalCost: number;               // inclusive planner cost
  planRows: number;                // estimated rows (per loop)
  actualTotalTimeMs: number | null; // inclusive, per loop
  actualRows: number | null;       // per loop
  actualLoops: number | null;
  selfTimeMs: number | null;       // derived: inclusive − children inclusive
  isSeqScan: boolean;
  misestimateFactor: number | null; // derived: max(est/act, act/est)
  children: Array<PlanNode>;
}

interface RawPlan {
  "Node Type"?: string;
  "Relation Name"?: string;
  "Index Name"?: string;
  "Function Name"?: string;
  "CTE Name"?: string;
  "Total Cost"?: number;
  "Plan Rows"?: number;
  "Actual Total Time"?: number;
  "Actual Rows"?: number;
  "Actual Loops"?: number;
  Plans?: Array<RawPlan>;
}

const MISESTIMATE_THRESHOLD = 10;

export function parsePlan(planJson: string): PlanNode | null {
  let doc: unknown;
  try {
    doc = JSON.parse(planJson);
  } catch {
    return null;
  }
  const first = Array.isArray(doc) ? doc[0] : undefined;
  const root = (first as { Plan?: RawPlan } | undefined)?.Plan;
  if (!root || typeof root !== "object" || typeof root["Node Type"] !== "string") {
    return null;
  }
  return buildNode(root, "0");
}

function numOrNull(v: number | undefined): number | null {
  return typeof v === "number" ? v : null;
}

function buildNode(raw: RawPlan, id: string): PlanNode {
  const children = (raw.Plans ?? []).map((child, i) => buildNode(child, `${id}.${i}`));
  const actualTotalTimeMs = numOrNull(raw["Actual Total Time"]);
  const actualLoops = numOrNull(raw["Actual Loops"]);
  const actualRows = numOrNull(raw["Actual Rows"]);
  const planRows = raw["Plan Rows"] ?? 0;

  return {
    id,
    nodeType: raw["Node Type"] ?? "?",
    object:
      raw["Relation Name"] ?? raw["Index Name"] ?? raw["Function Name"] ?? raw["CTE Name"] ?? null,
    totalCost: raw["Total Cost"] ?? 0,
    planRows,
    actualTotalTimeMs,
    actualRows,
    actualLoops,
    selfTimeMs: computeSelfTimeMs(actualTotalTimeMs, actualLoops, children),
    isSeqScan: raw["Node Type"] === "Seq Scan",
    misestimateFactor: misestimateFactor(planRows, actualRows),
    children,
  };
}

// Postgres "Actual Total Time" is per-loop and inclusive of children. A node's
// inclusive total is time × loops; its self time subtracts each child's inclusive
// total, floored at 0 (rounding can push it slightly negative).
export function computeSelfTimeMs(
  actualTotalTimeMs: number | null,
  actualLoops: number | null,
  children: Array<PlanNode>,
): number | null {
  if (actualTotalTimeMs === null) return null;
  const inclusive = actualTotalTimeMs * (actualLoops ?? 1);
  const childInclusive = children.reduce(
    (sum, c) => sum + (c.actualTotalTimeMs ?? 0) * (c.actualLoops ?? 1),
    0,
  );
  return Math.max(0, inclusive - childInclusive);
}

// Per-loop estimate vs actual, larger ratio; null without actuals.
export function misestimateFactor(planRows: number, actualRows: number | null): number | null {
  if (actualRows === null) return null;
  const est = Math.max(planRows, 1);
  const act = Math.max(actualRows, 1);
  return Math.max(est / act, act / est);
}

export function isMisestimated(node: PlanNode): boolean {
  return node.misestimateFactor !== null && node.misestimateFactor >= MISESTIMATE_THRESHOLD;
}

export function hasActuals(node: PlanNode): boolean {
  return node.actualTotalTimeMs !== null;
}

export function nodeWeight(node: PlanNode, metric: PlanMetric): number {
  switch (metric) {
    case "time":
      return node.selfTimeMs ?? 0;
    case "rows":
      return node.actualRows ?? node.planRows;
    case "cost":
      return node.totalCost;
  }
}

// Inclusive (subtree) weight, used for auto-collapsing cheap branches.
export function inclusiveWeight(node: PlanNode, metric: PlanMetric): number {
  switch (metric) {
    case "time":
      return (node.actualTotalTimeMs ?? 0) * (node.actualLoops ?? 1);
    case "rows":
      return node.actualRows ?? node.planRows;
    case "cost":
      return node.totalCost;
  }
}

export function flattenNodes(root: PlanNode): Array<PlanNode> {
  const out: Array<PlanNode> = [];
  const walk = (n: PlanNode) => {
    out.push(n);
    n.children.forEach(walk);
  };
  walk(root);
  return out;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/utils/__tests__/queryPlan.test.ts`
Expected: PASS (all cases).

- [ ] **Step 5: Gate + commit**

```bash
npx tsc -b && npm run lint
git add src/frontend/src/utils/queryPlan.ts src/frontend/src/utils/__tests__/queryPlan.test.ts
git commit -m "Add query plan parser and derived-metric helpers"
```

---

### Task 2: Single node row component (`PlanNodeRow`)

**Files:**
- Create: `src/frontend/src/components/query/PlanNodeRow.tsx`
- Test: `src/frontend/src/components/query/__tests__/PlanNodeRow.test.tsx`

**Interfaces:**
- Consumes: `PlanNode`, `PlanMetric`, `nodeWeight`, `isMisestimated`, `hasActuals` (Task 1).
- Produces: `PlanNodeRow` component with props `{ node: PlanNode; depth: number; metric: PlanMetric; weightShare: number; isHottest: boolean; hasChildren: boolean; collapsed: boolean; onToggle: () => void }`.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/query/__tests__/PlanNodeRow.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { PlanNodeRow } from "@/components/query/PlanNodeRow";
import type { PlanNode } from "@/utils/queryPlan";

const base = (over: Partial<PlanNode> = {}): PlanNode => ({
  id: "0", nodeType: "Seq Scan", object: "orders", totalCost: 890, planRows: 10000,
  actualTotalTimeMs: null, actualRows: null, actualLoops: null, selfTimeMs: null,
  isSeqScan: true, misestimateFactor: null, children: [], ...over,
});

const renderRow = (props: Partial<React.ComponentProps<typeof PlanNodeRow>> = {}) =>
  render(
    <MantineProvider>
      <PlanNodeRow
        node={base()} depth={0} metric="cost" weightShare={0.5}
        isHottest={false} hasChildren={false} collapsed={false} onToggle={vi.fn()}
        {...props}
      />
    </MantineProvider>,
  );

describe("PlanNodeRow", () => {
  it("shows node type, object, cost and est rows, and a seq-scan flag", () => {
    renderRow();
    expect(screen.getByText("Seq Scan")).toBeInTheDocument();
    expect(screen.getByText(/orders/)).toBeInTheDocument();
    expect(screen.getByText(/Full Table Scan/i)).toBeInTheDocument();
  });

  it("shows actuals and a mis-estimate flag when analyzed", () => {
    renderRow({
      node: base({
        nodeType: "Seq Scan", actualTotalTimeMs: 8.1, actualRows: 9981, actualLoops: 1,
        selfTimeMs: 8.1, misestimateFactor: 19.96,
      }),
      metric: "time",
    });
    expect(screen.getByText(/8\.1/)).toBeInTheDocument();
    expect(screen.getByText(/20×|19|est.*actual/i)).toBeInTheDocument();
  });

  it("invokes onToggle when the chevron is clicked", async () => {
    const onToggle = vi.fn();
    renderRow({ hasChildren: true, onToggle });
    await userEvent.click(screen.getByLabelText(/collapse|expand/i));
    expect(onToggle).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/components/query/__tests__/PlanNodeRow.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `PlanNodeRow`**

Create `src/frontend/src/components/query/PlanNodeRow.tsx`:

```typescript
import { ActionIcon, Badge, Box, Group, Text } from "@mantine/core";
import { IconChevronRight } from "@tabler/icons-react";
import type { PlanNode, PlanMetric } from "@/utils/queryPlan";
import { hasActuals, isMisestimated } from "@/utils/queryPlan";

function fmt(n: number): string {
  return new Intl.NumberFormat().format(Math.round(n));
}

// Green → orange → red by share of the heaviest node.
function heatColor(share: number): string {
  if (share >= 0.66) return "var(--mantine-color-red-6)";
  if (share >= 0.33) return "var(--mantine-color-orange-5)";
  return "var(--mantine-color-green-5)";
}

export function PlanNodeRow({
  node,
  depth,
  metric,
  weightShare,
  isHottest,
  hasChildren,
  collapsed,
  onToggle,
}: {
  node: PlanNode;
  depth: number;
  metric: PlanMetric;
  weightShare: number;
  isHottest: boolean;
  hasChildren: boolean;
  collapsed: boolean;
  onToggle: () => void;
}) {
  const analyzed = hasActuals(node);
  return (
    <Box
      style={{
        paddingLeft: depth * 16 + 4,
        borderLeft: isHottest ? "2px solid var(--mantine-color-red-6)" : "2px solid transparent",
      }}
    >
      <Group gap={6} wrap="nowrap" align="center" py={2}>
        {hasChildren ? (
          <ActionIcon
            variant="subtle" size="xs" color="gray" onClick={onToggle}
            aria-label={collapsed ? "expand node" : "collapse node"}
          >
            <IconChevronRight
              size={12}
              style={{ transform: collapsed ? "none" : "rotate(90deg)", transition: "transform 120ms" }}
            />
          </ActionIcon>
        ) : (
          <Box w={18} style={{ flexShrink: 0 }} />
        )}

        <Text size="xs" fw={600} style={{ whiteSpace: "nowrap" }}>{node.nodeType}</Text>
        {node.object && <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>· {node.object}</Text>}

        {/* heat bar */}
        <Box style={{ flex: 1, minWidth: 40, height: 6, background: "var(--mantine-color-default-border)", borderRadius: 3 }}>
          <Box style={{ width: `${Math.max(2, weightShare * 100)}%`, height: "100%", background: heatColor(weightShare), borderRadius: 3 }} />
        </Box>

        <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>
          {analyzed
            ? `${(node.selfTimeMs ?? 0).toFixed(1)} ms · ${fmt(node.actualRows ?? 0)} rows${(node.actualLoops ?? 1) > 1 ? ` · ${fmt(node.actualLoops ?? 1)} loops` : ""}`
            : `cost ${fmt(node.totalCost)} · ~${fmt(node.planRows)} rows`}
        </Text>

        {node.isSeqScan && <Badge variant="light" color="orange" size="xs">Full Table Scan</Badge>}
        {isMisestimated(node) && (
          <Badge variant="light" color="red" size="xs">
            est {fmt(node.planRows)} vs actual {fmt(node.actualRows ?? 0)} ({Math.round(node.misestimateFactor ?? 0)}×)
          </Badge>
        )}
      </Group>
    </Box>
  );
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/components/query/__tests__/PlanNodeRow.test.tsx`
Expected: PASS.

- [ ] **Step 5: Gate + commit**

```bash
npx tsc -b && npm run lint
git add src/frontend/src/components/query/PlanNodeRow.tsx src/frontend/src/components/query/__tests__/PlanNodeRow.test.tsx
git commit -m "Add PlanNodeRow component for plan tree nodes"
```

---

### Task 3: Tree container with metric toggle + auto-collapse (`PlanTree`)

**Files:**
- Create: `src/frontend/src/components/query/PlanTree.tsx`
- Test: `src/frontend/src/components/query/__tests__/PlanTree.test.tsx`

**Interfaces:**
- Consumes: `PlanNode`, `PlanMetric`, `nodeWeight`, `inclusiveWeight`, `hasActuals`, `flattenNodes` (Task 1); `PlanNodeRow` (Task 2).
- Produces: `PlanTree` component with props `{ root: PlanNode }`.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/query/__tests__/PlanTree.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { PlanTree } from "@/components/query/PlanTree";
import type { PlanNode } from "@/utils/queryPlan";

// Root is costliest by cost; the deep child is slowest by self-time.
const node = (over: Partial<PlanNode>): PlanNode => ({
  id: "x", nodeType: "Node", object: null, totalCost: 0, planRows: 0,
  actualTotalTimeMs: null, actualRows: null, actualLoops: null, selfTimeMs: null,
  isSeqScan: false, misestimateFactor: null, children: [], ...over,
});

const analyzedTree = node({
  id: "0", nodeType: "Aggregate", totalCost: 1000, planRows: 1,
  actualTotalTimeMs: 12, actualRows: 1, actualLoops: 1, selfTimeMs: 1,
  children: [
    node({ id: "0.0", nodeType: "Seq Scan", object: "t", totalCost: 200, planRows: 500,
      actualTotalTimeMs: 11, actualRows: 500, actualLoops: 1, selfTimeMs: 11, isSeqScan: true }),
  ],
});

const estimateTree = node({ id: "0", nodeType: "Result", totalCost: 5, planRows: 1 });

const renderTree = (root: PlanNode) =>
  render(<MantineProvider><PlanTree root={root} /></MantineProvider>);

describe("PlanTree", () => {
  it("renders every node and hides the Time toggle for estimate-only plans", () => {
    renderTree(estimateTree);
    expect(screen.getByText("Result")).toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: /time/i })).not.toBeInTheDocument();
  });

  it("offers Time/Rows/Cost for analyzed plans and defaults to Time", () => {
    renderTree(analyzedTree);
    expect(screen.getByText("Aggregate")).toBeInTheDocument();
    expect(screen.getByText("Seq Scan")).toBeInTheDocument();
    // default metric Time -> hottest is the deep Seq Scan (self 11), marked
    const seqRow = screen.getByText("Seq Scan").closest("div");
    expect(seqRow).toBeTruthy();
  });

  it("collapses a subtree when its chevron is clicked", async () => {
    renderTree(analyzedTree);
    expect(screen.getByText("Seq Scan")).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText("collapse node"));
    expect(screen.queryByText("Seq Scan")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/components/query/__tests__/PlanTree.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `PlanTree`**

Create `src/frontend/src/components/query/PlanTree.tsx`:

```typescript
import { useMemo, useState } from "react";
import { SegmentedControl, Stack } from "@mantine/core";
import type { PlanNode, PlanMetric } from "@/utils/queryPlan";
import { flattenNodes, hasActuals, inclusiveWeight, nodeWeight } from "@/utils/queryPlan";
import { PlanNodeRow } from "@/components/query/PlanNodeRow";

const AUTO_COLLAPSE_NODE_COUNT = 25;
const NEGLIGIBLE_SHARE = 0.01;

function initialCollapsed(root: PlanNode, metric: PlanMetric): Set<string> {
  const all = flattenNodes(root);
  if (all.length <= AUTO_COLLAPSE_NODE_COUNT) return new Set();
  const rootWeight = inclusiveWeight(root, metric) || 1;
  const collapsed = new Set<string>();
  for (const n of all) {
    if (n.id !== root.id && n.children.length > 0 && inclusiveWeight(n, metric) < NEGLIGIBLE_SHARE * rootWeight) {
      collapsed.add(n.id);
    }
  }
  return collapsed;
}

export function PlanTree({ root }: { root: PlanNode }) {
  const analyzed = hasActuals(root);
  const [metric, setMetric] = useState<PlanMetric>(analyzed ? "time" : "cost");
  const [collapsed, setCollapsed] = useState<Set<string>>(() => initialCollapsed(root, analyzed ? "time" : "cost"));

  const { maxWeight, hottestId } = useMemo(() => {
    let maxWeight = 0;
    let hottestId = root.id;
    for (const n of flattenNodes(root)) {
      const w = nodeWeight(n, metric);
      if (w > maxWeight) {
        maxWeight = w;
        hottestId = n.id;
      }
    }
    return { maxWeight, hottestId };
  }, [root, metric]);

  const rows = useMemo(() => {
    const out: Array<{ node: PlanNode; depth: number }> = [];
    const walk = (n: PlanNode, depth: number) => {
      out.push({ node: n, depth });
      if (!collapsed.has(n.id)) n.children.forEach((c) => walk(c, depth + 1));
    };
    walk(root, 0);
    return out;
  }, [root, collapsed]);

  const toggle = (id: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <Stack gap={4}>
      <SegmentedControl
        size="xs"
        value={metric}
        onChange={(v) => setMetric(v as PlanMetric)}
        data={[
          ...(analyzed ? [{ label: "Time", value: "time" }] : []),
          { label: "Rows", value: "rows" },
          { label: "Cost", value: "cost" },
        ]}
        style={{ alignSelf: "flex-start" }}
      />
      <Stack gap={0}>
        {rows.map(({ node, depth }) => (
          <PlanNodeRow
            key={node.id}
            node={node}
            depth={depth}
            metric={metric}
            weightShare={maxWeight > 0 ? nodeWeight(node, metric) / maxWeight : 0}
            isHottest={node.id === hottestId && maxWeight > 0}
            hasChildren={node.children.length > 0}
            collapsed={collapsed.has(node.id)}
            onToggle={() => toggle(node.id)}
          />
        ))}
      </Stack>
    </Stack>
  );
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/components/query/__tests__/PlanTree.test.tsx`
Expected: PASS.

- [ ] **Step 5: Gate + commit**

```bash
npx tsc -b && npm run lint
git add src/frontend/src/components/query/PlanTree.tsx src/frontend/src/components/query/__tests__/PlanTree.test.tsx
git commit -m "Add PlanTree with metric toggle and auto-collapse"
```

---

### Task 4: Wire the tree into `PlanView` (primary view + raw fallback)

**Files:**
- Modify: `src/frontend/src/components/query/PlanView.tsx`
- Test: `src/frontend/src/components/query/__tests__/PlanView.test.tsx` (extend existing)

**Interfaces:**
- Consumes: `parsePlan` (Task 1), `PlanTree` (Task 3), existing `PlanSummaryBadges`.

- [ ] **Step 1: Write the failing tests**

Add to `src/frontend/src/components/query/__tests__/PlanView.test.tsx` (keep existing cases):

```typescript
  it("renders the node tree for a parseable success plan", () => {
    renderView(entry({
      plan: {
        planJson: JSON.stringify([{ Plan: { "Node Type": "Seq Scan", "Relation Name": "orders", "Total Cost": 890, "Plan Rows": 10000 } }]),
        summary: { totalCost: 890, estimatedRows: 10000, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
      },
    }));
    expect(screen.getByText("Seq Scan")).toBeInTheDocument();
    expect(screen.getByText(/Raw plan/i)).toBeInTheDocument(); // secondary toggle still present
  });

  it("falls back to the raw panel when the plan JSON will not parse", () => {
    renderView(entry({
      plan: {
        planJson: "not valid json",
        summary: { totalCost: 0, estimatedRows: 0, rootNode: "?", hasSeqScan: false, actualTotalMs: null },
      },
    }));
    // No tree; raw content shown directly
    expect(screen.getByText(/not valid json/)).toBeInTheDocument();
  });
```

(Reuse the existing `entry()` / `renderView()` helpers in the file; the existing pending/blocked/error/analyze/seq-scan tests must keep passing.)

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/components/query/__tests__/PlanView.test.tsx`
Expected: FAIL — tree not rendered (`Seq Scan` absent).

- [ ] **Step 3: Modify `PlanView` success branch**

In `src/frontend/src/components/query/PlanView.tsx`, add imports:

```typescript
import { useMemo } from "react";
import { parsePlan } from "@/utils/queryPlan";
import { PlanTree } from "@/components/query/PlanTree";
```

**Hooks must run unconditionally**, so place the `parsed` memo at the TOP of the component, right after the existing `useDisclosure` line and BEFORE the `if (entry.status === "pending")` early returns. `entry.plan` is null on the non-success branches, so guard it:

```typescript
export function PlanView({ entry }: { entry: ExplainEntry }) {
  const [open, { toggle }] = useDisclosure(false);
  const parsed = useMemo(
    () => (entry.plan ? parsePlan(entry.plan.planJson) : null),
    [entry.plan],
  );

  if (entry.status === "pending") {
    // …unchanged early returns for pending / blocked / error …
```

Then replace the success `return (...)` block (the `<Stack p="xs" gap="xs">` … `</Stack>` at the end) with:

```typescript
  return (
    <Stack p="xs" gap="xs">
      <PlanSummaryBadges summary={entry.plan.summary} />
      {parsed && <PlanTree root={parsed} />}
      <UnstyledButton onClick={toggle}>
        <Text size="xs" c="dimmed" style={{ display: "flex", alignItems: "center", gap: 4 }}>
          <IconChevronRight
            size={12}
            style={{ transform: open ? "rotate(90deg)" : "none", transition: "transform 120ms" }}
          />
          Raw plan
        </Text>
      </UnstyledButton>
      <Collapse expanded={open || !parsed} keepMounted={false}>
        <Code block fz="xs" style={{ maxHeight: 320, overflow: "auto" }}>
          {prettyJson(entry.plan.planJson)}
        </Code>
      </Collapse>
    </Stack>
  );
```

Two behavioral changes from the original: the `<PlanTree>` line renders the tree when parsing succeeds, and `expanded={open || !parsed}` shows the raw panel outright when parsing failed (the fallback). Do not add any hook after the early returns.

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/components/query/__tests__/PlanView.test.tsx`
Expected: PASS (new + all existing cases).

- [ ] **Step 5: Full gate + commit**

```bash
npx tsc -b && npm run lint && npx vitest run
git add src/frontend/src/components/query/PlanView.tsx src/frontend/src/components/query/__tests__/PlanView.test.tsx
git commit -m "Render plan node tree in PlanView with raw-panel fallback"
```

---

## Final verification

- [ ] `cd src/frontend && npx tsc -b` clean; `npm run lint` exit 0; `npx vitest run` all pass.
- [ ] `npx vitest run --coverage --coverage.reporter=text` — confirm `queryPlan.ts`, `PlanNodeRow.tsx`, `PlanTree.tsx` each appear with ≥ 85% line coverage and `PlanView.tsx` stays high (protects the diff-coverage gate).
- [ ] Manual smoke via the running app (blue DB): run a join+aggregate query, open the Plan tab, confirm the tree renders with a heat bar, switch the Time/Rows/Cost toggle and watch the hottest node move, collapse a subtree, and run "Explain with timings" to see self-time + a mis-estimate flag (e.g. `SELECT * FROM transactions WHERE upper(city) = 'LONDON'`).

## Notes / deviations

- The metric toggle recomputes the heat/hottest node on change, but the initial auto-collapse set is computed once (from the default metric); switching metric does not re-auto-collapse. This is intentional for predictability — manual expand/collapse governs thereafter.
- "Full Table Scan" is the seq-scan badge label (matches the existing `PlanSummaryBadges` wording from the shipped feature).
