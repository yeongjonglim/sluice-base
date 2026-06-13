import {
  Badge,
  Button,
  Checkbox,
  Group,
  Loader,
  Modal,
  NavLink,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { IconDatabase, IconShieldLock, IconUser } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useRef, useState } from "react";
import ByPrincipalTab from "./access/ByPrincipalTab";
import type { AdminDatabaseItem } from "@/api/hooks";
import {
  meQueryOptions,
  useAdminServers,
  useAssignDatabaseRole,
  useAssignUserRole,
  useDatabaseRoles,
  useGrantColumnBypass,
  useMarkSensitiveColumn,
  useRemoveDatabaseRole,
  useRevokeColumnBypass,
  useSchema,
  useSensitiveColumns,
  useUnmarkSensitiveColumn,
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

type AccessSearch = {
  tab?: "principal" | "resource" | "columns";
  segment?: "users" | "groups";
};

export const Route = createFileRoute("/_authed/access")({
  validateSearch: (search: Record<string, unknown>): AccessSearch => ({
    tab: (["principal", "resource", "columns"] as const).find(
      (t) => t === search.tab,
    ),
    segment: (["users", "groups"] as const).find((s) => s === search.segment),
  }),
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: AccessPage,
});

function AccessPage() {
  const { tab, segment } = Route.useSearch();
  const navigate = Route.useNavigate();

  return (
    <Stack gap="md">
      <Title order={2}>Access control</Title>
      <Tabs
        value={tab ?? "principal"}
        onChange={(value) =>
          navigate({
            search: (s) => ({
              ...s,
              tab: (value ?? undefined) as AccessSearch["tab"],
            }),
          })
        }
      >
        <Tabs.List>
          <Tabs.Tab value="principal" leftSection={<IconUser size={14} />}>
            By Principal
          </Tabs.Tab>
          <Tabs.Tab value="resource" leftSection={<IconDatabase size={14} />}>
            By Resource
          </Tabs.Tab>
          <Tabs.Tab value="columns" leftSection={<IconShieldLock size={14} />}>
            Sensitive Columns
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="principal" pt="md">
          <ByPrincipalTab initialSegment={segment ?? "users"} />
        </Tabs.Panel>
        <Tabs.Panel value="resource" pt="md">
          <ByDatabaseTab />
        </Tabs.Panel>
        <Tabs.Panel value="columns" pt="md">
          <SensitiveColumnsTab />
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
                      <Table.Td
                        key={p.value}
                        onClick={() => { if (!roles.isLoading) handleToggle(db.id, p.value, !isChecked(db.id, p.value)); }}
                        style={{ cursor: roles.isLoading ? "default" : "pointer" }}
                      >
                        <div style={{ display: "flex", justifyContent: "center" }}>
                          <Checkbox
                            checked={isChecked(db.id, p.value)}
                            disabled={roles.isLoading}
                            onChange={() => {}}
                            aria-label={`${p.label} on ${db.displayName}`}
                            styles={{ root: { pointerEvents: "none" } }}
                          />
                        </div>
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
                  <Table.Td
                    key={p.value}
                    onClick={() => { if (!roles.isLoading) handleToggle(user.id, p.value, !isChecked(user.id, p.value)); }}
                    style={{ cursor: roles.isLoading ? "default" : "pointer" }}
                  >
                    <div style={{ display: "flex", justifyContent: "center" }}>
                      <Checkbox
                        checked={isChecked(user.id, p.value)}
                        disabled={roles.isLoading}
                        onChange={() => {}}
                        aria-label={`${p.label} for ${user.email ?? user.id}`}
                        styles={{ root: { pointerEvents: "none" } }}
                      />
                    </div>
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

function SensitiveColumnsTab() {
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
            </Text>
            {s.databases.map((d) => (
              <NavLink
                key={d.id}
                label={d.displayName}
                active={selectedDb?.id === d.id}
                onClick={() => setSelectedDb({ ...d, serverName: s.name })}
                pl="lg"
              />
            ))}
          </Stack>
        ))}
      </Stack>
      {selectedDb && <SensitiveColumnsPanel database={selectedDb} />}
    </Group>
  );
}

