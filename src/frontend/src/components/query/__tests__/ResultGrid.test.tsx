import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { ResultGrid } from "@/components/query/ResultGrid";
import type { RunEntry } from "@/api/useQueryRuns";

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
  it("renders a table with columns and rows for a successful result", () => {
    renderGrid({
      ...base(),
      status: "success",
      response: { columns: ["id", "name"], rows: [["1", "Ada"]], rowCount: 1, durationMs: 5, error: null },
    });
    expect(screen.getByText("id")).toBeInTheDocument();
    expect(screen.getByText("Ada")).toBeInTheDocument();
    expect(screen.getByText(/1 row/)).toBeInTheDocument();
  });

  it("renders a query error alert", () => {
    renderGrid({
      ...base(),
      status: "error",
      response: { columns: null, rows: null, rowCount: 0, durationMs: 2, error: "boom" },
    });
    expect(screen.getByText("boom")).toBeInTheDocument();
  });
});
