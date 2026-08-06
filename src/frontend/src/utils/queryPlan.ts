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
  try {
    const doc: unknown = JSON.parse(planJson);
    const first = Array.isArray(doc) ? doc[0] : undefined;
    const root = (first as { Plan?: RawPlan } | undefined)?.Plan;
    if (!root || typeof root !== "object" || typeof root["Node Type"] !== "string") {
      return null;
    }
    return buildNode(root, "0");
  } catch {
    return null;
  }
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
