import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  NumberInput,
  Paper,
  PasswordInput,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconChevronDown,
  IconChevronUp,
  IconDatabase,
  IconKey,
  IconPencil,
  IconPlayerPlay,
  IconPlus,
  IconServer,
  IconTrash,
} from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import React, { useState } from "react";
import type { ServerItem, TestConnectionResponse } from "@/api/hooks";
import {
  meQueryOptions,
  useCreateCredential,
  useCreateDatabase,
  useCreateServer,
  useDeleteCredential,
  useDeleteDatabase,
  useDeleteServer,
  useServers,
  useTestDatabaseConnection,
  useUpdateCredential,
  useUpdateDatabase,
  useUpdateServer,
} from "@/api/hooks";

type CredentialItem = ServerItem["credentials"][0];
type DatabaseItem = ServerItem["databases"][0];

export const Route = createFileRoute("/_authed/server")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("server:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: ServerPage,
});

function ServerPage() {
  const servers = useServers();
  const [modalOpen, { open: openModal, close: closeModal }] = useDisclosure(false);
  const [editing, setEditing] = useState<ServerItem | null>(null);

  function handleAdd() {
    setEditing(null);
    openModal();
  }

  function handleEdit(server: ServerItem) {
    setEditing(server);
    openModal();
  }

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={2}>Server management</Title>
        <Button leftSection={<IconServer size={16} />} onClick={handleAdd}>
          Add server
        </Button>
      </Group>

      <Table.ScrollContainer minWidth={700}>
        <Table stickyHeader striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Kind</Table.Th>
              <Table.Th>Host:Port</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th />
              <Table.Th>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(servers.data?.servers ?? []).map((s) => (
              <ServerRow key={s.id} server={s} onEdit={() => handleEdit(s)} />
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal
        opened={modalOpen}
        onClose={closeModal}
        title={editing ? "Edit server" : "Add server"}
        size="md"
      >
        <ServerForm server={editing} onSuccess={closeModal} />
      </Modal>
    </Stack>
  );
}

function ServerRow({ server, onEdit }: { server: ServerItem; onEdit: () => void }) {
  const deleteServer = useDeleteServer();
  const [expanded, setExpanded] = useState(false);
  const createCred = useCreateCredential(server.id);
  const updateCred = useUpdateCredential(server.id);
  const deleteCred = useDeleteCredential(server.id);
  const createDb = useCreateDatabase(server.id);
  const updateDb = useUpdateDatabase(server.id);
  const deleteDb = useDeleteDatabase(server.id);
  const testConn = useTestDatabaseConnection(server.id);

  const [credModalOpen, { open: openCredModal, close: closeCredModal }] = useDisclosure(false);
  const [editingCred, setEditingCred] = useState<CredentialItem | null>(null);
  const [dbModalOpen, { open: openDbModal, close: closeDbModal }] = useDisclosure(false);
  const [editingDb, setEditingDb] = useState<DatabaseItem | null>(null);
  const [testResults, setTestResults] = useState<Partial<Record<string, TestConnectionResponse>>>({});

  function handleAddCred() {
    setEditingCred(null);
    openCredModal();
  }

  function handleEditCred(cred: CredentialItem) {
    setEditingCred(cred);
    openCredModal();
  }

  function handleAddDb() {
    setEditingDb(null);
    openDbModal();
  }

  function handleEditDb(db: DatabaseItem) {
    setEditingDb(db);
    openDbModal();
  }

  async function handleTestConn(databaseId: string) {
    const result = await testConn.mutateAsync(databaseId);
    setTestResults((prev) => ({ ...prev, [databaseId]: result }));
  }

  return (
    <>
      <Table.Tr>
        <Table.Td>{server.name}</Table.Td>
        <Table.Td>
          <Badge variant="light">{server.kind}</Badge>
        </Table.Td>
        <Table.Td>
          <Text size="sm" ff="monospace">
            {server.host}:{server.port}
          </Text>
        </Table.Td>
        <Table.Td>
          {server.isDisabled && (
            <Badge color="gray" variant="light">
              Disabled
            </Badge>
          )}
        </Table.Td>
        <Table.Td>
          <ActionIcon
            variant="subtle"
            onClick={() => setExpanded((v) => !v)}
            title={expanded ? "Collapse" : "Expand"}
          >
            {expanded ? <IconChevronUp size={16} /> : <IconChevronDown size={16} />}
          </ActionIcon>
        </Table.Td>
        <Table.Td>
          <Group gap="xs">
            <ActionIcon variant="subtle" onClick={onEdit} title="Edit">
              <IconPencil size={16} />
            </ActionIcon>
            <ActionIcon
              variant="subtle"
              color="red"
              loading={deleteServer.isPending}
              onClick={() => deleteServer.mutate(server.id)}
              title="Delete"
            >
              <IconTrash size={16} />
            </ActionIcon>
          </Group>
        </Table.Td>
      </Table.Tr>

      {expanded && (
        <Table.Tr>
          <Table.Td colSpan={6} p="md">
            <Stack gap="md">
              {/* Credentials sub-table */}
              <Paper withBorder p="sm">
                <Group justify="space-between" mb="xs">
                  <Group gap="xs">
                    <IconKey size={16} />
                    <Text fw={600} size="sm">
                      Credentials
                    </Text>
                  </Group>
                  <Button
                    size="xs"
                    variant="subtle"
                    leftSection={<IconPlus size={14} />}
                    onClick={handleAddCred}
                  >
                    Add credential
                  </Button>
                </Group>

                {server.credentials.length === 0 ? (
                  <Text size="sm" c="dimmed">
                    No credentials configured
                  </Text>
                ) : (
                  <Table fz="sm">
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Label</Table.Th>
                        <Table.Th>Username</Table.Th>
                        <Table.Th>Actions</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {server.credentials.map((cred) => {
                        const isReferenced = server.databases.some(
                          (d) =>
                            !d.isDisabled &&
                            (d.readCredentialId === cred.id ||
                              d.writeCredentialId === cred.id),
                        );
                        return (
                          <Table.Tr key={cred.id}>
                            <Table.Td>{cred.label}</Table.Td>
                            <Table.Td>
                              <Text size="sm" ff="monospace">
                                {cred.username}
                              </Text>
                            </Table.Td>
                            <Table.Td>
                              <Group gap="xs">
                                <ActionIcon
                                  variant="subtle"
                                  onClick={() => handleEditCred(cred)}
                                  title="Edit credential"
                                >
                                  <IconPencil size={14} />
                                </ActionIcon>
                                <Tooltip
                                  label="Referenced by an active database"
                                  disabled={!isReferenced}
                                >
                                  <ActionIcon
                                    variant="subtle"
                                    color="red"
                                    disabled={isReferenced}
                                    loading={deleteCred.isPending}
                                    onClick={() => deleteCred.mutate(cred.id)}
                                    title="Delete credential"
                                  >
                                    <IconTrash size={14} />
                                  </ActionIcon>
                                </Tooltip>
                              </Group>
                            </Table.Td>
                          </Table.Tr>
                        );
                      })}
                    </Table.Tbody>
                  </Table>
                )}
              </Paper>

              {/* Databases sub-table */}
              <Paper withBorder p="sm">
                <Group justify="space-between" mb="xs">
                  <Group gap="xs">
                    <IconDatabase size={16} />
                    <Text fw={600} size="sm">
                      Databases
                    </Text>
                  </Group>
                  <Button
                    size="xs"
                    variant="subtle"
                    leftSection={<IconPlus size={14} />}
                    onClick={handleAddDb}
                  >
                    Add database
                  </Button>
                </Group>

                {server.databases.length === 0 ? (
                  <Text size="sm" c="dimmed">
                    No databases configured
                  </Text>
                ) : (
                  <Table fz="sm">
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Display name</Table.Th>
                        <Table.Th>DB name</Table.Th>
                        <Table.Th>Read cred</Table.Th>
                        <Table.Th>Write cred</Table.Th>
                        <Table.Th>Status</Table.Th>
                        <Table.Th>Actions</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {server.databases.map((db) => {
                        const readCred = server.credentials.find(
                          (c) => c.id === db.readCredentialId,
                        );
                        const writeCred = db.writeCredentialId
                          ? server.credentials.find((c) => c.id === db.writeCredentialId)
                          : null;
                        const testResult = testResults[db.id];
                        return (
                          <React.Fragment key={db.id}>
                            <Table.Tr>
                              <Table.Td>{db.displayName}</Table.Td>
                              <Table.Td>
                                <Text size="sm" ff="monospace">
                                  {db.databaseName}
                                </Text>
                              </Table.Td>
                              <Table.Td>{readCred?.label ?? db.readCredentialId}</Table.Td>
                              <Table.Td>
                                {writeCred?.label ?? (db.writeCredentialId ? db.writeCredentialId : "—")}
                              </Table.Td>
                              <Table.Td>
                                {db.isDisabled && (
                                  <Badge color="gray" variant="light" size="sm">
                                    Disabled
                                  </Badge>
                                )}
                              </Table.Td>
                              <Table.Td>
                                <Group gap="xs">
                                  <ActionIcon
                                    variant="subtle"
                                    loading={testConn.isPending}
                                    onClick={() => void handleTestConn(db.id)}
                                    title="Test connection"
                                  >
                                    <IconPlayerPlay size={14} />
                                  </ActionIcon>
                                  <ActionIcon
                                    variant="subtle"
                                    onClick={() => handleEditDb(db)}
                                    title="Edit database"
                                  >
                                    <IconPencil size={14} />
                                  </ActionIcon>
                                  <ActionIcon
                                    variant="subtle"
                                    color="red"
                                    loading={deleteDb.isPending}
                                    onClick={() => deleteDb.mutate(db.id)}
                                    title="Delete database"
                                  >
                                    <IconTrash size={14} />
                                  </ActionIcon>
                                </Group>
                              </Table.Td>
                            </Table.Tr>
                            {testResult && (
                              <Table.Tr>
                                <Table.Td colSpan={6}>
                                  <Group gap="xs">
                                    <ConnBadge label="Read" result={testResult.read} />
                                    {testResult.write && (
                                      <ConnBadge label="Write" result={testResult.write} />
                                    )}
                                  </Group>
                                </Table.Td>
                              </Table.Tr>
                            )}
                          </React.Fragment>
                        );
                      })}
                    </Table.Tbody>
                  </Table>
                )}
              </Paper>
            </Stack>
          </Table.Td>
        </Table.Tr>
      )}

      <Modal
        opened={credModalOpen}
        onClose={closeCredModal}
        title={editingCred ? "Edit credential" : "Add credential"}
        size="sm"
      >
        <CredentialForm
          credential={editingCred}
          createMutation={createCred}
          updateMutation={updateCred}
          onSuccess={closeCredModal}
        />
      </Modal>

      <Modal
        opened={dbModalOpen}
        onClose={closeDbModal}
        title={editingDb ? "Edit database" : "Add database"}
        size="md"
      >
        <DatabaseForm
          database={editingDb}
          credentials={server.credentials}
          createMutation={createDb}
          updateMutation={updateDb}
          onSuccess={closeDbModal}
        />
      </Modal>
    </>
  );
}

function ConnBadge({
  label,
  result,
}: {
  label: string;
  result: { ok: boolean; error?: string | null };
}) {
  const badge = (
    <Badge color={result.ok ? "teal" : "red"} variant="light">
      {label}: {result.ok ? "Connected" : "Failed"}
    </Badge>
  );
  if (!result.ok && result.error) {
    return <Tooltip label={result.error}>{badge}</Tooltip>;
  }
  return badge;
}

function ServerForm({ server, onSuccess }: { server: ServerItem | null; onSuccess: () => void }) {
  const createServer = useCreateServer();
  const updateServer = useUpdateServer();
  const isEditing = server !== null;

  const [name, setName] = useState(server?.name ?? "");
  const [kind, setKind] = useState(server?.kind ?? "postgres");
  const [host, setHost] = useState(server?.host ?? "");
  const [port, setPort] = useState<string | number>(server?.port ?? 5432);
  const [connectionMode, setConnectionMode] = useState(
    server?.connectionMode ?? "Standard",
  );
  const [authSource, setAuthSource] = useState(server?.authSource ?? "");
  const [replicaSet, setReplicaSet] = useState(server?.replicaSet ?? "");
  const [useTls, setUseTls] = useState(server?.useTls ?? false);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const mongoOptions =
      kind === "mongodb"
        ? {
            connectionMode,
            authSource: authSource || null,
            replicaSet: replicaSet || null,
            useTls,
          }
        : { useTls: false };

    if (isEditing) {
      await updateServer.mutateAsync({
        id: server.id,
        body: { name, host, port, kind, isDisabled: server.isDisabled, ...mongoOptions },
      });
    } else {
      await createServer.mutateAsync({ name, kind, host, port, ...mongoOptions });
    }
    onSuccess();
  }

  const isPending = createServer.isPending || updateServer.isPending;

  return (
    <form onSubmit={(e) => void handleSubmit(e)}>
      <Stack gap="sm">
        <TextInput
          label="Name"
          required
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <Select
          label="Kind"
          required
          value={kind}
          onChange={(v) => setKind(v ?? "postgres")}
          data={[
            { value: "postgres", label: "PostgreSQL" },
            { value: "mongodb", label: "MongoDB" },
          ]}
        />
        <Group grow>
          <TextInput
            label="Host"
            required
            value={host}
            onChange={(e) => setHost(e.currentTarget.value)}
          />
          <NumberInput
            label="Port"
            required
            value={port}
            onChange={(v) => setPort(Number(v))}
            min={1}
            max={65535}
            disabled={kind === "mongodb" && connectionMode === "Srv"}
          />
        </Group>
        {kind === "mongodb" && (
          <>
            <Select
              label="Connection mode"
              value={connectionMode}
              onChange={(v) => setConnectionMode(v ?? "Standard")}
              data={[
                { value: "Standard", label: "Standard (host:port)" },
                { value: "Srv", label: "SRV (mongodb+srv DNS name)" },
              ]}
            />
            <TextInput
              label="Auth source"
              placeholder="admin"
              value={authSource}
              onChange={(e) => setAuthSource(e.currentTarget.value)}
            />
            <TextInput
              label="Replica set"
              value={replicaSet}
              onChange={(e) => setReplicaSet(e.currentTarget.value)}
            />
            <Switch
              label="Use TLS"
              checked={useTls}
              onChange={(e) => setUseTls(e.currentTarget.checked)}
            />
          </>
        )}
        <Group justify="flex-end" mt="md">
          <Button type="submit" loading={isPending}>
            {isEditing ? "Save changes" : "Add server"}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function CredentialForm({
  credential,
  createMutation,
  updateMutation,
  onSuccess,
}: {
  credential: CredentialItem | null;
  createMutation: ReturnType<typeof useCreateCredential>;
  updateMutation: ReturnType<typeof useUpdateCredential>;
  onSuccess: () => void;
}) {
  const isEditing = credential !== null;

  const [label, setLabel] = useState(credential?.label ?? "");
  const [username, setUsername] = useState(credential?.username ?? "");
  const [password, setPassword] = useState("");

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (isEditing) {
      await updateMutation.mutateAsync({
        credentialId: credential.id,
        label,
        username,
        password: password || null,
      });
    } else {
      await createMutation.mutateAsync({ label, username, password });
    }
    onSuccess();
  }

  const isPending = createMutation.isPending || updateMutation.isPending;

  return (
    <form onSubmit={(e) => void handleSubmit(e)}>
      <Stack gap="sm">
        <TextInput
          label="Label"
          required
          placeholder="e.g. Read-only role"
          value={label}
          onChange={(e) => setLabel(e.currentTarget.value)}
        />
        <TextInput
          label="Username"
          required
          value={username}
          onChange={(e) => setUsername(e.currentTarget.value)}
        />
        <PasswordInput
          label="Password"
          required={!isEditing}
          placeholder={isEditing ? "Leave blank to keep existing" : "Enter password"}
          value={password}
          onChange={(e) => setPassword(e.currentTarget.value)}
        />
        <Group justify="flex-end" mt="md">
          <Button type="submit" loading={isPending}>
            {isEditing ? "Save changes" : "Add credential"}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function DatabaseForm({
  database,
  credentials,
  createMutation,
  updateMutation,
  onSuccess,
}: {
  database: DatabaseItem | null;
  credentials: Array<CredentialItem>;
  createMutation: ReturnType<typeof useCreateDatabase>;
  updateMutation: ReturnType<typeof useUpdateDatabase>;
  onSuccess: () => void;
}) {
  const isEditing = database !== null;

  const [displayName, setDisplayName] = useState(database?.displayName ?? "");
  const [databaseName, setDatabaseName] = useState(database?.databaseName ?? "");
  const [readCredentialId, setReadCredentialId] = useState<string | null>(
    database?.readCredentialId ?? null,
  );
  const [writeCredentialId, setWriteCredentialId] = useState<string | null>(
    database?.writeCredentialId ?? null,
  );
  const [isDisabled, setIsDisabled] = useState(database?.isDisabled ?? false);

  const credentialOptions = credentials.map((c) => ({ value: c.id, label: c.label }));

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!readCredentialId) return;
    if (isEditing) {
      await updateMutation.mutateAsync({
        databaseId: database.id,
        displayName,
        databaseName,
        readCredentialId,
        writeCredentialId,
        isDisabled,
      });
    } else {
      await createMutation.mutateAsync({
        displayName,
        databaseName,
        readCredentialId,
        writeCredentialId,
      });
    }
    onSuccess();
  }

  const isPending = createMutation.isPending || updateMutation.isPending;

  return (
    <form onSubmit={(e) => void handleSubmit(e)}>
      <Stack gap="sm">
        <TextInput
          label="Display name"
          required
          placeholder="e.g. Blue App DB"
          value={displayName}
          onChange={(e) => setDisplayName(e.currentTarget.value)}
        />
        <TextInput
          label="Database name"
          required
          placeholder="e.g. appdb"
          value={databaseName}
          onChange={(e) => setDatabaseName(e.currentTarget.value)}
        />
        <Select
          label="Read credential"
          required
          placeholder="Select credential"
          data={credentialOptions}
          value={readCredentialId}
          onChange={setReadCredentialId}
        />
        <Select
          label="Write credential (optional)"
          placeholder="None"
          data={credentialOptions}
          value={writeCredentialId}
          onChange={setWriteCredentialId}
          clearable
        />
        {isEditing && (
          <Group>
            <Button
              variant="subtle"
              size="xs"
              color={isDisabled ? "teal" : "gray"}
              onClick={() => setIsDisabled((v) => !v)}
            >
              {isDisabled ? "Enable database" : "Disable database"}
            </Button>
          </Group>
        )}
        <Group justify="flex-end" mt="md">
          <Button type="submit" loading={isPending}>
            {isEditing ? "Save changes" : "Add database"}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
