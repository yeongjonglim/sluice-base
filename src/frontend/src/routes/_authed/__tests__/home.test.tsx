import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { Route } from "../index.tsx";

const HomePage = (Route as unknown as Record<string, () => React.ReactNode>).component;

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  useNavigate: () => vi.fn(),
  Link: ({ to, children }: { to: string; children: React.ReactNode }) =>
    React.createElement("a", { href: to }, children),
}));

let permissions: Array<string> = [];

vi.mock("@/auth/permission.ts", () => ({
  useHasPermission: (permission: string) => permissions.includes(permission),
}));

vi.mock("@/auth/hooks/useAuth.tsx", () => ({
  useAuth: () => ({ user: { id: "user-1", name: "Ada Lovelace", email: "ada@acme.dev" } }),
}));

const PENDING = {
  requests: [
    {
      id: "req-9",
      databaseDisplayName: "orders",
      submitterName: "Alan Turing",
      reason: "Backfill missing shipment carriers",
      status: "Pending",
      submittedAt: new Date(Date.now() - 2 * 86_400_000).toISOString(),
      execSuccess: null,
    },
  ],
};

const HISTORY = {
  items: [
    {
      id: "q-1",
      databaseDisplayName: "orders",
      queryText: "select id, email from customers limit 10",
      status: "Success",
      executedAt: new Date(Date.now() - 3_600_000).toISOString(),
    },
  ],
};

const CATALOG = {
  servers: [
    {
      id: "srv-1",
      name: "orders-primary",
      databases: [{ id: "db-orders", displayName: "orders", canWrite: true }],
    },
  ],
};

let pendingData: { requests: Array<Record<string, unknown>> } = PENDING;

vi.mock("@/api/hooks", () => ({
  useUpdateRequests: () => ({ data: pendingData, isPending: false, isError: false }),
  useQueryHistory: () => ({ data: HISTORY, isPending: false, isError: false }),
  useCatalogServer: () => ({ data: CATALOG, isPending: false, isError: false }),
}));

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

afterEach(() => {
  cleanup();
  permissions = [];
  pendingData = PENDING;
});

describe("HomePage", () => {
  it("surfaces pending approvals, recent queries and reachable databases", () => {
    permissions = ["query:execute", "update:approve"];
    render(React.createElement(HomePage), { wrapper: Wrapper });

    expect(screen.getByText(/welcome, ada lovelace/i)).toBeInTheDocument();
    expect(screen.getByText("Waiting on you")).toBeInTheDocument();
    expect(screen.getByText("Backfill missing shipment carriers")).toBeInTheDocument();
    expect(screen.getByText("Recent queries")).toBeInTheDocument();
    expect(screen.getByText("Your databases")).toBeInTheDocument();
    expect(screen.getByText(/orders-primary/)).toBeInTheDocument();
    expect(screen.queryByText("No access yet")).toBeNull();
  });

  it("explains the empty queue instead of leaving a blank panel", () => {
    permissions = ["update:approve"];
    pendingData = { requests: [] };
    render(React.createElement(HomePage), { wrapper: Wrapper });

    expect(screen.getByText("Waiting on you")).toBeInTheDocument();
    expect(screen.getByText(/nothing is waiting for your approval/i)).toBeInTheDocument();
  });

  it("gives an account with no permissions an explicit next step", () => {
    permissions = [];
    render(React.createElement(HomePage), { wrapper: Wrapper });

    expect(screen.getByText("No access yet")).toBeInTheDocument();
    expect(screen.getByText(/ask an administrator/i)).toBeInTheDocument();
    expect(screen.queryByText("Waiting on you")).toBeNull();
    expect(screen.queryByText("Your databases")).toBeNull();
  });
});
