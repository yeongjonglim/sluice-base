import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { notifications } from "@mantine/notifications";
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

  it("opens the Create group modal when the button is clicked", async () => {
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    await userEvent.click(screen.getByRole("button", { name: /create group/i }));
    // Mantine Modal renders into a portal — wait for the dialog to appear in the document
    await waitFor(() => expect(screen.getByPlaceholderText(/group name/i)).toBeInTheDocument());
  });

  it("calls createGroup.mutate when the Create form is submitted", async () => {
    const mutateFn = vi.fn();
    mockUseCreateGroup.mockReturnValue({ mutate: mutateFn, isPending: false });
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    await userEvent.click(screen.getByRole("button", { name: /create group/i }));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());
    await userEvent.type(screen.getByPlaceholderText(/group name/i), "New Group");
    const createBtn = screen.getByRole("button", { name: /^create$/i });
    await userEvent.click(createBtn);
    expect(mutateFn).toHaveBeenCalledWith(
      expect.objectContaining({ name: "New Group" }),
      expect.any(Object),
    );
  });
});

// ── GroupPanel (rendered when a group is selected) ─────────────────────────

const GROUP_DETAIL_DATA = {
  id: "grp-1",
  name: "Admins",
  description: "Admin group",
  members: [
    { userId: "user-1", email: "alice@example.com" },
  ],
  globalPermissions: [] as Array<string>,
  databaseRoles: [] as Array<{ databaseId: string; permission: string }>,
};

