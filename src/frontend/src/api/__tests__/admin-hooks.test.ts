import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import {
  useAuthedHealth,
  useGrantPermission,
  useMe,
  usePermissionCatalog,
  useRevokePermission,
  useUsers,
} from "@/api/hooks";

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

describe("useMe", () => {
  it("fetches GET /api/me", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ id: "user-1", name: "Alice", email: "alice@example.com" });
    const { result } = renderHook(() => useMe(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/me");
  });
});

describe("useAuthedHealth", () => {
  it("fetches GET /api/health/authed", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ status: "ok" });
    const { result } = renderHook(() => useAuthedHealth(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/health/authed");
  });
});

describe("useUsers", () => {
  it("fetches GET /api/admin/user", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      users: [{ id: "user-1", name: "Alice", email: "alice@example.com", permissions: [] }],
    });
    const { result } = renderHook(() => useUsers(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/admin/user");
  });
});

describe("usePermissionCatalog", () => {
  it("fetches GET /api/permission/catalog with infinite staleTime", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ permissions: ["query", "update:submit"] });
    const { result } = renderHook(() => usePermissionCatalog(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/permission/catalog");
  });
});

describe("useGrantPermission", () => {
  it("posts to /api/admin/user/{userId}/permission and invalidates ['admin','user']", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useGrantPermission(), { wrapper });
    result.current.mutate({ userId: "user-1", permission: "query" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/user/user-1/permission",
      expect.objectContaining({ method: "POST", body: { permission: "query" } }),
    );
  });
});

describe("useRevokePermission", () => {
  it("deletes /api/admin/user/{userId}/permission/{permission} and invalidates ['admin','user']", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRevokePermission(), { wrapper });
    result.current.mutate({ userId: "user-1", permission: "query" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/user/user-1/permission/query",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});
