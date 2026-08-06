import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiClientModule from "@/api/client";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import { useExplainRuns } from "@/api/useExplainRuns";

vi.mock("@/api/client", async () => {
  const actual = await vi.importActual<typeof ApiClientModule>("@/api/client");
  return { ...actual, apiRequest: vi.fn() };
});

const { apiRequest } = await import("@/api/client");

const mockApiRequest = vi.mocked(apiRequest);

function stmt(text: string): SqlStatement {
  return { text, fromPos: 0, toPos: text.length, fromLine: 1, toLine: 1 };
}

beforeEach(() => mockApiRequest.mockReset());

// Blocked-classification is not re-tested through the hook here: useExplainRuns
// reuses isBlocked() unchanged from useQueryRuns, and that function's behavior
// (403 + sensitive_columns body vs. any other failure) is already fully covered
// in useQueryRuns.test.ts. Driving a rejection through this hook's fire-and-forget
// runLimited worker a second time in the same file is not reliably observable in
// jsdom (see the equivalent comment in useQueryRuns.test.ts).
describe("useExplainRuns", () => {
  it("posts to /api/query/explain with the analyze flag and records the plan", async () => {
    mockApiRequest.mockResolvedValue({
      planJson: "[{}]",
      summary: { totalCost: 1, estimatedRows: 2, rootNode: "Seq Scan", hasSeqScan: true, actualTotalMs: null },
    });

    const { result } = renderHook(() => useExplainRuns());
    act(() => result.current.run("db-1", [stmt("SELECT 1")], true));

    await waitFor(() => expect(result.current.runs[0].status).toBe("success"));
    expect(result.current.runs[0].plan?.summary.rootNode).toBe("Seq Scan");
    expect(mockApiRequest).toHaveBeenCalledWith("/api/query/explain", {
      method: "POST",
      body: { databaseId: "db-1", sql: "SELECT 1", analyze: true },
    });
  });
});
