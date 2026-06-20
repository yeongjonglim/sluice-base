import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { PermissionsAdminPage } from "@/routes/_authed/permission.tsx";

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

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useMe: (...args: Array<unknown>) => mockUseMe(...args),
  useUsers: (...args: Array<unknown>) => mockUseUsers(...args),
  usePermissionCatalog: (...args: Array<unknown>) => mockUsePermissionCatalog(...args),
  useGrantPermission: () => mockUseGrantPermission(),
  useRevokePermission: () => mockUseRevokePermission(),
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
      id: "user-2", email: "bob@example.com", name: "Bob", lastLoginAt: null,
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
    // The inherited switch is disabled (not directly granted)
    expect(within(bobRow).getByLabelText("Manage servers")).toBeDisabled();
  });

  it("checks the switch for a directly-granted permission", () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    // Alice's permission:manage is direct → checked & enabled
    const aliceRow = rowFor("alice@example.com");
    expect(within(aliceRow).getByLabelText("Manage permissions")).toBeChecked();
  });

  it("grants a permission when toggling an unchecked direct switch on", async () => {
    const grantFn = vi.fn();
    mockUseGrantPermission.mockReturnValue({ mutate: grantFn, isPending: false, variables: undefined });
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    // Alice's server:manage is absent → unchecked & enabled; toggling it on grants.
    const aliceServerSwitch = within(rowFor("alice@example.com")).getByLabelText("Manage servers");
    expect(aliceServerSwitch).not.toBeChecked();
    await userEvent.click(aliceServerSwitch);
    expect(grantFn).toHaveBeenCalledWith({ userId: "user-1", permission: "server:manage" });
  });

  it("filters users by the search box", async () => {
    render(React.createElement(PermissionsAdminPage), { wrapper: Wrapper });
    await userEvent.type(screen.getByPlaceholderText(/filter by email/i), "bob");
    expect(screen.queryByText("alice@example.com")).not.toBeInTheDocument();
    expect(screen.getByText("bob@example.com")).toBeInTheDocument();
  });
});
