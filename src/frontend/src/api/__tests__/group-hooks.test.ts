import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import {
  useAddGroupMember,
  useAssignGroupDatabaseRole,
  useAssignGroupPermission,
  useCreateGroup,
  useDeleteGroup,
  useGroups,
  useRemoveGroupDatabaseRole,
  useRemoveGroupMember,
  useRemoveGroupPermission,
} from "@/api/hooks";

vi.mock("@/api/client", () => ({
  apiRequest: vi.fn(),
  ApiError: class ApiError extends Error {
    constructor(public status: number, public body: unknown) { super(`API ${status}`); }
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
  it("calls apiRequest with /api/admin/group", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ groups: [{ id: "g1", name: "Analysts", memberCount: 2 }] });
    const { result } = renderHook(() => useGroups(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/admin/group");
  });

  it("returns the group list", async () => {
    const data = { groups: [{ id: "g1", name: "Analysts", memberCount: 2 }] };
    vi.mocked(apiRequest).mockResolvedValue(data);
    const { result } = renderHook(() => useGroups(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.groups[0].name).toBe("Analysts");
  });
});

describe("useCreateGroup", () => {
  it("POSTs to /api/admin/group with the body", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useCreateGroup(), { wrapper });
    result.current.mutate({ name: "New Group", description: "A new group" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group",
      expect.objectContaining({ method: "POST", body: { name: "New Group", description: "A new group" } }),
    );
  });

  it("shows an error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("network error"));
    const { result } = renderHook(() => useCreateGroup(), { wrapper });
    result.current.mutate({ name: "New Group", description: null });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useAddGroupMember", () => {
  it("POSTs to the member path with no body", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAddGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g1", userId: "u1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1/member/u1",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("shows an error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("network error"));
    const { result } = renderHook(() => useAddGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g1", userId: "u1" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useRemoveGroupMember", () => {
  it("DELETEs the member path", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveGroupMember(), { wrapper });
    result.current.mutate({ groupId: "g1", userId: "u1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1/member/u1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

describe("useAssignGroupPermission", () => {
  it("POSTs to the permission path with no body", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAssignGroupPermission(), { wrapper });
    result.current.mutate({ groupId: "g1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1/permission/query:execute",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("useRemoveGroupPermission", () => {
  it("DELETEs the permission path", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveGroupPermission(), { wrapper });
    result.current.mutate({ groupId: "g1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1/permission/query:execute",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

describe("useAssignGroupDatabaseRole", () => {
  it("POSTs to the database role path with no body", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAssignGroupDatabaseRole(), { wrapper });
    result.current.mutate({ groupId: "g1", databaseId: "db1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1/database/db1/role/query:execute",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("useRemoveGroupDatabaseRole", () => {
  it("DELETEs the database role path", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveGroupDatabaseRole(), { wrapper });
    result.current.mutate({ groupId: "g1", databaseId: "db1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1/database/db1/role/query:execute",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

describe("useDeleteGroup", () => {
  it("DELETEs the group path", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useDeleteGroup(), { wrapper });
    result.current.mutate("g1");
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/admin/group/g1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});
