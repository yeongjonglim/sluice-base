# Bulk Database Access Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the modal-based one-at-a-time role assignment flow on `/access` with inline checkbox matrices so admins can toggle any assignment with a single click.

**Architecture:** `UserRolePanel` and `DatabaseRolePanel` in `access.tsx` are rewritten as checkbox matrices (rows × permission columns). A local in-flight counter (`useRef`) tracks concurrent mutations; a single "Access updated" toast fires when all settle successfully. The three mutating hooks (`useAssignDatabaseRole`, `useAssignUserRole`, `useRemoveDatabaseRole`) have their per-mutation success toasts removed since the component owns batch notification.

**Tech Stack:** React 19, Mantine 9, TanStack Query v5, Vitest + Testing Library, TypeScript.

---

## File Map

| Action | Path |
|--------|------|
| Modify | `src/frontend/src/api/hooks.ts` |
| Modify | `src/frontend/src/routes/_authed/access.tsx` |
| Create | `src/frontend/src/api/__tests__/role-hooks.test.ts` |
| Create | `src/frontend/src/routes/_authed/__tests__/access-matrix.test.tsx` |

---

### Task 1: Remove per-mutation success toasts from role hooks

The three hooks currently fire `notifications.show()` on every successful assign/remove. The matrix component fires a single batched toast instead, so the hook-level success toasts must be removed. Error toasts stay.

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Create: `src/frontend/src/api/__tests__/role-hooks.test.ts`

- [ ] **Step 1: Write failing tests**

Create `src/frontend/src/api/__tests__/role-hooks.test.ts`:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useAssignDatabaseRole, useAssignUserRole, useRemoveDatabaseRole } from "@/api/hooks";

vi.mock("@/api/client", () => ({
  apiRequest: vi.fn(),
  ApiError: class ApiError extends Error {
    constructor(public status: number, public body: unknown) { super(`API ${status}`); }
  },
}));

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const { apiRequest } = await import("@/api/client");
const { notifications } = await import("@mantine/notifications");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => vi.clearAllMocks());

describe("useAssignDatabaseRole", () => {
  it("does NOT show a success notification on success", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAssignDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", userId: "u-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(notifications.show).not.toHaveBeenCalledWith(
      expect.objectContaining({ title: "Role assigned" }),
    );
  });

  it("shows an error notification on failure", async () => {
    vi.mocked(apiRequest).mockRejectedValue(new Error("network error"));
    const { result } = renderHook(() => useAssignDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", userId: "u-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: "red" }));
  });
});

describe("useAssignUserRole", () => {
  it("does NOT show a success notification on success", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useAssignUserRole(), { wrapper });
    result.current.mutate({ userId: "u-1", databaseId: "db-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(notifications.show).not.toHaveBeenCalledWith(
      expect.objectContaining({ title: "Role assigned" }),
    );
  });
});

