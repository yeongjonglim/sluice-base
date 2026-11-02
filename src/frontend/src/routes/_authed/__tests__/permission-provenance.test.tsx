import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { modals } from "@mantine/modals";
import React from "react";
import { redirect } from "@tanstack/react-router";
import { PermissionsAdminPage, Route } from "@/routes/_authed/permission.tsx";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
}));

vi.mock("@mantine/modals", () => ({
  modals: { openConfirmModal: vi.fn() },
}));

const mockUseMe = vi.fn();
const mockUseUsers = vi.fn();
const mockUsePermissionCatalog = vi.fn();
const mockUseGrantPermission = vi.fn();
const mockUseRevokePermission = vi.fn();
const mockDisable = vi.fn();
const mockEnable = vi.fn();
const mockRevokeMcp = vi.fn();

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useMe: (...args: Array<unknown>) => mockUseMe(...args),
  useUsers: (...args: Array<unknown>) => mockUseUsers(...args),
  usePermissionCatalog: (...args: Array<unknown>) => mockUsePermissionCatalog(...args),
  useGrantPermission: () => mockUseGrantPermission(),
  useRevokePermission: () => mockUseRevokePermission(),
  useDisableUser: () => ({ mutate: mockDisable }),
  useEnableUser: () => ({ mutate: mockEnable }),
  useRevokeUserMcpGrants: () => ({ mutate: mockRevokeMcp }),
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

const CATALOG = { permissions: ["permission:manage", "server:manage"] };

