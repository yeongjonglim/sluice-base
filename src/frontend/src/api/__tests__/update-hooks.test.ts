import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import {
  useApproveUpdate,
  useCancelUpdate,
  useExecuteUpdate,
  useRejectUpdate,
  useSubmitUpdate,
  useUpdateRequest,
  useUpdateRequests,
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

const { apiRequest } = await import("@/api/client");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  vi.clearAllMocks();
});

const fakeList = {
  requests: [
    {
      id: "req-1",
      serverName: "Blue",
      submitterName: "Alice",
      reason: "fix data",
      status: "pending",
      submittedAt: "2026-05-09T00:00:00Z",
      execSuccess: null,
    },
  ],
};

const fakeDetail = {
  id: "req-1",
  serverId: "srv-1",
  serverName: "Blue",
  submitterId: "user-1",
  submitterName: "Alice",
  sqlText: "UPDATE public.users SET email = email WHERE 1=0",
  reason: "fix data",
  status: "pending",
  reviewerId: null,
  reviewerName: null,
  reviewNote: null,
  executorId: null,
  executorName: null,
  submittedAt: "2026-05-09T00:00:00Z",
  reviewedAt: null,
  executedAt: null,
  execSuccess: null,
  execDurationMs: null,
  execAffectedRows: null,
  execError: null,
};

describe("useUpdateRequests", () => {
  it("fetches GET /api/update with no filters", async () => {
    vi.mocked(apiRequest).mockResolvedValue(fakeList);
    const { result } = renderHook(() => useUpdateRequests({}), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/update");
    expect(result.current.data?.requests).toHaveLength(1);
  });

  it("appends status filter to the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue(fakeList);
    const { result } = renderHook(() => useUpdateRequests({ status: "Pending" }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/update?status=Pending");
  });

  it("appends multiple filters to the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue(fakeList);
    const { result } = renderHook(
      () => useUpdateRequests({ status: "Pending", databaseId: "db-123" }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const calledUrl = vi.mocked(apiRequest).mock.calls[0][0] as string;
    expect(calledUrl).toContain("status=Pending");
    expect(calledUrl).toContain("databaseId=db-123");
  });

  it("omits undefined filter values from the URL", async () => {
    vi.mocked(apiRequest).mockResolvedValue(fakeList);
    const { result } = renderHook(
      () => useUpdateRequests({ status: undefined, from: "2026-01-01" }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const calledUrl = vi.mocked(apiRequest).mock.calls[0][0] as string;
    expect(calledUrl).toContain("from=2026-01-01");
    expect(calledUrl).not.toContain("status=");
  });
});

describe("useUpdateRequest", () => {
  it("fetches GET /api/update/:id", async () => {
    vi.mocked(apiRequest).mockResolvedValue(fakeDetail);
    const { result } = renderHook(() => useUpdateRequest("req-1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/update/req-1");
    expect(result.current.data?.status).toBe("pending");
  });
});

describe("useSubmitUpdate", () => {
  it("posts to /api/update", async () => {
    vi.mocked(apiRequest).mockResolvedValue(fakeDetail);
    const { result } = renderHook(() => useSubmitUpdate(), { wrapper });
    result.current.mutate({
      databaseId: "db-1",
      sqlText: "UPDATE public.users SET email = email WHERE 1=0",
      reason: "fix data",
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/update",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("useApproveUpdate", () => {
  it("posts to /api/update/:id/approve", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ ...fakeDetail, status: "approved" });
    const { result } = renderHook(() => useApproveUpdate(), { wrapper });
    result.current.mutate({ id: "req-1", note: "looks good" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/update/req-1/approve",
      expect.objectContaining({ method: "POST", body: { note: "looks good" } }),
    );
  });
});

describe("useRejectUpdate", () => {
  it("posts to /api/update/:id/reject", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ ...fakeDetail, status: "rejected" });
    const { result } = renderHook(() => useRejectUpdate(), { wrapper });
    result.current.mutate({ id: "req-1", note: "not safe" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/update/req-1/reject",
      expect.objectContaining({ method: "POST", body: { note: "not safe" } }),
    );
  });
});

describe("useCancelUpdate", () => {
  it("posts to /api/update/:id/cancel", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ ...fakeDetail, status: "cancelled" });
    const { result } = renderHook(() => useCancelUpdate(), { wrapper });
    result.current.mutate({ id: "req-1", note: "not required" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/update/req-1/cancel",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("useExecuteUpdate", () => {
  it("posts to /api/update/:id/execute", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      ...fakeDetail,
      status: "executed",
      execSuccess: true,
      execDurationMs: 42,
      execAffectedRows: 0,
    });
    const { result } = renderHook(() => useExecuteUpdate(), { wrapper });
    result.current.mutate("req-1");
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/update/req-1/execute",
      expect.objectContaining({ method: "POST" }),
    );
    expect(result.current.data?.execSuccess).toBe(true);
  });
});