describe("useRemoveDatabaseRole", () => {
  it("does NOT show a success notification on success", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveDatabaseRole(), { wrapper });
    result.current.mutate({ databaseId: "db-1", userId: "u-1", permission: "query:execute" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(notifications.show).not.toHaveBeenCalledWith(
      expect.objectContaining({ title: "Role removed" }),
    );
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd src/frontend && npx vitest run src/api/__tests__/role-hooks.test.ts
```

Expected: 3 failures — hooks currently call `notifications.show` with "Role assigned" / "Role removed" on success.

- [ ] **Step 3: Remove success notifications from the three hooks**

In `src/frontend/src/api/hooks.ts`, update `useAssignDatabaseRole` — delete the `notifications.show(...)` line from `onSuccess`, keep everything else:

```typescript
export function useAssignDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ databaseId, userId, permission }: {
      databaseId: string; userId: string; permission: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/role"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/database/${databaseId}/role`, { method: "POST", body: { userId, permission } }),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Assign failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}
```

Update `useAssignUserRole` — same change (remove `notifications.show` from `onSuccess` only):

```typescript
export function useAssignUserRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, databaseId, permission }: {
      userId: string; databaseId: string; permission: string;
    }) =>
      apiRequest<
        paths["/api/admin/user/{userId}/role"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/user/${userId}/role`, { method: "POST", body: { databaseId, permission } }),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Assign failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}
```

Update `useRemoveDatabaseRole` — remove `notifications.show` from `onSuccess` only:

```typescript
export function useRemoveDatabaseRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ databaseId, userId, permission }: {
      databaseId: string; userId: string; permission: string;
    }) =>
      apiRequest(`/api/admin/database/${databaseId}/role/${userId}/${permission}`, { method: "DELETE" }),
    onSuccess: (_data, { databaseId, userId }) => {
      void qc.invalidateQueries({ queryKey: ["admin", "database", databaseId, "role"] });
      void qc.invalidateQueries({ queryKey: ["admin", "user", userId, "role"] });
    },
    onError: (error) => {
      notifications.show({
        title: "Remove failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd src/frontend && npx vitest run src/api/__tests__/role-hooks.test.ts
```

Expected: all 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/hooks.ts src/frontend/src/api/__tests__/role-hooks.test.ts
git commit -m "refactor: remove per-mutation success toasts from role hooks"
```

---

### Task 2: Replace panel components with checkbox matrices

**Files:**
- Create: `src/frontend/src/routes/_authed/__tests__/access-matrix.test.tsx`
- Modify: `src/frontend/src/routes/_authed/access.tsx`

- [ ] **Step 1: Write failing tests for both panels**

Create `src/frontend/src/routes/_authed/__tests__/access-matrix.test.tsx`:

```typescript
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
  useAdminServers: (...args: unknown[]) => mockUseAdminServers(...args),
  useUserRoles: (...args: unknown[]) => mockUseUserRoles(...args),
  useDatabaseRoles: (...args: unknown[]) => mockUseDatabaseRoles(...args),
  useUsers: (...args: unknown[]) => mockUseUsers(...args),
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
    mockAssignUserRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs?.onSuccess?.());
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: false })[0]);
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
    mockRemoveRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs?.onSuccess?.());
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: true })[0]);
    expect(mockRemoveRole).toHaveBeenCalledWith(
      expect.objectContaining({ databaseId: "db-1", userId: "user-1", permission: "query:execute" }),
      expect.any(Object),
    );
  });

  it("shows Access updated notification after mutation settles", async () => {
    const { notifications } = await import("@mantine/notifications");
    mockAssignUserRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs?.onSuccess?.());
    render(React.createElement(UserRolePanel, { user: testUser }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: false })[0]);
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
    mockAssignDatabaseRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs?.onSuccess?.());
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: false })[0]);
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
    mockRemoveRole.mockImplementation((_v: unknown, cbs: { onSuccess?: () => void }) => cbs?.onSuccess?.());
    render(React.createElement(DatabaseRolePanel, { database: testDatabase }), { wrapper: Wrapper });
    await userEvent.click(screen.getAllByRole("checkbox", { checked: true })[0]);
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
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/access-matrix.test.tsx
```

Expected: import of `UserRolePanel` and `DatabaseRolePanel` fails — they are not yet exported from `access.tsx`.

- [ ] **Step 3: Replace access.tsx with matrix implementation**

Overwrite `src/frontend/src/routes/_authed/access.tsx` entirely:

```typescript
import {
  Badge,
  Button,
  Checkbox,
  Group,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { IconDatabase, IconUser } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useRef, useState } from "react";
import type { AdminDatabaseItem } from "@/api/hooks";
import {
  meQueryOptions,
  useAdminServers,
  useAssignDatabaseRole,
  useAssignUserRole,
  useDatabaseRoles,
  useRemoveDatabaseRole,
  useUserRoles,
  useUsers,
} from "@/api/hooks";

const SCOPEABLE_PERMISSIONS = [
  { value: "query:execute", label: "Query Execute" },
  { value: "query:audit", label: "Query Audit" },
  { value: "update:submit", label: "Update Submit" },
  { value: "update:approve", label: "Update Approve" },
  { value: "update:execute", label: "Update Execute" },
];

export const Route = createFileRoute("/_authed/access")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: AccessPage,
});

