import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import type { QueryHistoryItem } from "@/api/hooks";

const mockNavigate = vi.fn();
let mockSearch: Record<string, unknown> = {};

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: Record<string, unknown>) => ({
    ...opts,
    useSearch: () => mockSearch,
  }),
  redirect: (arg: unknown) => ({ __redirect: arg }),
  useNavigate: () => mockNavigate,
}));

const mockUseQueryHistory = vi.fn();
const mockUseCatalogServer = vi.fn();

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useCatalogServer: () => mockUseCatalogServer(),
  useQueryHistory: (...a: Array<unknown>) => mockUseQueryHistory(...a),
}));

const mockUseHasPermission = vi.fn();
vi.mock("@/auth/permission", () => ({
  useHasPermission: (...a: Array<unknown>) => mockUseHasPermission(...a),
}));

vi.mock("@/components/SqlEditor", () => ({
  SqlEditor: ({ value }: { value: string }) => React.createElement("pre", null, value),
}));

const mockNotify = vi.fn();
vi.mock("@mantine/notifications", () => ({
  notifications: { show: (...a: Array<unknown>) => mockNotify(...a) },
}));

// Imported after mocks are registered.
const { QueryHistoryPage, Route } = await import("@/routes/_authed/query/history.tsx");

// Route's static type doesn't surface validateSearch/beforeLoad, but the
// mocked createFileRoute returns the raw options object that does.
const RouteOpts = Route as unknown as {
  validateSearch: (s: Record<string, unknown>) => Record<string, unknown>;
  beforeLoad: (o: {
    context: { queryClient: { getQueryData: () => { permissions: Array<string> } | undefined } };
  }) => void;
};

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

function makeItem(overrides: Partial<QueryHistoryItem> = {}): QueryHistoryItem {
  return {
    id: "1",
    databaseId: "db-1",
    databaseDisplayName: "Blue DB",
    queryText: "SELECT 1",
    status: "Success",
    executedAt: "2026-06-01T10:00:00Z",
    durationMs: 12,
    rowCount: 1,
    error: null,
    userId: "u1",
    userName: "Alice",
    sensitiveColumns: [],
    source: "Ui",
    ...overrides,
  };
}

const ITEMS: Array<QueryHistoryItem> = [
  makeItem({ id: "1", status: "Success", source: "Ui" }),
  makeItem({
    id: "2", status: "Error", source: "Mcp", error: "syntax error at or near \"SELCT\"",
    durationMs: null, rowCount: null, sensitiveColumns: ["public.users.email"],
    queryText: "SELCT 1", userName: "Bob",
  }),
  makeItem({
    id: "3", status: "Timeout", source: "Ui", error: "Query timed out after 30s.",
    queryText: "SELECT pg_sleep(99)", databaseDisplayName: null, userName: null,
  }),
];

