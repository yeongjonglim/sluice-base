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
