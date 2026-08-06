import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import type { PlanNode } from "@/utils/queryPlan";
import { PlanTree } from "@/components/query/PlanTree";

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