function AccessPage() {
  return (
    <Stack gap="md">
      <Title order={2}>Access control</Title>
      <Tabs defaultValue="database">
        <Tabs.List>
          <Tabs.Tab value="database" leftSection={<IconDatabase size={14} />}>
            By Database
          </Tabs.Tab>
          <Tabs.Tab value="user" leftSection={<IconUser size={14} />}>
            By User
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="database" pt="md">
          <ByDatabaseTab />
        </Tabs.Panel>
        <Tabs.Panel value="user" pt="md">
          <ByUserTab />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}

function ByDatabaseTab() {
  const servers = useAdminServers();
  const [selectedDb, setSelectedDb] = useState<(AdminDatabaseItem & { serverName: string }) | null>(null);

  return (
    <Group align="flex-start" gap="md">
      <Stack gap={4} style={{ minWidth: 220, maxWidth: 260 }}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">Databases</Text>
        {(servers.data?.servers ?? []).map((s) => (
          <Stack key={s.id} gap={2}>
            <Text size="xs" c="dimmed" fw={500} pl={4}>
              {s.name}
              {s.isDisabled && <Badge size="xs" color="gray" ml={4}>disabled</Badge>}
            </Text>
            {s.databases.map((d) => (
              <Button
                key={d.id}
                variant={selectedDb?.id === d.id ? "filled" : "subtle"}
                size="xs"
                justify="left"
                leftSection={<IconDatabase size={12} />}
                onClick={() => setSelectedDb({ ...d, serverName: s.name })}
                disabled={d.isDisabled}
                style={{ opacity: d.isDisabled ? 0.5 : 1 }}
              >
                {d.displayName}
              </Button>
            ))}
          </Stack>
        ))}
      </Stack>
      <Stack flex={1} gap="md">
        {selectedDb ? (
          <DatabaseRolePanel key={selectedDb.id} database={selectedDb} />
        ) : (
          <Text c="dimmed" size="sm">Select a database to manage its access assignments.</Text>
        )}
      </Stack>
    </Group>
  );
}

function ByUserTab() {
  const users = useUsers();
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const selectedUser = (users.data?.users ?? []).find((u) => u.id === selectedUserId);

  return (
    <Group align="flex-start" gap="md">
      <Stack gap={4} style={{ minWidth: 220, maxWidth: 280 }}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">Users</Text>
        {(users.data?.users ?? []).map((u) => (
          <Button
            key={u.id}
            variant={selectedUserId === u.id ? "filled" : "subtle"}
            size="xs"
            justify="left"
            leftSection={<IconUser size={12} />}
            onClick={() => setSelectedUserId(u.id)}
          >
            {u.email ?? u.name ?? u.id}
          </Button>
        ))}
      </Stack>
      <Stack flex={1} gap="md">
        {selectedUser ? (
          <UserRolePanel key={selectedUser.id} user={selectedUser} />
        ) : (
          <Text c="dimmed" size="sm">Select a user to manage their database access.</Text>
        )}
      </Stack>
    </Group>
  );
}

export function UserRolePanel({
  user,
}: {
  user: { id: string; email?: string | null; name?: string | null };
}) {
  const roles = useUserRoles(user.id);
  const servers = useAdminServers();
  const assign = useAssignUserRole();
  const remove = useRemoveDatabaseRole();
  const pendingRef = useRef(0);
  const hadErrorRef = useRef(false);

  function isChecked(databaseId: string, permission: string): boolean {
    return (roles.data?.roles ?? []).some(
      (r) => r.databaseId === databaseId && r.permission === permission,
    );
  }

  function handleToggle(databaseId: string, permission: string, checked: boolean) {
    if (pendingRef.current === 0) hadErrorRef.current = false;
    pendingRef.current += 1;

    const onSettled = () => {
      pendingRef.current -= 1;
      if (pendingRef.current === 0 && !hadErrorRef.current) {
        notifications.show({ title: "Access updated", message: "", color: "teal" });
      }
    };

    if (checked) {
      assign.mutate(
        { userId: user.id, databaseId, permission },
        { onSuccess: onSettled, onError: () => { hadErrorRef.current = true; onSettled(); } },
      );
    } else {
      remove.mutate(
        { databaseId, userId: user.id, permission },
        { onSuccess: onSettled, onError: () => { hadErrorRef.current = true; onSettled(); } },
      );
    }
  }

  return (
    <Stack gap="sm">
      <Stack gap={0}>
        <Text fw={600}>{user.email ?? user.id}</Text>
        {user.name && <Text size="xs" c="dimmed">{user.name}</Text>}
      </Stack>
      <Table.ScrollContainer minWidth={500}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Database</Table.Th>
              {SCOPEABLE_PERMISSIONS.map((p) => (
                <Table.Th key={p.value} style={{ textAlign: "center", whiteSpace: "nowrap" }}>
                  {p.label}
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(servers.data?.servers ?? []).flatMap((server) => [
              <Table.Tr key={`server-${server.id}`}>
                <Table.Td
                  colSpan={SCOPEABLE_PERMISSIONS.length + 1}
                  style={{ fontWeight: 600, fontSize: 11, textTransform: "uppercase",
                    color: "var(--mantine-color-dimmed)", paddingTop: 12, paddingBottom: 4 }}
                >
                  {server.name}
                </Table.Td>
              </Table.Tr>,
              ...server.databases
                .filter((d) => !d.isDisabled)
                .map((db) => (
                  <Table.Tr key={db.id}>
                    <Table.Td><Text size="sm">{db.displayName}</Text></Table.Td>
                    {SCOPEABLE_PERMISSIONS.map((p) => (
                      <Table.Td key={p.value} style={{ textAlign: "center" }}>
                        <Checkbox
                          checked={isChecked(db.id, p.value)}
                          disabled={roles.isLoading}
                          onChange={(e) => handleToggle(db.id, p.value, e.currentTarget.checked)}
                          aria-label={`${p.label} on ${db.displayName}`}
                        />
                      </Table.Td>
                    ))}
                  </Table.Tr>
                )),
            ])}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Stack>
  );
}

export function DatabaseRolePanel({
  database,
}: {
  database: AdminDatabaseItem & { serverName: string };
}) {
  const roles = useDatabaseRoles(database.id);
  const users = useUsers();
  const assign = useAssignDatabaseRole();
  const remove = useRemoveDatabaseRole();
  const pendingRef = useRef(0);
  const hadErrorRef = useRef(false);

  function isChecked(userId: string, permission: string): boolean {
    return (roles.data?.roles ?? []).some(
      (r) => r.userId === userId && r.permission === permission,
    );
  }

  function handleToggle(userId: string, permission: string, checked: boolean) {
    if (pendingRef.current === 0) hadErrorRef.current = false;
    pendingRef.current += 1;

    const onSettled = () => {
      pendingRef.current -= 1;
      if (pendingRef.current === 0 && !hadErrorRef.current) {
        notifications.show({ title: "Access updated", message: "", color: "teal" });
      }
    };

    if (checked) {
      assign.mutate(
        { databaseId: database.id, userId, permission },
        { onSuccess: onSettled, onError: () => { hadErrorRef.current = true; onSettled(); } },
      );
    } else {
      remove.mutate(
        { databaseId: database.id, userId, permission },
        { onSuccess: onSettled, onError: () => { hadErrorRef.current = true; onSettled(); } },
      );
    }
  }

  return (
    <Stack gap="sm">
      <Stack gap={0}>
        <Text fw={600}>{database.displayName}</Text>
        <Text size="xs" c="dimmed">{database.serverName}</Text>
      </Stack>
      <Table.ScrollContainer minWidth={500}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              {SCOPEABLE_PERMISSIONS.map((p) => (
                <Table.Th key={p.value} style={{ textAlign: "center", whiteSpace: "nowrap" }}>
                  {p.label}
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(users.data?.users ?? []).map((user) => (
              <Table.Tr key={user.id}>
                <Table.Td>
                  <Text size="sm">{user.email ?? user.id}</Text>
                  {user.name && <Text size="xs" c="dimmed">{user.name}</Text>}
                </Table.Td>
                {SCOPEABLE_PERMISSIONS.map((p) => (
                  <Table.Td key={p.value} style={{ textAlign: "center" }}>
                    <Checkbox
                      checked={isChecked(user.id, p.value)}
                      disabled={roles.isLoading}
                      onChange={(e) => handleToggle(user.id, p.value, e.currentTarget.checked)}
                      aria-label={`${p.label} for ${user.email ?? user.id}`}
                    />
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
            {(users.data?.users ?? []).length === 0 && !users.isLoading && (
              <Table.Tr>
                <Table.Td colSpan={SCOPEABLE_PERMISSIONS.length + 1}>
                  <Text size="sm" c="dimmed">No users yet.</Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Stack>
  );
}
```

- [ ] **Step 4: Run panel tests to confirm they pass**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/access-matrix.test.tsx
```

Expected: all 10 tests pass.

- [ ] **Step 5: Run the full test suite to check for regressions**

```bash
cd src/frontend && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/routes/_authed/access.tsx src/frontend/src/routes/_authed/__tests__/access-matrix.test.tsx
git commit -m "feat: replace modal-based role assignment with checkbox matrix on access page"
```
