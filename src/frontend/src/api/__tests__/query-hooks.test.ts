import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useSchema } from "@/api/hooks";

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

describe("useSchema", () => {
  it("fetches GET /api/schema/{databaseId} when databaseId is provided", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ tables: [{ name: "users", columns: [] }] });
    const { result } = renderHook(() => useSchema("db-1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/schema/db-1");
  });

  it("does not fetch when databaseId is null", () => {
    const { result } = renderHook(() => useSchema(null), { wrapper });
    expect(result.current.fetchStatus).toBe("idle");
    expect(apiRequest).not.toHaveBeenCalled();
  });
});
