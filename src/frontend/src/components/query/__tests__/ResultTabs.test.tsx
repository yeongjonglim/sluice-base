import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import type { RunEntry } from "@/api/useQueryRuns";
import { ResultTabs } from "@/components/query/ResultTabs";

function entry(partial: Partial<RunEntry> & Pick<RunEntry, "id" | "index" | "text">): RunEntry {
  return {
    fromPos: 0, toPos: 8, fromLine: 1, toLine: 1,
    status: "success",
    response: { columns: ["n"], rows: [["1"]], rowCount: 1, durationMs: 1, error: null, estimate: null },
    error: null,
    ...partial,
  };
}

function renderTabs(runs: Array<RunEntry>, onHighlight = vi.fn()) {
  render(
    <MantineProvider>
      <ResultTabs runs={runs} onHighlight={onHighlight} />
    </MantineProvider>,
  );
  return onHighlight;
}

describe("ResultTabs", () => {
  it("shows a placeholder when there are no runs", () => {
    renderTabs([]);
    expect(screen.getByText(/Run a query to see results/)).toBeInTheDocument();
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

  it("falls back to the first tab when a new run batch replaces the tabs", async () => {
    const grid = (col: string): RunEntry["response"] => ({
      columns: [col], rows: [["x"]], rowCount: 1, durationMs: 1, error: null, estimate: null,
    });
    const batch1 = [
      entry({ id: "1-0", index: 0, text: "SELECT a", response: grid("colA") }),
      entry({ id: "1-1", index: 1, text: "SELECT b", response: grid("colB") }),
    ];
    const { rerender } = render(
      <MantineProvider>
        <ResultTabs runs={batch1} onHighlight={vi.fn()} />
      </MantineProvider>,
    );

    // The first tab's grid is active on mount.
    expect(screen.getByText("colA")).toBeInTheDocument();

    // Activate the second tab — its grid replaces the first.
    await userEvent.click(screen.getByText(/SELECT b/));
    expect(screen.getByText("colB")).toBeInTheDocument();

    // A new batch (fresh ids) arrives; the previously-active id no longer
    // matches, so the active tab falls back to the first entry of the new batch.
    const batch2 = [
      entry({ id: "2-0", index: 0, text: "SELECT c", response: grid("colC") }),
      entry({ id: "2-1", index: 1, text: "SELECT d", response: grid("colD") }),
    ];
    rerender(
      <MantineProvider>
        <ResultTabs runs={batch2} onHighlight={vi.fn()} />
      </MantineProvider>,
    );

    expect(screen.getByText("colC")).toBeInTheDocument();
    expect(screen.queryByText("colB")).not.toBeInTheDocument();
    expect(screen.queryByText("colD")).not.toBeInTheDocument();
  });
});