function SensitiveColumnsPanel({ database }: { database: AdminDatabaseItem & { serverName: string } }) {
  const cols = useSensitiveColumns(database.id);
  const schema = useSchema(database.id);
  const mark = useMarkSensitiveColumn();
  const unmark = useUnmarkSensitiveColumn();
  const grantBypass = useGrantColumnBypass();
  const revokeBypass = useRevokeColumnBypass();
  const users = useUsers();
  const [addOpen, setAddOpen] = useState(false);

  return (
    <Stack gap="md" style={{ flex: 1 }}>
      <Group justify="space-between">
        <Text fw={500}>{database.displayName}</Text>
        <Button size="xs" onClick={() => setAddOpen(true)}>
          Mark column as sensitive
        </Button>
      </Group>

      {cols.isLoading && <Loader size="sm" />}

      {cols.data?.columns.length === 0 && (
        <Text size="sm" c="dimmed">
          No sensitive columns configured.
        </Text>
      )}

      {(cols.data?.columns ?? []).map((col) => (
        <Stack
          key={col.id as string}
          gap={4}
          p="xs"
          style={{ border: "1px solid var(--mantine-color-default-border)", borderRadius: 4 }}
        >
          <Group justify="space-between">
            <Text size="sm" fw={500}>
              {col.schemaName}.{col.tableName}.{col.columnName}
            </Text>
            <Button
              size="xs"
              color="red"
              variant="light"
              onClick={() => unmark.mutate({ databaseId: database.id, sensitiveColumnId: col.id as string })}
            >
              Remove
            </Button>
          </Group>

          {col.bypasses.length > 0 && (
            <Table fz="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>User</Table.Th>
                  <Table.Th>Granted</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {col.bypasses.map((b) => (
                  <Table.Tr key={b.id as string}>
                    <Table.Td>{b.userEmail ?? b.userId}</Table.Td>
                    <Table.Td>{new Date(b.grantedAt).toLocaleDateString()}</Table.Td>
                    <Table.Td>
                      <Button
                        size="xs"
                        variant="subtle"
                        color="red"
                        onClick={() =>
                          revokeBypass.mutate({
                            databaseId: database.id,
                            sensitiveColumnId: col.id as string,
                            userId: b.userId,
                          })
                        }
                      >
                        Revoke
                      </Button>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}

          <Select
            placeholder="Add bypass for user…"
            size="xs"
            data={(users.data?.users ?? []).map((u) => ({ value: u.id, label: u.email ?? u.id }))}
            onChange={(userId) => {
              if (userId) {
                grantBypass.mutate({ databaseId: database.id, sensitiveColumnId: col.id as string, userId });
              }
            }}
            clearable
          />
        </Stack>
      ))}

      <Modal opened={addOpen} onClose={() => setAddOpen(false)} title="Mark column as sensitive">
        <MarkColumnsForm
          schema={schema.data}
          onMark={(schemaName, tableName, columnName) =>
            mark.mutate(
              { databaseId: database.id, schemaName, tableName, columnName },
              { onSuccess: () => setAddOpen(false) },
            )
          }
        />
      </Modal>
    </Stack>
  );
}

function MarkColumnsForm({
  schema,
  onMark,
}: {
  schema: ReturnType<typeof useSchema>["data"];
  onMark: (schema: string, table: string, column: string) => void;
}) {
  const [selectedSchema, setSelectedSchema] = useState<string | null>(null);
  const [selectedTable, setSelectedTable] = useState<string | null>(null);
  const [selectedColumn, setSelectedColumn] = useState<string | null>(null);

  const schemaOptions = (schema?.schemas ?? []).map((s) => ({ value: s.name, label: s.name }));
  const tableOptions = (
    schema?.schemas.find((s) => s.name === selectedSchema)?.tables ?? []
  ).map((t) => ({ value: t.name, label: t.name }));
  const columnOptions = (
    schema?.schemas
      .find((s) => s.name === selectedSchema)
      ?.tables.find((t) => t.name === selectedTable)?.columns ?? []
  ).map((c) => ({ value: c.name, label: c.name }));

  return (
    <Stack gap="sm">
      <Select
        label="Schema"
        placeholder="Pick schema"
        data={schemaOptions}
        value={selectedSchema}
        onChange={(v) => {
          setSelectedSchema(v);
          setSelectedTable(null);
          setSelectedColumn(null);
        }}
      />
      <Select
        label="Table"
        placeholder="Pick table"
        data={tableOptions}
        value={selectedTable}
        disabled={!selectedSchema}
        onChange={(v) => {
          setSelectedTable(v);
          setSelectedColumn(null);
        }}
      />
      <Select
        label="Column"
        placeholder="Pick column"
        data={columnOptions}
        value={selectedColumn}
        disabled={!selectedTable}
        onChange={setSelectedColumn}
      />
      <Button
        disabled={!selectedSchema || !selectedTable || !selectedColumn}
        onClick={() => {
          if (selectedSchema && selectedTable && selectedColumn) {
            onMark(selectedSchema, selectedTable, selectedColumn);
          }
        }}
      >
        Mark as sensitive
      </Button>
    </Stack>
  );
}
