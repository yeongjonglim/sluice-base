import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useExecuteQuery, useSchema } from "@/api/hooks";

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

describe("useExecuteQuery", () => {
  it("posts to /api/query with serverId and sql", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      columns: ["id", "email"],
      rows: [["1", "alice@example.com"]],
      rowCount: 1,
      durationMs: 42,
      error: null,
    });

    const { result } = renderHook(() => useExecuteQuery(), { wrapper });

    result.current.mutate({ databaseId: "database-uuid", sql: "SELECT id, email FROM users LIMIT 1" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(apiRequest).toHaveBeenCalledWith(
      "/api/query",
      expect.objectContaining({
        method: "POST",
        body: { databaseId: "database-uuid", sql: "SELECT id, email FROM users LIMIT 1" },
      }),
    );
    expect(result.current.data?.columns).toEqual(["id", "email"]);
    expect(result.current.data?.rowCount).toBe(1);
  });

  it("exposes error field from response when query fails", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      columns: null,
      rows: null,
      rowCount: 0,
      durationMs: 10,
      error: 'column "naem" does not exist',
    });

    const { result } = renderHook(() => useExecuteQuery(), { wrapper });

    result.current.mutate({ databaseId: "database-uuid", sql: "SELECT naem FROM users" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.error).toBe('column "naem" does not exist');
    expect(result.current.data?.columns).toBeNull();
  });
});