const SERVERS = {
  servers: [{ id: "srv-1", name: "Blue", databases: [{ id: "db-1", displayName: "App DB" }] }],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockSearch = {};
  mockUseHasPermission.mockReturnValue(false);
  mockUseCatalogServer.mockReturnValue({ data: SERVERS });
  mockUseQueryHistory.mockReturnValue({ isPending: false, isError: false, data: { items: ITEMS } });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

function renderPage() {
  return render(React.createElement(QueryHistoryPage), { wrapper: Wrapper });
}

describe("QueryHistoryPage", () => {
  it("renders a row per history item with status, database, duration and rows", () => {
    renderPage();
    const table = within(screen.getByRole("table"));
    expect(table.getByText("Success")).toBeInTheDocument();
    expect(table.getByText("Error")).toBeInTheDocument();
    expect(table.getByText("Timeout")).toBeInTheDocument();
    expect(table.getAllByText("Blue DB").length).toBe(2);
    expect(table.getAllByText("12 ms").length).toBeGreaterThan(0);
    // null duration / rowCount render as an em dash
    expect(table.getAllByText("—").length).toBeGreaterThan(0);
  });

  it("navigates with the chosen source when the source filter changes", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("combobox", { name: "Source" }));
    await userEvent.click(await screen.findByText("MCP"));
    expect(mockNavigate).toHaveBeenCalled();
    const arg = mockNavigate.mock.calls[0][0] as { search: (p: object) => Record<string, unknown> };
    expect(arg.search({})).toMatchObject({ source: "Mcp" });
  });

  it("navigates with the chosen status when the status filter changes", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("combobox", { name: "Status" }));
    await userEvent.click(await screen.findByText("Blocked"));
    const arg = mockNavigate.mock.calls[0][0] as { search: (p: object) => Record<string, unknown> };
    expect(arg.search({})).toMatchObject({ status: "Blocked" });
  });

  it("navigates with the chosen database when the database filter changes", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("combobox", { name: "Database" }));
    await userEvent.click(await screen.findByText("Blue — App DB"));
    const arg = mockNavigate.mock.calls[0][0] as { search: (p: object) => Record<string, unknown> };
    expect(arg.search({})).toMatchObject({ databaseId: "db-1" });
  });

  it("navigates with the chosen sensitive column when that filter changes", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("combobox", { name: "Sensitive columns" }));
    await userEvent.click(await screen.findByText("Any sensitive column"));
    const arg = mockNavigate.mock.calls[0][0] as { search: (p: object) => Record<string, unknown> };
    expect(arg.search({})).toMatchObject({ sensitiveColumn: ["any"] });
  });

  it("navigates with the typed date when the From filter changes", async () => {
    renderPage();
    await userEvent.type(screen.getByLabelText("From"), "2026-03-15");
    await waitFor(() => expect(mockNavigate).toHaveBeenCalled());
    const arg = mockNavigate.mock.calls.at(-1)![0] as { search: (p: object) => Record<string, unknown> };
    expect(arg.search({})).toMatchObject({ from: "2026-03-15" });
  });

  it("copies the SQL to the clipboard and shows a notification", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    renderPage();
    await userEvent.click(screen.getAllByRole("button", { name: /copy sql/i })[0]);
    expect(writeText).toHaveBeenCalledWith("SELECT 1");
    await waitFor(() => expect(mockNotify).toHaveBeenCalled());
  });

  it("silently ignores a clipboard failure when copying", async () => {
    const writeText = vi.fn().mockRejectedValue(new Error("denied"));
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    renderPage();
    await userEvent.click(screen.getAllByRole("button", { name: /copy sql/i })[0]);
    await waitFor(() => expect(writeText).toHaveBeenCalled());
    expect(mockNotify).not.toHaveBeenCalled();
  });

  it("shows the User column and filters by user name when query:audit is granted", async () => {
    mockUseHasPermission.mockReturnValue(true);
    renderPage();
    expect(screen.getByRole("columnheader", { name: "User" })).toBeInTheDocument();
    expect(screen.getByText("Alice")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("User"), "bob");
    // Only Bob's row remains; the empty/other rows are filtered out
    expect(screen.getByText("Bob")).toBeInTheDocument();
    expect(screen.queryByText("Alice")).not.toBeInTheDocument();
  });

  it("shows the empty state when no rows match", () => {
    mockUseQueryHistory.mockReturnValue({ isPending: false, isError: false, data: { items: [] } });
    renderPage();
    expect(screen.getByText(/no entries match/i)).toBeInTheDocument();
  });

  it("shows the loading state", () => {
    mockUseQueryHistory.mockReturnValue({ isPending: true, isError: false, data: undefined });
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it("shows the error state", () => {
    mockUseQueryHistory.mockReturnValue({ isPending: false, isError: true, data: undefined });
    renderPage();
    expect(screen.getByText(/failed to load history/i)).toBeInTheDocument();
  });
});

describe("RouteOpts.validateSearch", () => {
  it("passes through string filters and normalizes sensitiveColumn shapes", () => {
    expect(
      RouteOpts.validateSearch({
        from: "2026-01-01", to: "2026-02-01", databaseId: "db-1",
        status: "Error", source: "Mcp", sensitiveColumn: ["a", 2, "b"],
      }),
    ).toEqual({
      from: "2026-01-01", to: "2026-02-01", databaseId: "db-1",
      status: "Error", source: "Mcp", sensitiveColumn: ["a", "b"],
    });
  });

  it("coerces a single sensitiveColumn string into an array and drops non-strings", () => {
    expect(RouteOpts.validateSearch({ sensitiveColumn: "email" }).sensitiveColumn).toEqual(["email"]);
    expect(RouteOpts.validateSearch({ from: 5, source: 7 })).toEqual({
      from: undefined, to: undefined, databaseId: undefined,
      status: undefined, source: undefined, sensitiveColumn: undefined,
    });
  });
});

describe("RouteOpts.beforeLoad", () => {
  function context(permissions: Array<string>) {
    return { context: { queryClient: { getQueryData: () => ({ permissions }) } } };
  }

  it("allows users that have query:execute", () => {
    expect(() => RouteOpts.beforeLoad(context(["query:execute"]))).not.toThrow();
  });

  it("redirects users without query:execute", () => {
    expect(() => RouteOpts.beforeLoad(context([]))).toThrow();
  });
});