const USERS = {
  users: [
    {
      id: "user-1", email: "alice@example.com", name: "Alice", lastLoginAt: "2026-06-01T10:00:00Z",
      permissions: [
        { permission: "permission:manage", fromDirect: true, fromGroups: [] },
      ],
    },
    {
      id: "user-3", email: "carol@example.com", name: "Carol", lastLoginAt: null, disabledAt: null,
      permissions: [],
    },
    {
      id: "user-2", email: "bob@example.com", name: "Bob", lastLoginAt: null, disabledAt: "2026-06-02T10:00:00Z",
      permissions: [
        // server:manage is inherited only via a group
        { permission: "server:manage", fromDirect: false, fromGroups: [{ groupId: "g1", name: "Platform" }] },
      ],
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseMe.mockReturnValue({ data: { id: "user-1" } });
  mockUseUsers.mockReturnValue({ isLoading: false, data: USERS });
  mockUsePermissionCatalog.mockReturnValue({ data: CATALOG });
  mockUseGrantPermission.mockReturnValue({ mutate: vi.fn(), isPending: false, variables: undefined });
  mockUseRevokePermission.mockReturnValue({ mutate: vi.fn(), isPending: false, variables: undefined });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("PermissionsAdminPage provenance", () => {
  it("renders users and the permission columns", () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    expect(screen.getByText("alice@example.com")).toBeInTheDocument();
    expect(screen.getByText("bob@example.com")).toBeInTheDocument();
  });

  function rowFor(email: string): HTMLElement {
    return screen.getByText(email).closest("tr") as HTMLElement;
  }

  it("shows the inherited-via group name for a group-derived permission", () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    const bobRow = rowFor("bob@example.com");
    // Bob's server:manage is inherited via "Platform"
    expect(within(bobRow).getByText("Platform")).toBeInTheDocument();
    // It reads as granted, and is not a switch the admin can toggle off.
    const inheritedMark = within(bobRow).getByLabelText(/manage servers.*inherited via platform/i);
    expect(inheritedMark.tagName).toBe("SPAN");
    expect(within(bobRow).queryByRole("checkbox", { name: /manage servers for bob@example.com/i })).toBeNull();
  });

  it("checks the box for a directly-granted permission", () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    // Alice's permission:manage is direct → checked & enabled
    const aliceRow = rowFor("alice@example.com");
    expect(within(aliceRow).getByLabelText(/manage permissions for alice@example.com/i)).toBeChecked();
  });

  it("grants a permission when toggling an unchecked direct box on", async () => {
    const grantFn = vi.fn();
    mockUseGrantPermission.mockReturnValue({ mutate: grantFn, isPending: false, variables: undefined });
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    // Alice's server:manage is absent → unchecked & enabled; toggling it on grants.
    const aliceServerBox = within(rowFor("alice@example.com")).getByLabelText(/manage servers for alice@example.com/i);
    expect(aliceServerBox).not.toBeChecked();
    await userEvent.click(aliceServerBox);
    expect(grantFn).toHaveBeenCalledWith({ userId: "user-1", permission: "server:manage" });
  });

  it("marks a disabled user and offers to enable them", async () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    const bobRow = rowFor("bob@example.com");
    expect(within(bobRow).getByText("disabled")).toBeInTheDocument();
    await userEvent.click(within(bobRow).getByLabelText(/account actions for bob@example.com/i));
    await userEvent.click(await screen.findByText("Enable account"));
    expect(mockEnable).toHaveBeenCalledWith("user-2");
  });

  it("does not offer to disable your own account", async () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    await userEvent.click(within(rowFor("alice@example.com")).getByLabelText(/account actions for alice/i));
    expect(await screen.findByText("Revoke AI tool connections")).toBeInTheDocument();
    expect(screen.queryByText("Disable account")).not.toBeInTheDocument();
    expect(modals.openConfirmModal).not.toHaveBeenCalled();
  });

  it("asks before disabling another user, then disables them", async () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    await userEvent.click(within(rowFor("carol@example.com")).getByLabelText(/account actions for carol/i));
    await userEvent.click(await screen.findByText("Disable account"));

    expect(mockDisable).not.toHaveBeenCalled();
    const confirm = vi.mocked(modals.openConfirmModal).mock.calls[0][0];
    confirm.onConfirm?.();
    expect(mockDisable).toHaveBeenCalledWith("user-3");
  });

  it("revokes a user's AI tool connections", async () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    await userEvent.click(within(rowFor("carol@example.com")).getByLabelText(/account actions for carol/i));
    await userEvent.click(await screen.findByText("Revoke AI tool connections"));
    expect(mockRevokeMcp).toHaveBeenCalledWith("user-3");
  });

  it("confirms before you revoke your own admin permission", async () => {
    const revokeFn = vi.fn();
    mockUseRevokePermission.mockReturnValue({ mutate: revokeFn, isPending: false, variables: undefined });
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });

    await userEvent.click(within(rowFor("alice@example.com")).getByLabelText(/manage permissions for alice@example.com/i));
    const confirm = vi.mocked(modals.openConfirmModal).mock.calls[0][0];
    confirm.onCancel?.();
    await Promise.resolve();
    expect(revokeFn).not.toHaveBeenCalled();

    await userEvent.click(within(rowFor("alice@example.com")).getByLabelText(/manage permissions for alice@example.com/i));
    vi.mocked(modals.openConfirmModal).mock.calls[1][0].onConfirm?.();
    await vi.waitFor(() =>
      expect(revokeFn).toHaveBeenCalledWith({ userId: "user-1", permission: "permission:manage" }));
  });

  it("revokes someone else's permission without asking", async () => {
    const revokeFn = vi.fn();
    mockUseRevokePermission.mockReturnValue({ mutate: revokeFn, isPending: false, variables: undefined });
    mockUseMe.mockReturnValue({ data: { id: "someone-else" } });
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });

    await userEvent.click(within(rowFor("alice@example.com")).getByLabelText(/manage permissions for alice@example.com/i));
    expect(modals.openConfirmModal).not.toHaveBeenCalled();
    expect(revokeFn).toHaveBeenCalledWith({ userId: "user-1", permission: "permission:manage" });
  });

  it("marks only the cell being saved as pending", () => {
    mockUseGrantPermission.mockReturnValue({
      mutate: vi.fn(), isPending: true, variables: { userId: "user-1", permission: "server:manage" },
    });
    mockUseRevokePermission.mockReturnValue({
      mutate: vi.fn(), isPending: true, variables: { userId: "user-2", permission: "server:manage" },
    });
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    expect(screen.getByText("alice@example.com")).toBeInTheDocument();
  });

  it("explains an empty user table", () => {
    mockUseUsers.mockReturnValue({ isLoading: false, data: { users: [] } });
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    expect(screen.getByText(/no users yet/i)).toBeInTheDocument();
  });

  it("only lets permission managers open the page", () => {
    vi.mocked(redirect).mockReturnValue({ to: "/" } as never);
    const beforeLoad = (Route as unknown as {
      beforeLoad: (args: { context: { queryClient: { getQueryData: () => unknown } } }) => void;
    }).beforeLoad;
    const withPermissions = (permissions: Array<string>) => ({
      context: { queryClient: { getQueryData: () => ({ permissions }) } },
    });

    expect(() => beforeLoad(withPermissions(["query:execute"]))).toThrow();
    expect(() => beforeLoad(withPermissions(["permission:manage"]))).not.toThrow();
  });

  it("filters users by the search box", async () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    await userEvent.type(screen.getByPlaceholderText(/filter by email/i), "bob");
    expect(screen.queryByText("alice@example.com")).not.toBeInTheDocument();
    expect(screen.getByText("bob@example.com")).toBeInTheDocument();
  });
});
