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

  it("moves the hottest marker when the metric toggle changes", async () => {
    renderTree(analyzedTree);
    // default metric Time -> hottest is the deep Seq Scan (self-time 11 > root's 1)
    expect(document.querySelector('[data-hottest="true"]')).toHaveTextContent("Seq Scan");

    await userEvent.click(screen.getByText("Cost"));
    // by Cost, root (1000) outweighs the child (200) -> hottest is the root
    expect(document.querySelector('[data-hottest="true"]')).toHaveTextContent("Aggregate");
  });
});

describe("PlanTree auto-collapse", () => {
  // Build a root with ~25 cheap leaf children plus one mid-weight "Cheap" child
  // that itself has a child, so the tree exceeds the 25-node auto-collapse
  // threshold and "Cheap" (< 1% of root weight) collapses by default.
  const buildLeaves = (count: number): Array<PlanNode> =>
    Array.from({ length: count }, (_, i) =>
      node({ id: `leaf-${i}`, nodeType: `Leaf ${i}`, totalCost: 1, planRows: 1 }),
    );

  const bigTree = node({
    id: "0",
    nodeType: "Root",
    totalCost: 100000,
    planRows: 1,
    children: [
      ...buildLeaves(25),
      node({
        id: "cheap",
        nodeType: "Cheap",
        totalCost: 10, // < 1% of root's 100000
        planRows: 1,
        children: [node({ id: "cheap.0", nodeType: "Hidden Leaf", totalCost: 1, planRows: 1 })],
      }),
    ],
  });

  it("auto-collapses cheap subtrees in large plans, expandable on demand", async () => {
    renderTree(bigTree);
    expect(screen.getByText("Cheap")).toBeInTheDocument();
    expect(screen.queryByText("Hidden Leaf")).not.toBeInTheDocument();

    await userEvent.click(screen.getByLabelText("expand node"));
    expect(screen.getByText("Hidden Leaf")).toBeInTheDocument();
  });
});
