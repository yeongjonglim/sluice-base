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

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
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
  it("is disabled and does not fetch when serverId is null", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ schemas: [] });

    const { result } = renderHook(() => useSchema(null), { wrapper });
    await new Promise((r) => setTimeout(r, 50));

    expect(apiRequest).not.toHaveBeenCalled();
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("fetches /api/schema/{serverId} and uses correct query key", async () => {
    const mockTree = {
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "users",
              columns: [{ name: "id", dataType: "integer", isNullable: false }],
            },
          ],
        },
      ],
    };
    vi.mocked(apiRequest).mockResolvedValue(mockTree);

    const { result } = renderHook(() => useSchema("server-abc"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(apiRequest).toHaveBeenCalledWith("/api/schema/server-abc");
    expect(result.current.data).toEqual(mockTree);
  });
});
