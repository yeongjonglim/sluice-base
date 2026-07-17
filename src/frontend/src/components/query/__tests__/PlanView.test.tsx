import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { PlanView } from "@/components/query/PlanView";

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
});
