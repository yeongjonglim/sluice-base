import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { GroupsTab } from "@/routes/_authed/access.tsx";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
}));

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const mockUseGroups = vi.fn();
const mockUseGroup = vi.fn();
const mockUseAdminServers = vi.fn();
const mockUseUsers = vi.fn();
const mockUseCreateGroup = vi.fn();
const mockUseUpdateGroup = vi.fn();
const mockUseDeleteGroup = vi.fn();
const mockUseAddGroupMember = vi.fn();
const mockUseRemoveGroupMember = vi.fn();
const mockUseAssignGroupPermission = vi.fn();
const mockUseRemoveGroupPermission = vi.fn();
const mockUseAssignGroupDatabaseRole = vi.fn();
const mockUseRemoveGroupDatabaseRole = vi.fn();

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useGroups: (...args: Array<unknown>) => mockUseGroups(...args),
  useGroup: (...args: Array<unknown>) => mockUseGroup(...args),
  useAdminServers: (...args: Array<unknown>) => mockUseAdminServers(...args),
  useUsers: (...args: Array<unknown>) => mockUseUsers(...args),
  useCreateGroup: () => mockUseCreateGroup(),
  useUpdateGroup: () => mockUseUpdateGroup(),
  useDeleteGroup: () => mockUseDeleteGroup(),
  useAddGroupMember: () => mockUseAddGroupMember(),
  useRemoveGroupMember: () => mockUseRemoveGroupMember(),
  useAssignGroupPermission: () => mockUseAssignGroupPermission(),
  useRemoveGroupPermission: () => mockUseRemoveGroupPermission(),
  useAssignGroupDatabaseRole: () => mockUseAssignGroupDatabaseRole(),
  useRemoveGroupDatabaseRole: () => mockUseRemoveGroupDatabaseRole(),
  // Other hooks used by the module but not by GroupsTab
  useUserRoles: () => ({ isLoading: false, data: { roles: [] } }),
  useDatabaseRoles: () => ({ isLoading: false, data: { roles: [] } }),
  useAssignUserRole: () => ({ mutate: vi.fn() }),
  useAssignDatabaseRole: () => ({ mutate: vi.fn() }),
  useRemoveDatabaseRole: () => ({ mutate: vi.fn() }),
  useMarkSensitiveColumn: () => ({ mutate: vi.fn() }),
  useUnmarkSensitiveColumn: () => ({ mutate: vi.fn() }),
  useGrantColumnBypass: () => ({ mutate: vi.fn() }),
  useRevokeColumnBypass: () => ({ mutate: vi.fn() }),
  useSchema: () => ({ data: undefined }),
  useSensitiveColumns: () => ({ isLoading: false, data: { columns: [] } }),
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

const GROUPS_DATA = {
  groups: [
    { id: "grp-1", name: "Admins", description: "Admin group", memberCount: 3, globalPermissionCount: 1, databaseRoleCount: 2 },
  ],
};

const SERVERS_DATA = {
  servers: [{
    id: "srv-1", name: "Blue", isDisabled: false,
    databases: [
      { id: "db-1", displayName: "Blue App DB", isDisabled: false },
    ],
  }],
};

const USERS_DATA = {
  users: [
    { id: "user-1", email: "alice@example.com", name: "Alice Dev", permissions: [], lastLoginAt: null },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseGroups.mockReturnValue({ isLoading: false, data: GROUPS_DATA });
  mockUseGroup.mockReturnValue({ isLoading: false, data: undefined });
  mockUseAdminServers.mockReturnValue({ data: SERVERS_DATA });
  mockUseUsers.mockReturnValue({ isLoading: false, data: USERS_DATA });
  mockUseCreateGroup.mockReturnValue({ mutate: vi.fn(), isPending: false });
  mockUseUpdateGroup.mockReturnValue({ mutate: vi.fn(), isPending: false });
  mockUseDeleteGroup.mockReturnValue({ mutate: vi.fn(), isPending: false });
  mockUseAddGroupMember.mockReturnValue({ mutate: vi.fn() });
  mockUseRemoveGroupMember.mockReturnValue({ mutate: vi.fn() });
  mockUseAssignGroupPermission.mockReturnValue({ mutate: vi.fn() });
  mockUseRemoveGroupPermission.mockReturnValue({ mutate: vi.fn() });
  mockUseAssignGroupDatabaseRole.mockReturnValue({ mutate: vi.fn() });
  mockUseRemoveGroupDatabaseRole.mockReturnValue({ mutate: vi.fn() });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

// ── GroupsTab ─────────────────────────────────────────────────────────────

describe("GroupsTab", () => {
  it("renders the group name from useGroups", () => {
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    expect(screen.getByText("Admins")).toBeInTheDocument();
  });

  it("renders a Create group control", () => {
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    expect(screen.getByRole("button", { name: /create group/i })).toBeInTheDocument();
  });

  it("shows member count badge for each group", () => {
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    // "3 members" badge
    expect(screen.getByText(/3/)).toBeInTheDocument();
  });

  it("shows placeholder when no group is selected", () => {
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    expect(screen.getByText(/select a group/i)).toBeInTheDocument();
  });
});
