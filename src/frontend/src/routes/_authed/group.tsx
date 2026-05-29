import {
  Button,
  Card,
  Group,
  Modal,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import {
  meQueryOptions,
  useCreateGroup,
  useDeleteGroup,
  useGroups,
} from "@/api/hooks";

export const Route = createFileRoute("/_authed/group")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("group:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: GroupAdminPage,
});

function GroupAdminPage() {
  const groups = useGroups();
  const deleteGroup = useDeleteGroup();
  const [search, setSearch] = useState("");
  const [showCreateModal, setShowCreateModal] = useState(false);

  const allGroups = groups.data?.groups ?? [];
  const filtered = allGroups.filter((g) =>
    g.name.toLowerCase().includes(search.toLowerCase())
  );

  if (allGroups.length === 0 && !groups.isLoading) {
    return (
      <Stack gap="md">
        <Group justify="space-between">
          <Title order={2}>Group management</Title>
          <Button onClick={() => setShowCreateModal(true)}>Create group</Button>
        </Group>
        <Card withBorder>
          <Text c="dimmed" size="sm">
            No groups yet. Create one to get started.
          </Text>
        </Card>
        {showCreateModal && (
          <CreateGroupModal onClose={() => setShowCreateModal(false)} />
        )}
      </Stack>
    );
  }

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={2}>Group management</Title>
        <Button onClick={() => setShowCreateModal(true)}>Create group</Button>
      </Group>

      <TextInput
        placeholder="Search groups..."
        value={search}
        onChange={(e) => setSearch(e.currentTarget.value)}
      />

      <Stack gap="sm">
        {filtered.map((group) => (
          <Card key={group.id} withBorder>
            <Group justify="space-between">
              <Stack gap={4} style={{ flex: 1 }}>
                <Text fw={500}>{group.name}</Text>
                {group.description && (
                  <Text size="sm" c="dimmed">
                    {group.description}
                  </Text>
                )}
                <Text size="xs" c="dimmed">
                  {group.memberCount} members
                </Text>
              </Stack>
              <Button
                color="red"
                onClick={() => {
                  modals.openConfirmModal({
                    title: `Delete ${group.name}?`,
                    children: (
                      <Text size="sm">
                        This will remove all associated members and permissions.
                      </Text>
                    ),
                    labels: { confirm: "Delete", cancel: "Cancel" },
                    confirmProps: { color: "red" },
                    onConfirm: () => {
                      deleteGroup.mutate({ groupId: group.id });
                    },
                  });
                }}
              >
                Delete
              </Button>
            </Group>
          </Card>
        ))}
      </Stack>

      {showCreateModal && (
        <CreateGroupModal onClose={() => setShowCreateModal(false)} />
      )}
    </Stack>
  );
}

function CreateGroupModal({ onClose }: { onClose: () => void }) {
  const create = useCreateGroup();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  return (
    <Modal opened onClose={onClose} title="Create group">
      <Stack gap="md">
        <TextInput
          label="Name"
          placeholder="Group name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <TextInput
          label="Description"
          placeholder="Group description (optional)"
          value={description}
          onChange={(e) => setDescription(e.currentTarget.value)}
        />
        <Group justify="flex-end">
          <Button variant="light" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={() => {
              if (name.trim()) {
                create.mutate({ name, description });
                onClose();
              }
            }}
            disabled={!name.trim()}
          >
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
