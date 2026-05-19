import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { DatabaseRolePanel, UserRolePanel } from "@/routes/_authed/access.tsx";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
}));

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const mockAssignUserRole = vi.fn();
const mockAssignDatabaseRole = vi.fn();
const mockRemoveRole = vi.fn();
const mockUseAdminServers = vi.fn();
const mockUseUserRoles = vi.fn();
const mockUseDatabaseRoles = vi.fn();
const mockUseUsers = vi.fn();

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useAdminServers: (...args: Array<unknown>) => mockUseAdminServers(...args),
  useUserRoles: (...args: Array<unknown>) => mockUseUserRoles(...args),
  useDatabaseRoles: (...args: Array<unknown>) => mockUseDatabaseRoles(...args),
  useUsers: (...args: Array<unknown>) => mockUseUsers(...args),
  useAssignUserRole: () => ({ mutate: mockAssignUserRole }),
  useAssignDatabaseRole: () => ({ mutate: mockAssignDatabaseRole }),
  useRemoveDatabaseRole: () => ({ mutate: mockRemoveRole }),
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

const SERVERS_DATA = {
  servers: [{
    id: "srv-1", name: "Blue", isDisabled: false,
    databases: [
      { id: "db-1", displayName: "Blue App DB", isDisabled: false },
      { id: "db-2", displayName: "Blue Reports DB", isDisabled: false },
    ],
  }],
};

const USERS_DATA = {
  users: [
    { id: "user-2", email: "bob@example.com", name: "Bob Dev", permissions: [], lastLoginAt: null },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseAdminServers.mockReturnValue({ data: SERVERS_DATA });
  mockUseUserRoles.mockReturnValue({ isLoading: false, data: { roles: [] } });
  mockUseDatabaseRoles.mockReturnValue({ isLoading: false, data: { roles: [] } });
  mockUseUsers.mockReturnValue({ isLoading: false, data: USERS_DATA });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

const testUser = { id: "user-1", email: "alice@example.com", name: "Alice Dev" };
const testDatabase = { id: "db-1", displayName: "Blue App DB", isDisabled: false, serverName: "Blue" };

// ── UserRolePanel ─────────────────────────────────────────────────────────

describe("UserRolePanel", () => {
  it("renders a checkbox for each database × permission pair", () => {
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    // 2 databases × 5 permissions = 10 checkboxes
    expect(screen.getAllByRole("checkbox")).toHaveLength(10);
  });

  it("checks boxes that match existing roles", () => {
    mockUseUserRoles.mockReturnValue({
      isLoading: false,
      data: { roles: [{ id: "r-1", databaseId: "db-1", permission: "query:execute", databaseDisplayName: "Blue App DB", serverName: "Blue", grantedAt: "" }] },
    });
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    const checked = screen.getAllByRole("checkbox", { checked: true });
    expect(checked).toHaveLength(1);
    expect(checked[0]).toHaveAccessibleName(/query execute on blue app db/i);
  });

  it("calls assignUserRole when checking an unchecked box", async () => {
    mockAssignUserRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs.onSuccess?.());
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: false })[0].closest("td")!);
    expect(mockAssignUserRole).toHaveBeenCalledWith(
      expect.objectContaining({ userId: "user-1", databaseId: expect.any(String), permission: expect.any(String) }),
      expect.any(Object),
    );
  });

  it("calls removeDatabaseRole when unchecking a checked box", async () => {
    mockUseUserRoles.mockReturnValue({
      isLoading: false,
      data: { roles: [{ id: "r-1", databaseId: "db-1", permission: "query:execute", databaseDisplayName: "Blue App DB", serverName: "Blue", grantedAt: "" }] },
    });
    mockRemoveRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs.onSuccess?.());
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: true })[0].closest("td")!);
    expect(mockRemoveRole).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", userId: "user-1", permission: "query:execute" }),
      expect.any(Object),
    );
  });

  it("shows Access updated notification after mutation settles", async () => {
    const { notifications } = await import("@mantine/notifications");
    mockAssignUserRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs.onSuccess?.());
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: false })[0].closest("td")!);
    expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ title: "Access updated", color: "teal" }),
    );
  });

  it("disables all checkboxes while roles are loading", () => {
    mockUseUserRoles.mockReturnValue({ isLoading: true, data: undefined });
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    screen.getAllByRole("checkbox").forEach((cb) => expect(cb).toBeDisabled());
  });
});

// ── DatabaseRolePanel ─────────────────────────────────────────────────────

describe("DatabaseRolePanel", () => {
  it("renders a checkbox for each user × permission pair", () => {
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    // 1 user × 5 permissions = 5 checkboxes
    expect(screen.getAllByRole("checkbox")).toHaveLength(5);
  });

  it("checks boxes that match existing roles", () => {
    mockUseDatabaseRoles.mockReturnValue({
      isLoading: false,
      data: { roles: [{ id: "r-2", userId: "user-2", permission: "query:execute", userEmail: "bob@example.com", userName: "Bob Dev", grantedAt: "", grantedById: null }] },
    });
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    const checked = screen.getAllByRole("checkbox", { checked: true });
    expect(checked).toHaveLength(1);
    expect(checked[0]).toHaveAccessibleName(/query execute for bob@example.com/i);
  });

  it("calls assignDatabaseRole when checking an unchecked box", async () => {
    mockAssignDatabaseRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs.onSuccess?.());
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: false })[0].closest("td")!);
    expect(mockAssignDatabaseRole).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", userId: "user-2", permission: expect.any(String) }),
      expect.any(Object),
    );
  });

  it("calls removeDatabaseRole when unchecking a checked box", async () => {
    mockUseDatabaseRoles.mockReturnValue({
      isLoading: false,
      data: { roles: [{ id: "r-2", userId: "user-2", permission: "query:execute", userEmail: "bob@example.com", userName: "Bob Dev", grantedAt: "", grantedById: null }] },
    });
    mockRemoveRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs.onSuccess?.());
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: true })[0].closest("td")!);
    expect(mockRemoveRole).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", userId: "user-2", permission: "query:execute" }),
      expect.any(Object),
    );
  });

  it("disables all checkboxes while roles are loading", () => {
    mockUseDatabaseRoles.mockReturnValue({ isLoading: true, data: undefined });
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    screen.getAllByRole("checkbox").forEach((cb) => expect(cb).toBeDisabled());
  });
});
