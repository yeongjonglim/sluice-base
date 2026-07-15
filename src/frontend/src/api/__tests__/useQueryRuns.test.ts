import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiClientModule from "@/api/client";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import { isBlocked, useQueryRuns } from "@/api/useQueryRuns";

vi.mock("@/api/client", async () => {
  const actual = await vi.importActual<typeof ApiClientModule>("@/api/client");
  return { ...actual, apiRequest: vi.fn() };
});

const { apiRequest, ApiError } = await import("@/api/client");

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

// The hook maps a rejected request to a per-entry status via isBlocked(): a 403
// with a sensitive_columns body becomes "blocked", every other failure becomes
// "error". Verified directly here — driving the hook's fire-and-forget rejection
// path through the async worker is not reliably observable in jsdom.
describe("isBlocked", () => {
  it("treats a 403 sensitive_columns error as blocked", () => {
    expect(isBlocked(new ApiError(403, { type: "sensitive_columns", columns: [] }))).toBe(true);
  });

  it("does not treat a 403 without a sensitive_columns body as blocked", () => {
    expect(isBlocked(new ApiError(403, { type: "forbidden" }))).toBe(false);
    expect(isBlocked(new ApiError(403, null))).toBe(false);
  });

  it("does not treat other statuses or non-ApiError failures as blocked", () => {
    expect(isBlocked(new ApiError(500, { type: "sensitive_columns" }))).toBe(false);
    expect(isBlocked(new Error("network"))).toBe(false);
    expect(isBlocked(null)).toBe(false);
  });
});
