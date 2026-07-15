import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiClientModule from "@/api/client";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import { useQueryRuns } from "@/api/useQueryRuns";

vi.mock("@/api/client", async () => {
  const actual = await vi.importActual<typeof ApiClientModule>("@/api/client");
  return { ...actual, apiRequest: vi.fn() };
});

const { apiRequest } = await import("@/api/client");

const mockApiRequest = vi.mocked(apiRequest);

function stmt(text: string, fromPos: number): SqlStatement {
  return { text, fromPos, toPos: fromPos + text.length, fromLine: 1, toLine: 1 };
}

beforeEach(() => mockApiRequest.mockReset());

describe("useQueryRuns", () => {
  it("creates one pending entry per statement, then resolves each to success", async () => {
    mockApiRequest.mockResolvedValue({
      columns: ["n"], rows: [["1"]], rowCount: 1, durationMs: 3, error: null,
    });

    const { result } = renderHook(() => useQueryRuns());
    act(() => result.current.run("db-1", [stmt("SELECT 1", 0), stmt("SELECT 2", 10)]));

    expect(result.current.runs).toHaveLength(2);
    await waitFor(() =>
      expect(result.current.runs.every((r) => r.status === "success")).toBe(true),
    );
    expect(mockApiRequest).toHaveBeenCalledTimes(2);
    expect(result.current.isRunning).toBe(false);
  });

  it("marks a query-level error (200 with error text) as error but keeps the response", async () => {
    mockApiRequest.mockResolvedValue({
      columns: null, rows: null, rowCount: 0, durationMs: 2, error: "syntax error",
    });
    const { result } = renderHook(() => useQueryRuns());
    act(() => result.current.run("db-1", [stmt("SELEC 1", 0)]));
    await waitFor(() => expect(result.current.runs[0].status).toBe("error"));
    expect(result.current.runs[0].response?.error).toBe("syntax error");
  });
});
