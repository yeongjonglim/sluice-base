import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import type { RunEntry } from "@/api/useQueryRuns";
import { ResultGrid } from "@/components/query/ResultGrid";

function base(): RunEntry {
  return {
    id: "1-0", index: 0, text: "SELECT 1", fromPos: 0, toPos: 8,
    fromLine: 1, toLine: 1, status: "success", response: null, error: null,
  };
}

function renderGrid(entry: RunEntry) {
  return render(
    <MantineProvider>
      <ResultGrid entry={entry} />
    </MantineProvider>,
  );
}

describe("ResultGrid", () => {
  it("delegates a successful result to the virtualized table (headers + filter)", () => {
    renderGrid({
      ...base(),
      status: "success",
      response: { columns: ["id", "name"], rows: [["1", "Ada"]], rowCount: 1, durationMs: 5, error: null, estimate: null },
    });
    // Column headers and the stats render outside the virtualized body.
    expect(screen.getByText("id")).toBeInTheDocument();
    expect(screen.getByText(/1 row/)).toBeInTheDocument();
    expect(screen.getByLabelText("Filter rows")).toBeInTheDocument();
  });

  it("renders a query error alert", () => {
    renderGrid({
      ...base(),
      status: "error",
      response: { columns: null, rows: null, rowCount: 0, durationMs: 2, error: "boom", estimate: null },
    });
    expect(screen.getByText("boom")).toBeInTheDocument();
  });
});
