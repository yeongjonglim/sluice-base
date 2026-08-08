import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import type { RunEntry } from "@/api/useQueryRuns";
import { ResultGrid } from "@/components/query/ResultGrid";
import { ApiError } from "@/api/client";

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

  it("shows the restricted columns for a blocked query", () => {
    const error = new ApiError(403, {
      type: "sensitive_columns",
      columns: [{ schema: "public", table: "users", column: "ssn" }],
    });
    renderGrid({ ...base(), status: "blocked", error });
    expect(screen.getByText(/restricted columns/i)).toBeInTheDocument();
    expect(screen.getByText("public.users.ssn")).toBeInTheDocument();
  });

  it("shows the policy-block reason for a denylisted-function block", () => {
    const error = new ApiError(403, {
      type: "sensitive_columns",
      columns: [],
      reason: "Query uses query_to_xml(), which can bypass column-level access checks.",
    });
    renderGrid({ ...base(), status: "blocked", error });
    expect(
      screen.getByText(/Query uses query_to_xml\(\), which can bypass column-level access checks\./),
    ).toBeInTheDocument();
  });

  it("shows an advisory estimate strip on a successful run", () => {
    const entry = {
      id: "1-0", index: 0, text: "SELECT 1",
      fromPos: 0, toPos: 8, fromLine: 1, toLine: 1,
      status: "success" as const,
      response: {
        columns: ["?column?"], rows: [["1"]], rowCount: 1, durationMs: 2, error: null,
        estimate: { totalCost: 15, estimatedRows: 500, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
      },
      error: null,
    };
    renderGrid(entry); // use the test file's existing render helper
    expect(screen.getByText(/planner estimate/i)).toBeInTheDocument();
    expect(screen.getByText(/~500 rows/i)).toBeInTheDocument();
  });
});
