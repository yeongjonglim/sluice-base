import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconDatabase, IconPlus, IconTrash, IconUser } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import {
  meQueryOptions,
  useAdminServers,
  useAssignDatabaseRole,
  useAssignUserRole,
  useDatabaseRoles,
  useRemoveDatabaseRole,
  useUserRoles,
  useUsers,
  type AdminDatabaseItem,
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
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">
          Databases
        </Text>
        {(servers.data?.servers ?? []).map((s) => (
          <Stack key={s.id} gap={2}>
            <Text size="xs" c="dimmed" fw={500} pl={4}>
              {s.name}
              {s.isDisabled && (
                <Badge size="xs" color="gray" ml={4}>
                  disabled
                </Badge>
              )}
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
          <DatabaseRolePanel database={selectedDb} />
        ) : (
          <Text c="dimmed" size="sm">
            Select a database to manage its access assignments.
          </Text>
        )}
      </Stack>
    </Group>
  );
}

function DatabaseRolePanel({
  database,
}: {
  database: AdminDatabaseItem & { serverName: string };
}) {
  const roles = useDatabaseRoles(database.id);
  const users = useUsers();
  const remove = useRemoveDatabaseRole();
  const assign = useAssignDatabaseRole();
  const [addOpen, { open: openAdd, close: closeAdd }] = useDisclosure(false);
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [selectedPermission, setSelectedPermission] = useState<string | null>(null);

  const userOptions = (users.data?.users ?? []).map((u) => ({
    value: u.id,
    label: u.email ?? u.name ?? u.id,
  }));

  function handleAdd() {
    if (!selectedUserId || !selectedPermission) return;
    assign.mutate(
      { databaseId: database.id, userId: selectedUserId, permission: selectedPermission },
      {
        onSuccess: () => {
          closeAdd();
          setSelectedUserId(null);
          setSelectedPermission(null);
        },
      },
    );
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Stack gap={0}>
          <Text fw={600}>{database.displayName}</Text>
          <Text size="xs" c="dimmed">
            {database.serverName}
          </Text>
        </Stack>
        <Button size="xs" leftSection={<IconPlus size={14} />} onClick={openAdd}>
          Add assignment
        </Button>
      </Group>

      <Table.ScrollContainer minWidth={500}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              <Table.Th>Permission</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(roles.data?.roles ?? []).map((r) => (
              <Table.Tr key={r.id}>
                <Table.Td>
                  <Text size="sm">{r.userEmail ?? r.userId}</Text>
                  {r.userName && (
                    <Text size="xs" c="dimmed">
                      {r.userName}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <Badge variant="light" size="sm">
                    {r.permission}
                  </Badge>
                </Table.Td>
                <Table.Td>
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    size="sm"
                    onClick={() =>
                      remove.mutate({
                        databaseId: database.id,
                        userId: r.userId,
                        permission: r.permission,
                      })
                    }
                    aria-label="Remove assignment"
                  >
                    <IconTrash size={14} />
                  </ActionIcon>
                </Table.Td>
              </Table.Tr>
            ))}
            {(roles.data?.roles ?? []).length === 0 && !roles.isLoading && (
              <Table.Tr>
                <Table.Td colSpan={3}>
                  <Text size="sm" c="dimmed">
                    No assignments yet.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal opened={addOpen} onClose={closeAdd} title="Add assignment">
        <Stack gap="sm">
          <Select
            label="User"
            placeholder="Select user…"
            data={userOptions}
            value={selectedUserId}
            onChange={setSelectedUserId}
            searchable
          />
          <Select
            label="Permission"
            placeholder="Select permission…"
            data={SCOPEABLE_PERMISSIONS}
            value={selectedPermission}
            onChange={setSelectedPermission}
          />
          <Button
            onClick={handleAdd}
            loading={assign.isPending}
            disabled={!selectedUserId || !selectedPermission}
          >
            Assign
          </Button>
        </Stack>
      </Modal>
    </Stack>
  );
}

function ByUserTab() {
  const users = useUsers();
  const servers = useAdminServers();
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);

  const selectedUser = (users.data?.users ?? []).find((u) => u.id === selectedUserId);

  const allDatabases = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases.map((d) => ({
      value: d.id,
      label: `${s.name} / ${d.displayName}`,
    })),
  );

  return (
    <Group align="flex-start" gap="md">
      <Stack gap={4} style={{ minWidth: 220, maxWidth: 280 }}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">
          Users
        </Text>
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
          <UserRolePanel user={selectedUser} databaseOptions={allDatabases} />
        ) : (
          <Text c="dimmed" size="sm">
            Select a user to manage their database access.
          </Text>
        )}
      </Stack>
    </Group>
  );
}

function UserRolePanel({
  user,
  databaseOptions,
}: {
  user: { id: string; email?: string | null; name?: string | null };
  databaseOptions: { value: string; label: string }[];
}) {
  const roles = useUserRoles(user.id);
  const remove = useRemoveDatabaseRole();
  const assign = useAssignUserRole();
  const [addOpen, { open: openAdd, close: closeAdd }] = useDisclosure(false);
  const [selectedDbId, setSelectedDbId] = useState<string | null>(null);
  const [selectedPermission, setSelectedPermission] = useState<string | null>(null);

  function handleAdd() {
    if (!selectedDbId || !selectedPermission) return;
    assign.mutate(
      { userId: user.id, databaseId: selectedDbId, permission: selectedPermission },
      {
        onSuccess: () => {
          closeAdd();
          setSelectedDbId(null);
          setSelectedPermission(null);
        },
      },
    );
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Stack gap={0}>
          <Text fw={600}>{user.email ?? user.id}</Text>
          {user.name && (
            <Text size="xs" c="dimmed">
              {user.name}
            </Text>
          )}
        </Stack>
        <Button size="xs" leftSection={<IconPlus size={14} />} onClick={openAdd}>
          Add assignment
        </Button>
      </Group>

      <Table.ScrollContainer minWidth={500}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Database</Table.Th>
              <Table.Th>Permission</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(roles.data?.roles ?? []).map((r) => (
              <Table.Tr key={r.id}>
                <Table.Td>
                  <Text size="sm">{r.databaseDisplayName}</Text>
                  <Text size="xs" c="dimmed">
                    {r.serverName}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Badge variant="light" size="sm">
                    {r.permission}
                  </Badge>
                </Table.Td>
                <Table.Td>
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    size="sm"
                    onClick={() =>
                      remove.mutate({
                        databaseId: r.databaseId,
                        userId: user.id,
                        permission: r.permission,
                      })
                    }
                    aria-label="Remove assignment"
                  >
                    <IconTrash size={14} />
                  </ActionIcon>
                </Table.Td>
              </Table.Tr>
            ))}
            {(roles.data?.roles ?? []).length === 0 && !roles.isLoading && (
              <Table.Tr>
                <Table.Td colSpan={3}>
                  <Text size="sm" c="dimmed">
                    No assignments yet.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal opened={addOpen} onClose={closeAdd} title="Add assignment">
        <Stack gap="sm">
          <Select
            label="Database"
            placeholder="Select database…"
            data={databaseOptions}
            value={selectedDbId}
            onChange={setSelectedDbId}
            searchable
          />
          <Select
            label="Permission"
            placeholder="Select permission…"
            data={SCOPEABLE_PERMISSIONS}
            value={selectedPermission}
            onChange={setSelectedPermission}
          />
          <Button
            onClick={handleAdd}
            loading={assign.isPending}
            disabled={!selectedDbId || !selectedPermission}
          >
            Assign
          </Button>
        </Stack>
      </Modal>
    </Stack>
  );
}
