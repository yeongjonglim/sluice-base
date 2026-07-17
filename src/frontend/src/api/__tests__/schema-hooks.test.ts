import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { schemaToCompletions, useSchema, useSchemaCompletions } from "@/api/hooks";

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
  it("is disabled and does not fetch when databaseId is null", async () => {
    vi.mocked(apiRequest).mockResolvedValue({ schemas: [] });

    const { result } = renderHook(() => useSchema(null), { wrapper });
    await new Promise((r) => setTimeout(r, 50));

    expect(apiRequest).not.toHaveBeenCalled();
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("fetches /api/schema/{databaseId} and uses correct query key", async () => {
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

    const { result } = renderHook(() => useSchema("db-abc"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(apiRequest).toHaveBeenCalledWith("/api/schema/db-abc");
    expect(result.current.data).toEqual(mockTree);
  });
});

describe("schemaToCompletions", () => {
  it("transforms SchemaTree into a nested schema→table→column namespace", () => {
    const tree = {
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "users",
              columns: [
                { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
                { name: "email", dataType: "text", isNullable: false, isSensitive: true, isRestricted: false },
              ],
            },
          ],
        },
      ],
    };

    expect(schemaToCompletions(tree)).toEqual({
      public: {
        self: { label: "public", type: "type" },
        children: {
          users: {
            self: { label: "users", type: "type" },
            children: [
              { label: "id", type: "property" },
              { label: "email", type: "property" },
            ],
          },
        },
      },
    });
  });

  it("quotes mixed-case and reserved-word identifiers via apply", () => {
    const tree = {
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "__EFMigrationHistory",
              columns: [{ name: "group", isRestricted: false }],
            },
          ],
        },
      ],
    };

    expect(schemaToCompletions(tree)).toEqual({
      public: {
        self: { label: "public", type: "type" },
        children: {
          __EFMigrationHistory: {
            self: { label: "__EFMigrationHistory", type: "type", apply: '"__EFMigrationHistory"' },
            children: [{ label: "group", type: "property", apply: '"group"' }],
          },
        },
      },
    });
  });

  it("excludes restricted columns but keeps sensitive ones", () => {
    const tree = {
      schemas: [
        {
          name: "hr",
          tables: [
            {
              name: "employees",
              columns: [
                { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
                { name: "ssn", dataType: "text", isNullable: false, isSensitive: true, isRestricted: true },
                { name: "salary", dataType: "numeric", isNullable: false, isSensitive: true, isRestricted: false },
              ],
            },
          ],
        },
      ],
    };

    expect(schemaToCompletions(tree)).toEqual({
      hr: {
        self: { label: "hr", type: "type" },
        children: {
          employees: {
            self: { label: "employees", type: "type" },
            children: [
              { label: "id", type: "property" },
              { label: "salary", type: "property" },
            ],
          },
        },
      },
    });
  });

  it("handles multiple schemas and tables", () => {
    const tree = {
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "users",
              columns: [
                { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
              ],
            },
            {
              name: "orders",
              columns: [
                { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
                { name: "user_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
              ],
            },
          ],
        },
        {
          name: "audit",
          tables: [
            {
              name: "logs",
              columns: [
                { name: "ts", dataType: "timestamptz", isNullable: false, isSensitive: false, isRestricted: false },
              ],
            },
          ],
        },
      ],
    };

    expect(schemaToCompletions(tree)).toEqual({
      public: {
        self: { label: "public", type: "type" },
        children: {
          users: {
            self: { label: "users", type: "type" },
            children: [{ label: "id", type: "property" }],
          },
          orders: {
            self: { label: "orders", type: "type" },
            children: [
              { label: "id", type: "property" },
              { label: "user_id", type: "property" },
            ],
          },
        },
      },
      audit: {
        self: { label: "audit", type: "type" },
        children: {
          logs: {
            self: { label: "logs", type: "type" },
            children: [{ label: "ts", type: "property" }],
          },
        },
      },
    });
  });

  it("returns empty object for empty schema tree", () => {
    expect(schemaToCompletions({ schemas: [] })).toEqual({});
  });

  it("includes view columns alongside table columns", () => {
    const tree = {
      schemas: [
        {
          name: "public",
          tables: [{ name: "orders", columns: [{ name: "id", isRestricted: false }] }],
          views: [{ name: "active_orders", columns: [{ name: "id", isRestricted: false }] }],
        },
      ],
    };

    expect(schemaToCompletions(tree)).toEqual({
      public: {
        self: { label: "public", type: "type" },
        children: {
          orders: {
            self: { label: "orders", type: "type" },
            children: [{ label: "id", type: "property" }],
          },
          active_orders: {
            self: { label: "active_orders", type: "type" },
            children: [{ label: "id", type: "property" }],
          },
        },
      },
    });
  });
});

describe("useSchemaCompletions", () => {
  it("returns undefined when databaseId is null", async () => {
    const { result } = renderHook(() => useSchemaCompletions(null), { wrapper });
    await new Promise((r) => setTimeout(r, 50));

    expect(apiRequest).not.toHaveBeenCalled();
    expect(result.current.data).toBeUndefined();
  });

  it("returns transformed schema when databaseId is set", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "items",
              columns: [
                { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
                { name: "secret", dataType: "text", isNullable: false, isSensitive: true, isRestricted: true },
              ],
            },
          ],
        },
      ],
    });

    const { result } = renderHook(() => useSchemaCompletions("db-1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual({
      public: {
        self: { label: "public", type: "type" },
        children: {
          items: {
            self: { label: "items", type: "type" },
            children: [{ label: "id", type: "property" }],
          },
        },
      },
    });
  });
});
