import { describe, expect, it } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { PlanView } from "@/components/query/PlanView";
import { ApiError } from "@/api/client";

const entry = (over: Partial<ExplainEntry> = {}): ExplainEntry => ({
  id: "1-0", index: 0, text: "SELECT * FROM users",
  fromPos: 0, toPos: 19, fromLine: 1, toLine: 1, analyze: false,
  status: "success",
  plan: {
    planJson: '[{"Plan":{"Node Type":"Seq Scan"}}]',
    summary: { totalCost: 42, estimatedRows: 1000, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
  },
  error: null,
  ...over,
});

const renderView = (e: ExplainEntry) =>
  render(<MantineProvider><PlanView entry={e} /></MantineProvider>);

describe("PlanView", () => {
  it("shows estimated cost, rows and a seq-scan warning", () => {
    renderView(entry());
    expect(screen.getByText(/1,?000/)).toBeInTheDocument();
    expect(screen.getByText(/42/)).toBeInTheDocument();
    expect(screen.getByText("Full Table Scan")).toBeInTheDocument();
    expect(screen.getByText("Seq Scan")).toBeInTheDocument();
  });

  it("shows actual time when analyzed", () => {
    renderView(entry({
      analyze: true,
      plan: {
        planJson: "[{}]",
        summary: { totalCost: 42, estimatedRows: 10, rootNode: "Index Scan", hasSeqScan: false, actualTotalMs: 3.5 },
      },
    }));
    expect(screen.getByText(/3\.5\s*ms/i)).toBeInTheDocument();
  });

  it("renders the query error for a failed explain", () => {
    renderView(entry({ status: "error", plan: null, error: new Error("boom") }));
    expect(screen.getByText(/could not/i)).toBeInTheDocument();
  });

  it("shows an analyzing placeholder while pending", () => {
    renderView(entry({ status: "pending", plan: null }));
    expect(screen.getByText(/Analyzing…/)).toBeInTheDocument();
  });

  it("shows the restricted columns for a blocked explain", () => {
    const error = new ApiError(403, {
      type: "sensitive_columns",
      columns: [{ schema: "public", table: "users", column: "ssn" }],
    });
    renderView(entry({ status: "blocked", plan: null, error }));
    expect(screen.getByText(/restricted columns/i)).toBeInTheDocument();
    expect(screen.getByText("public.users.ssn")).toBeInTheDocument();
  });

  it("toggles the raw plan JSON open when the button is clicked", async () => {
    renderView(entry());
    expect(screen.queryByText(/"Node Type"/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByText("Raw plan"));
    // The Collapse open animation runs across nested requestAnimationFrame
    // callbacks, so the raw plan mounts asynchronously.
    await waitFor(() => expect(screen.getByText(/"Node Type"/)).toBeInTheDocument());
  });
});
