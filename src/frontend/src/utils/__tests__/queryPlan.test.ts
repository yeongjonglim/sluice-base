import { describe, expect, it } from "vitest";

import type { PlanNode } from "@/utils/queryPlan";
import {
  computeSelfTimeMs,
  flattenNodes,
  hasActuals,
  isMisestimated,
  misestimateFactor,
  nodeWeight,
  parsePlan,
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
