import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useAssignDatabaseRole, useAssignUserRole, useRemoveDatabaseRole } from "@/api/hooks";

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

describe("useAssignDatabaseRole", () => {
  it("does NOT show a success notification on success", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAssignDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", userId: "u-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(notifications.show).not.toHaveBeenCalledWith(
      expect.objectContaining({ title: "Role assigned" }),
    );
  });

  it("shows an error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("network error"));
    const { result } = renderHook(() => useAssignDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", userId: "u-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useAssignUserRole", () => {
  it("does NOT show a success notification on success", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAssignUserRole(), { wrapper });
    result.current.mutate({ userId: "u-1", databaseId: "db-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(notifications.show).not.toHaveBeenCalledWith(
      expect.objectContaining({ title: "Role assigned" }),
    );
  });
});

describe("useRemoveDatabaseRole", () => {
  it("does NOT show a success notification on success", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", userId: "u-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(notifications.show).not.toHaveBeenCalledWith(
      expect.objectContaining({ title: "Role removed" }),
    );
  });
});
