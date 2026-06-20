import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import {
  AccessPage,
  ByDatabaseTab,
  ByUserTab,
  SensitiveColumnsTab,
} from "@/routes/_authed/access.tsx";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
}));

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const mockUseAdminServers = vi.fn();
const mockUseUsers = vi.fn();
const mockUseSensitiveColumns = vi.fn();
const mockUseSchema = vi.fn();
const mockUseMarkSensitiveColumn = vi.fn();
const mockUseUnmarkSensitiveColumn = vi.fn();
const mockUseGrantColumnBypass = vi.fn();
const mockUseRevokeColumnBypass = vi.fn();

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useAdminServers: (...a: Array<unknown>) => mockUseAdminServers(...a),
  useUsers: (...a: Array<unknown>) => mockUseUsers(...a),
  useSensitiveColumns: (...a: Array<unknown>) => mockUseSensitiveColumns(...a),
  useSchema: (...a: Array<unknown>) => mockUseSchema(...a),
  useMarkSensitiveColumn: () => mockUseMarkSensitiveColumn(),
  useUnmarkSensitiveColumn: () => mockUseUnmarkSensitiveColumn(),
  useGrantColumnBypass: () => mockUseGrantColumnBypass(),
  useRevokeColumnBypass: () => mockUseRevokeColumnBypass(),
  // Hooks used by the other tabs / panels — return inert defaults
  useGroups: () => ({ isLoading: false, data: { groups: [] } }),
  useGroup: () => ({ isLoading: false, data: undefined }),
  useCreateGroup: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateGroup: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteGroup: () => ({ mutate: vi.fn(), isPending: false }),
  useAddGroupMember: () => ({ mutate: vi.fn() }),
  useRemoveGroupMember: () => ({ mutate: vi.fn() }),
  useAssignGroupPermission: () => ({ mutate: vi.fn() }),
  useRemoveGroupPermission: () => ({ mutate: vi.fn() }),
  useAssignGroupDatabaseRole: () => ({ mutate: vi.fn() }),
  useRemoveGroupDatabaseRole: () => ({ mutate: vi.fn() }),
  useUserRoles: () => ({ isLoading: false, data: { roles: [] } }),
  useDatabaseRoles: () => ({ isLoading: false, data: { roles: [] } }),
  useAssignUserRole: () => ({ mutate: vi.fn() }),
  useAssignDatabaseRole: () => ({ mutate: vi.fn() }),
  useRemoveDatabaseRole: () => ({ mutate: vi.fn() }),
}));

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

const SERVERS = {
  servers: [{
    id: "srv-1", name: "Blue", isDisabled: false,
    databases: [{ id: "db-1", displayName: "Blue App DB", isDisabled: false }],
  }],
};

const USERS = {
  users: [{ id: "user-1", email: "alice@example.com", name: "Alice", permissions: [], lastLoginAt: null }],
};

