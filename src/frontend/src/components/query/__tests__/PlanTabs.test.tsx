import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { PlanTabs } from "@/components/query/PlanTabs";

function entry(partial: Partial<ExplainEntry> & Pick<ExplainEntry, "id" | "index" | "text">): ExplainEntry {
  return {
    fromPos: 0, toPos: 8, fromLine: 1, toLine: 1, analyze: false,
    status: "success",
    plan: {
      planJson: '[{"Plan":{"Node Type":"Seq Scan"}}]',
      summary: { totalCost: 1, estimatedRows: 1, rootNode: "Seq Scan", hasSeqScan: false, actualTotalMs: null },
    },
    error: null,
    ...partial,
  };
}

function renderTabs(runs: Array<ExplainEntry>, onHighlight = vi.fn()) {
  render(
    <MantineProvider>
      <PlanTabs runs={runs} onHighlight={onHighlight} />
    </MantineProvider>,
  );
  return onHighlight;
}

describe("PlanTabs", () => {
  it("shows a placeholder when there are no runs", () => {
    renderTabs([]);
    expect(screen.getByText(/Explain a query to see its plan/)).toBeInTheDocument();
  });

  it("renders a tab per run with a statement snippet", () => {
    renderTabs([
      entry({ id: "1-0", index: 0, text: "SELECT a FROM t1" }),
      entry({ id: "1-1", index: 1, text: "SELECT b FROM t2" }),
    ]);
    expect(screen.getByText(/SELECT a FROM t1/)).toBeInTheDocument();
    expect(screen.getByText(/SELECT b FROM t2/)).toBeInTheDocument();
  });

  it("calls onHighlight with the entry when its tab is clicked", async () => {
    const onHighlight = renderTabs([
      entry({ id: "1-0", index: 0, text: "SELECT a" }),
      entry({ id: "1-1", index: 1, text: "SELECT b" }),
    ]);
    await userEvent.click(screen.getByText(/SELECT b/));
    expect(onHighlight).toHaveBeenCalledWith(expect.objectContaining({ id: "1-1" }));
  });

  it("renders the active entry's plan below the tabs", () => {
    renderTabs([
      entry({
        id: "1-0", index: 0, text: "SELECT a",
        plan: {
          planJson: "[{}]",
          summary: { totalCost: 5, estimatedRows: 20, rootNode: "Index Scan", hasSeqScan: false, actualTotalMs: null },
        },
      }),
    ]);
    expect(screen.getByText("Index Scan")).toBeInTheDocument();
  });
});