describe("GroupPanel (via GroupsTab group selection)", () => {
  beforeEach(() => {
    mockUseGroup.mockReturnValue({ isLoading: false, data: GROUP_DETAIL_DATA });
  });

  function renderAndSelectGroup() {
    render(React.createElement(GroupsTab), { wrapper: Wrapper });
    return userEvent.click(screen.getByRole("button", { name: /admins/i }));
  }

  it("renders the group name and members section after selecting a group", async () => {
    await renderAndSelectGroup();
    await waitFor(() => expect(screen.getByText("alice@example.com")).toBeInTheDocument());
    expect(screen.getByText(/members/i)).toBeInTheDocument();
  });

  it("calls removeGroupMember when Remove is clicked for a member", async () => {
    const removeMutateFn = vi.fn();
    mockUseRemoveGroupMember.mockReturnValue({ mutate: removeMutateFn });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText("alice@example.com"));
    await userEvent.click(screen.getByRole("button", { name: /remove/i }));
    expect(removeMutateFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", userId: "user-1" }),
      expect.any(Object),
    );
  });

  it("calls assignGroupPermission when checking a global permission checkbox", async () => {
    const assignPermFn = vi.fn();
    mockUseAssignGroupPermission.mockReturnValue({ mutate: assignPermFn });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/global permissions/i));
    const serverManageCb = screen.getByRole("checkbox", { name: /server manage/i });
    await userEvent.click(serverManageCb);
    expect(assignPermFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", permission: "server:manage" }),
      expect.any(Object),
    );
  });

  it("calls removeGroupPermission when unchecking a checked global permission", async () => {
    mockUseGroup.mockReturnValue({
      isLoading: false,
      data: { ...GROUP_DETAIL_DATA, globalPermissions: ["server:manage"] },
    });
    const removePermFn = vi.fn();
    mockUseRemoveGroupPermission.mockReturnValue({ mutate: removePermFn });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/global permissions/i));
    const serverManageCb = screen.getByRole("checkbox", { name: /server manage/i });
    expect(serverManageCb).toBeChecked();
    await userEvent.click(serverManageCb);
    expect(removePermFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", permission: "server:manage" }),
      expect.any(Object),
    );
  });

  it("calls assignGroupDatabaseRole when clicking a db-role matrix cell", async () => {
    const assignDbRoleFn = vi.fn();
    mockUseAssignGroupDatabaseRole.mockReturnValue({ mutate: assignDbRoleFn });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/per-database roles/i));
    // Click the first db-role cell (td with aria-label "Query Execute on Blue App DB")
    const queryExecuteCb = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    await userEvent.click(queryExecuteCb.closest("td")!);
    expect(assignDbRoleFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", databaseId: "db-1", permission: "query:execute" }),
      expect.any(Object),
    );
  });

  it("calls removeGroupDatabaseRole when clicking an already-checked db-role cell", async () => {
    mockUseGroup.mockReturnValue({
      isLoading: false,
      data: { ...GROUP_DETAIL_DATA, databaseRoles: [{ databaseId: "db-1", permission: "query:execute" }] },
    });
    const removeDbRoleFn = vi.fn();
    mockUseRemoveGroupDatabaseRole.mockReturnValue({ mutate: removeDbRoleFn });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/per-database roles/i));
    const queryExecuteCb = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    expect(queryExecuteCb).toBeChecked();
    await userEvent.click(queryExecuteCb.closest("td")!);
    expect(removeDbRoleFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", databaseId: "db-1", permission: "query:execute" }),
      expect.any(Object),
    );
  });

  it("shows a loader when group data is loading", async () => {
    mockUseGroup.mockReturnValue({ isLoading: true, data: undefined });
    await renderAndSelectGroup();
    // Mantine Loader renders as a span with class containing "Loader"
    await waitFor(() => {
      const loader = document.querySelector(".mantine-Loader-root");
      expect(loader).not.toBeNull();
    });
  });

  it("fires the success notification after a db-role toggle settles", async () => {
    // mutate invokes the onSuccess callback so the onSettled notification path runs
    mockUseAssignGroupDatabaseRole.mockReturnValue({
      mutate: (_args: unknown, opts: { onSuccess?: () => void }) => opts.onSuccess?.(),
    });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/per-database roles/i));
    const cb = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    await userEvent.click(cb.closest("td")!);
    expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ title: "Access updated" }),
    );
  });

  it("fires the success notification after granting a global permission", async () => {
    mockUseAssignGroupPermission.mockReturnValue({
      mutate: (_args: unknown, opts: { onSuccess?: () => void }) => opts.onSuccess?.(),
    });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/global permissions/i));
    await userEvent.click(screen.getByRole("checkbox", { name: /server manage/i }));
    expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ title: "Permission granted" }),
    );
  });

  it("adds a member through the Select and notifies", async () => {
    const addMutateFn = vi.fn((_args: unknown, opts: { onSuccess?: () => void }) => opts.onSuccess?.());
    mockUseAddGroupMember.mockReturnValue({ mutate: addMutateFn });
    // user-2 is not yet a member, so it appears in the add-member Select
    mockUseUsers.mockReturnValue({
      isLoading: false,
      data: { users: [
        { id: "user-1", email: "alice@example.com", name: "Alice", permissions: [], lastLoginAt: null },
        { id: "user-2", email: "bob@example.com", name: "Bob", permissions: [], lastLoginAt: null },
      ] },
    });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByPlaceholderText(/add member/i));
    await userEvent.click(screen.getByPlaceholderText(/add member/i));
    await userEvent.click(await screen.findByText("bob@example.com"));
    expect(addMutateFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", userId: "user-2" }),
      expect.any(Object),
    );
  });

  async function openHeaderMenu() {
    const menuBtn = screen.getAllByRole("button").find(
      (b) => b.getAttribute("aria-haspopup") === "menu",
    );
    await userEvent.click(menuBtn!);
  }

  it("renames the group through the menu and modal", async () => {
    const updateFn = vi.fn();
    mockUseUpdateGroup.mockReturnValue({ mutate: updateFn, isPending: false });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/per-database roles/i));
    await openHeaderMenu();
    await userEvent.click(await screen.findByText("Rename"));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());
    // The rename modal pre-fills the name input with the current group name.
    const nameInput = screen.getByDisplayValue("Admins");
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, "Renamed");
    await userEvent.click(screen.getByRole("button", { name: /^save$/i }));
    expect(updateFn).toHaveBeenCalledWith(
      expect.objectContaining({ groupId: "grp-1", name: "Renamed" }),
      expect.any(Object),
    );
  });

  it("deletes the group through the menu and confirm modal", async () => {
    const deleteFn = vi.fn();
    mockUseDeleteGroup.mockReturnValue({ mutate: deleteFn, isPending: false });
    await renderAndSelectGroup();
    await waitFor(() => screen.getByText(/per-database roles/i));
    await openHeaderMenu();
    await userEvent.click(await screen.findByText(/delete group/i));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());
    const confirmBtn = screen.getByRole("button", { name: /^delete$/i });
    await userEvent.click(confirmBtn);
    expect(deleteFn).toHaveBeenCalledWith("grp-1", expect.any(Object));
  });
});
