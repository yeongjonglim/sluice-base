import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useExportSchemaDdl } from "@/api/hooks";

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

vi.mock("@/utils/download", () => ({
  downloadTextFile: vi.fn(),
}));

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const { apiRequest } = await import("@/api/client");
const { downloadTextFile } = await import("@/utils/download");
const { notifications } = await import("@mantine/notifications");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useExportSchemaDdl", () => {
  it("fetches the DDL and triggers a download", async () => {
    vi.mocked(apiRequest).mockResolvedValue("CREATE TABLE public.users ();");
    const { result } = renderHook(() => useExportSchemaDdl(), { wrapper });

    result.current.mutate({ databaseId: "db-1", filename: "blue-schema.sql" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/schema/db-1/ddl");
    expect(downloadTextFile).toHaveBeenCalledWith(
      "CREATE TABLE public.users ();",
      "blue-schema.sql",
      "application/sql",
    );
  });

  it("shows a red notification when the request fails", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("network error"));
    const { result } = renderHook(() => useExportSchemaDdl(), { wrapper });

    result.current.mutate({ databaseId: "db-1", filename: "blue-schema.sql" });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(downloadTextFile).not.toHaveBeenCalled();
    expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ color: "red" }),
    );
  });
});
