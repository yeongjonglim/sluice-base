import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import {
  useAddGroupMember,
  useCreateGroup,
  useDeleteGroup,
  useGrantGroupPermission,
  useGroupMembers,
  useGroupPermissions,
  useGroups,
  useRemoveGroupDatabaseRole,
  useRemoveGroupMember,
  useRevokeGroupColumnBypass,
  useRevokeGroupPermission,
  useUpdateGroup,
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
const { notifications } = await import("@mantine/notifications");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => vi.clearAllMocks());

describe("useGroups", () => {
  it("fetches GET /api/admin/group", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ groups: [] });
    const { result } = renderHook(() => useGroups(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/admin/group");
  });
});

describe("useGroupMembers", () => {
  it("fetches members for a group", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ members: [] });
    const { result } = renderHook(() => useGroupMembers("g-1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/admin/group/g-1/member");
  });

  it("is disabled when groupId is null", () => {
    const { result } = renderHook(() => useGroupMembers(null), { wrapper });
    expect(result.current.fetchStatus).toBe("idle");
  });
});

describe("useGroupPermissions", () => {
  it("fetches permissions for a group", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ permissions: [] });
    const { result } = renderHook(() => useGroupPermissions("g-1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/admin/group/g-1/permission");
  });

  it("is disabled when groupId is null", () => {
    const { result } = renderHook(() => useGroupPermissions(null), { wrapper });
    expect(result.current.fetchStatus).toBe("idle");
  });
});

describe("useCreateGroup", () => {
  it("posts to /api/admin/group", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ id: "g-1", name: "Admins" });
    const { result } = renderHook(() => useCreateGroup(), { wrapper });
    result.current.mutate({ name: "Admins", description: "Admin group" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group",
      expect.objectContaining({ method: "POST", body: { name: "Admins", description: "Admin group" } }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useCreateGroup(), { wrapper });
    result.current.mutate({ name: "Admins" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useUpdateGroup", () => {
  it("puts to /api/admin/group/{groupId}", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ id: "g-1", name: "Updated" });
    const { result } = renderHook(() => useUpdateGroup(), { wrapper });
    result.current.mutate({ groupId: "g-1", name: "Updated" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g-1",
      expect.objectContaining({ method: "PUT", body: { name: "Updated", description: undefined } }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useUpdateGroup(), { wrapper });
    result.current.mutate({ groupId: "g-1", name: "X" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useDeleteGroup", () => {
  it("deletes /api/admin/group/{groupId}", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useDeleteGroup(), { wrapper });
    result.current.mutate({ groupId: "g-1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useDeleteGroup(), { wrapper });
    result.current.mutate({ groupId: "g-1" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useAddGroupMember", () => {
  it("posts to /api/admin/group/{groupId}/member", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAddGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g-1", userId: "u-1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g-1/member",
      expect.objectContaining({ method: "POST", body: { userId: "u-1" } }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useAddGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g-1", userId: "u-1" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useRemoveGroupMember", () => {
  it("deletes /api/admin/group/{groupId}/member/{userId}", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g-1", userId: "u-1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g-1/member/u-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useRemoveGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g-1", userId: "u-1" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useGrantGroupPermission", () => {
  it("posts to /api/admin/group/{groupId}/permission", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useGrantGroupPermission(), { wrapper });
    result.current.mutate({ groupId: "g-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g-1/permission",
      expect.objectContaining({ method: "POST", body: { permission: "query:execute" } }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useGrantGroupPermission(), { wrapper });
    result.current.mutate({ groupId: "g-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useRevokeGroupPermission", () => {
  it("deletes /api/admin/group/{groupId}/permission/{permission}", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRevokeGroupPermission(), { wrapper });
    result.current.mutate({ groupId: "g-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g-1/permission/query:execute",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useRevokeGroupPermission(), { wrapper });
    result.current.mutate({ groupId: "g-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useRemoveGroupDatabaseRole", () => {
  it("deletes /api/admin/database/{databaseId}/role/group/{groupId}/{permission}", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveGroupDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", groupId: "g-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/database/db-1/role/group/g-1/query:execute",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("shows error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useRemoveGroupDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", groupId: "g-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useRevokeGroupColumnBypass", () => {
  it("deletes the bypass endpoint", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRevokeGroupColumnBypass(), { wrapper });
    result.current.mutate({ databaseId: "db-1", sensitiveColumnId: "sc-1", groupId: "g-1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/database/db-1/sensitive-column/sc-1/bypass/group/g-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});
