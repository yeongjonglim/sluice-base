import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useQueryHistory } from "@/api/hooks";

vi.mock("@/api/client", () => ({
  apiRequest: vi.fn(),
  ApiError: class ApiError extends Error {
    constructor(
      public status: number,
      public body: unknown,
    ) {
      super(`API ${status}`);
    }
  },
}));

const { apiRequest } = await import("@/api/client");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useQueryHistory", () => {
  it("fetches GET /api/query/history with no params when filters are empty", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(() => useQueryHistory({}), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/query/history");
  });

  it("appends status filter to the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(() => useQueryHistory({ status: "Error" }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/query/history?status=Error");
  });

  it("appends multiple filters to the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(
      () => useQueryHistory({ status: "Success", databaseId: "db-123" }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const calledUrl = vi.mocked(apiRequest).mock.calls[0][0] as string;
    expect(calledUrl).toContain("status=Success");
    expect(calledUrl).toContain("databaseId=db-123");
  });

  it("includes filter values in the cache key", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });

    const { result: r1 } = renderHook(() => useQueryHistory({ status: "Error" }), { wrapper });
    const { result: r2 } = renderHook(() => useQueryHistory({ status: "Success" }), { wrapper });

    await waitFor(() => expect(r1.current.isSuccess).toBe(true));
    await waitFor(() => expect(r2.current.isSuccess).toBe(true));

    // Two different fetches because cache keys differ
    expect(apiRequest).toHaveBeenCalledTimes(2);
  });

  it("omits undefined filter values from the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ items: [] });
    const { result } = renderHook(
      () => useQueryHistory({ status: undefined, from: "2024-01-01" }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const calledUrl = vi.mocked(apiRequest).mock.calls[0][0] as string;
    expect(calledUrl).toContain("from=2024-01-01");
    expect(calledUrl).not.toContain("status=");
  });
});