const SCHEMA = {
  schemas: [{
    name: "public",
    tables: [{ name: "accounts", columns: [{ name: "ssn" }, { name: "email" }] }],
  }],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseAdminServers.mockReturnValue({ data: SERVERS });
  mockUseUsers.mockReturnValue({ isLoading: false, data: USERS });
  mockUseSensitiveColumns.mockReturnValue({ isLoading: false, data: { columns: [] } });
  mockUseSchema.mockReturnValue({ data: SCHEMA });
  mockUseMarkSensitiveColumn.mockReturnValue({ mutate: vi.fn() });
  mockUseUnmarkSensitiveColumn.mockReturnValue({ mutate: vi.fn() });
  mockUseGrantColumnBypass.mockReturnValue({ mutate: vi.fn() });
  mockUseRevokeColumnBypass.mockReturnValue({ mutate: vi.fn() });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("AccessPage", () => {
  it("renders all four tabs", () => {
    render(React.createElement(AccessPage), { wrapper: Wrapper });
    expect(screen.getByRole("tab", { name: /by database/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /by user/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /groups/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /sensitive columns/i })).toBeInTheDocument();
  });
});

describe("ByDatabaseTab", () => {
  it("lists databases and shows a panel after selecting one", async () => {
    render(React.createElement(ByDatabaseTab), { wrapper: Wrapper });
    const dbButton = screen.getByRole("button", { name: /blue app db/i });
    await userEvent.click(dbButton);
    // DatabaseRolePanel shows the selected database name + server
    await waitFor(() => expect(screen.getAllByText("Blue App DB").length).toBeGreaterThan(0));
  });
});

describe("ByUserTab", () => {
  it("lists users and shows a panel after selecting one", async () => {
    render(React.createElement(ByUserTab), { wrapper: Wrapper });
    await userEvent.click(screen.getByRole("button", { name: /alice@example.com/i }));
    await waitFor(() => expect(screen.getAllByText("alice@example.com").length).toBeGreaterThan(0));
  });
});

describe("SensitiveColumnsTab", () => {
  function selectDatabase() {
    render(React.createElement(SensitiveColumnsTab), { wrapper: Wrapper });
    return userEvent.click(screen.getByText("Blue App DB"));
  }

  it("shows the empty state when no columns are configured", async () => {
    await selectDatabase();
    await waitFor(() => expect(screen.getByText(/no sensitive columns configured/i)).toBeInTheDocument());
  });

  it("renders a sensitive column with its bypasses and revokes one", async () => {
    const revokeFn = vi.fn();
    mockUseRevokeColumnBypass.mockReturnValue({ mutate: revokeFn });
    mockUseSensitiveColumns.mockReturnValue({
      isLoading: false,
      data: { columns: [{
        id: "col-1", schemaName: "public", tableName: "accounts", columnName: "ssn",
        bypasses: [{ id: "bp-1", userId: "user-1", userEmail: "alice@example.com", grantedAt: "2026-06-01T00:00:00Z" }],
      }] },
    });
    await selectDatabase();
    await waitFor(() => expect(screen.getByText("public.accounts.ssn")).toBeInTheDocument());
    await userEvent.click(screen.getByRole("button", { name: /revoke/i }));
    expect(revokeFn).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", sensitiveColumnId: "col-1", userId: "user-1" }),
    );
  });

  it("unmarks a sensitive column", async () => {
    const unmarkFn = vi.fn();
    mockUseUnmarkSensitiveColumn.mockReturnValue({ mutate: unmarkFn });
    mockUseSensitiveColumns.mockReturnValue({
      isLoading: false,
      data: { columns: [{ id: "col-1", schemaName: "public", tableName: "accounts", columnName: "ssn", bypasses: [] }] },
    });
    await selectDatabase();
    await waitFor(() => screen.getByText("public.accounts.ssn"));
    await userEvent.click(screen.getByRole("button", { name: /remove/i }));
    expect(unmarkFn).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", sensitiveColumnId: "col-1" }),
    );
  });

  it("opens the mark-column modal and marks a column via the form", async () => {
    const markFn = vi.fn();
    mockUseMarkSensitiveColumn.mockReturnValue({ mutate: markFn });
    await selectDatabase();
    await userEvent.click(screen.getByRole("button", { name: /mark column as sensitive/i }));
    const dialog = await screen.findByRole("dialog");
    // Pick schema → table → column via the three selects, then Mark
    await userEvent.click(within(dialog).getByLabelText("Schema"));
    await userEvent.click(await screen.findByText("public"));
    await userEvent.click(within(dialog).getByLabelText("Table"));
    await userEvent.click(await screen.findByText("accounts"));
    await userEvent.click(within(dialog).getByLabelText("Column"));
    await userEvent.click(await screen.findByText("ssn"));
    await userEvent.click(within(dialog).getByRole("button", { name: /mark as sensitive/i }));
    expect(markFn).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", schemaName: "public", tableName: "accounts", columnName: "ssn" }),
      expect.any(Object),
    );
  });
});
