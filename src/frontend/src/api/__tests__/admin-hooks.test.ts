import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import {
  useAuthedHealth,
  useDisableUser,
  useEnableUser,
  useGrantPermission,
  useMe,
  usePermissionCatalog,
  useRevokePermission,
  useRevokeUserMcpGrants,
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

const { apiRequest, ApiError } = await import("@/api/client");
const { notifications } = await import("@mantine/notifications");

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

describe.each([
  ["useDisableUser", useDisableUser, "/api/admin/user/user-2/disable", "POST", "User disabled"],
  ["useEnableUser", useEnableUser, "/api/admin/user/user-2/enable", "POST", "User enabled"],
  ["useRevokeUserMcpGrants", useRevokeUserMcpGrants, "/api/admin/user/user-2/mcp-grant", "DELETE", "MCP access revoked"],
] as const)("%s", (_name, useHook, path, method, successTitle) => {
  it(`sends ${method} ${path} and reports success`, async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useHook(), { wrapper });
    result.current.mutate("user-2");
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(path, { method });
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ title: successTitle }));
  });

  it("shows the API's validation message on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(
      new ApiError(400, { errors: { userId: ["You cannot disable your own account."] } }),
    );
    const { result } = renderHook(() => useHook(), { wrapper });
    result.current.mutate("user-2");
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ message: "You cannot disable your own account.", color: "red" }),
    );
  });
});

