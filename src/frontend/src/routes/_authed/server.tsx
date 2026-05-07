import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Collapse,
  Group,
  Modal,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconPencil, IconPlayerPlay, IconServer, IconTrash } from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import React, { useState } from "react";
import type { ServerItem, TestConnectionResponse } from "@/api/hooks";
import {
  meQueryOptions,
  useCreateServer,
  useDeleteServer,
  useServers,
  useTestConnection,
  useUpdateServer,
} from "@/api/hooks";

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
              <Table.Th>Host</Table.Th>
              <Table.Th>Database</Table.Th>
              <Table.Th>Read user</Table.Th>
              <Table.Th>Write user</Table.Th>
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
        size="lg"
      >
        <ServerForm server={editing} onSuccess={closeModal} />
      </Modal>
    </Stack>
  );
}

function ServerRow({ server, onEdit }: { server: ServerItem; onEdit: () => void }) {
  const deleteServer = useDeleteServer();
  const testConn = useTestConnection();
  const [testResult, setTestResult] = useState<TestConnectionResponse | null>(null);

  async function handleTest() {
    setTestResult(null);
    const result = await testConn.mutateAsync(server.id);
    setTestResult(result);
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
        <Table.Td>{server.database}</Table.Td>
        <Table.Td>{server.hasWriteCredential ? "Has write" : "No write"}</Table.Td>
        <Table.Td>
          <Group gap="xs">
            <ActionIcon
              variant="subtle"
              loading={testConn.isPending}
              onClick={() => void handleTest()}
              title="Test connection"
            >
              <IconPlayerPlay size={16} />
            </ActionIcon>
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
      {testResult && (
        <Table.Tr>
          <Table.Td colSpan={7}>
            <Group gap="xs">
              <ConnBadge label="Read" result={testResult.read} />
              {testResult.write && <ConnBadge label="Write" result={testResult.write} />}
            </Group>
          </Table.Td>
        </Table.Tr>
      )}
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
  const [kind] = useState("postgres");
  const [host, setHost] = useState(server?.host ?? "");
  const [port, setPort] = useState<string | number>(server?.port ?? 5432);
  const [database, setDatabase] = useState(server?.database ?? "");
  const [readUsername, setReadUsername] = useState<string>("");
  const [readPassword, setReadPassword] = useState<string>("");
  const [writeUsername, setWriteUsername] = useState<string | undefined>();
  const [writePassword, setWritePassword] = useState<string | undefined>();
  const [clearWrite, setClearWrite] = useState(false);
  const [writeOpen, { toggle: toggleWrite }] = useDisclosure(!!server?.hasWriteCredential);

  async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault();
    if (isEditing) {
      await updateServer.mutateAsync({
        id: server.id,
        body: {
          name,
          host,
          port,
          database,
          readUsername,
          readPassword,
          writeUsername: clearWrite ? "" : writeUsername || null,
          writePassword: clearWrite ? "" : writePassword || null,
          isEnabled: true, // Think about how to disable later
        },
      });
    } else {
      await createServer.mutateAsync({
        name,
        kind,
        host,
        port,
        database,
        readUsername: readUsername || "",
        readPassword: readPassword || "",
        writeUsername: writeUsername || undefined,
        writePassword: writePassword || undefined,
      });
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
          data={[{ value: "postgres", label: "PostgreSQL" }]}
          readOnly
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
          />
        </Group>
        <TextInput
          label="Database"
          required
          value={database}
          onChange={(e) => setDatabase(e.currentTarget.value)}
        />

        <Text fw={500} size="sm" mt="xs">
          Read credentials
        </Text>
        <TextInput
          label="Username"
          required={!isEditing}
          placeholder={isEditing ? "Leave blank to keep existing" : "Enter username"}
          value={readUsername}
          onChange={(e) => setReadUsername(e.currentTarget.value)}
        />
        <PasswordInput
          label="Password"
          required={!isEditing}
          placeholder={isEditing ? "Leave blank to keep existing" : "Enter password"}
          value={readPassword}
          onChange={(e) => setReadPassword(e.currentTarget.value)}
        />

        <Button variant="subtle" size="xs" onClick={toggleWrite} mt="xs">
          {writeOpen ? "Hide write credentials" : "Add write credentials (optional)"}
        </Button>
        <Collapse expanded={writeOpen}>
          <Stack gap="sm">
            <TextInput
              label="Write username"
              value={writeUsername}
              onChange={(e) => setWriteUsername(e.currentTarget.value)}
              disabled={clearWrite}
            />
            <PasswordInput
              label="Write password"
              placeholder={
                isEditing && server.hasWriteCredential
                  ? "Leave blank to keep existing"
                  : "Enter password"
              }
              value={writePassword}
              onChange={(e) => setWritePassword(e.currentTarget.value)}
              disabled={clearWrite}
            />
            {isEditing && server.hasWriteCredential && (
              <Checkbox
                label="Clear write credentials"
                checked={clearWrite}
                onChange={(e) => setClearWrite(e.currentTarget.checked)}
              />
            )}
          </Stack>
        </Collapse>

        <Group justify="flex-end" mt="md">
          <Button type="submit" loading={isPending}>
            {isEditing ? "Save changes" : "Add server"}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
