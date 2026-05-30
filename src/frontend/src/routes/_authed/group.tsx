import {
  ActionIcon,
  Avatar,
  Badge,
  Box,
  Button,
  Flex,
  Group,
  Modal,
  NavLink,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import {
  IconPencil,
  IconSearch,
  IconShieldLock,
  IconTrash,
  IconUserPlus,
  IconUsers,
  IconUsersGroup,
} from "@tabler/icons-react";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import type { components } from "@/api/schema.ts";
import { permissionLabel } from "@/auth/permission.ts";
import {
  meQueryOptions,
  useAddGroupMember,
  useCreateGroup,
  useDeleteGroup,
  useGrantGroupPermission,
  useGroupMembers,
  useGroupPermissions,
  useGroups,
  usePermissionCatalog,
  useRemoveGroupMember,
  useRevokeGroupPermission,
  useUpdateGroup,
  useUsers,
} from "@/api/hooks";

type GroupSummary = components["schemas"]["GroupItem"];

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
  const [search, setSearch] = useState("");
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const allGroups = groups.data?.groups ?? [];
  const filtered = allGroups.filter((g) =>
    g.name.toLowerCase().includes(search.toLowerCase())
  );

  const selected =
    allGroups.find((g) => g.id === selectedId) ?? filtered.at(0) ?? null;

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={2}>Group management</Title>
        <Button
          leftSection={<IconUsersGroup size={16} />}
          onClick={() => setShowCreateModal(true)}
        >
          Create group
        </Button>
      </Group>

      {allGroups.length === 0 && !groups.isLoading ? (
        <Paper withBorder p="xl" radius="sm">
          <Stack align="center" gap="xs">
            <IconUsersGroup
              size={32}
              stroke={1.5}
              color="var(--mantine-color-dimmed)"
            />
            <Text c="dimmed" size="sm">
              No groups yet. Create one to get started.
            </Text>
          </Stack>
        </Paper>
      ) : (
        <Flex gap="md" align="flex-start">
          <Box w={240} style={{ flexShrink: 0 }}>
            <Stack gap="xs">
              <TextInput
                placeholder="Search groups…"
                value={search}
                onChange={(e) => setSearch(e.currentTarget.value)}
                leftSection={<IconSearch size={16} />}
                size="sm"
              />
              <Paper withBorder radius="sm">
                <ScrollArea.Autosize mah={460}>
                  {filtered.length === 0 ? (
                    <Text c="dimmed" size="sm" p="sm">
                      No matches.
                    </Text>
                  ) : (
                    filtered.map((group) => (
                      <NavLink
                        key={group.id}
                        active={selected?.id === group.id}
                        label={<Text truncate>{group.name}</Text>}
                        description={
                          group.description ? (
                            <Text size="xs" truncate>
                              {group.description}
                            </Text>
                          ) : (
                            <Text size="xs" c="dimmed" fs="italic">
                              No description
                            </Text>
                          )
                        }
                        leftSection={
                          <Avatar
                            name={group.name}
                            color="initials"
                            radius="sm"
                            size="sm"
                          />
                        }
                        rightSection={
                          <Tooltip
                            label={`${group.memberCount} ${
                              group.memberCount === 1 ? "member" : "members"
                            }`}
                            withArrow
                          >
                            <Badge variant="light" color="gray" size="sm">
                              {group.memberCount}
                            </Badge>
                          </Tooltip>
                        }
                        onClick={() => setSelectedId(group.id)}
                      />
                    ))
                  )}
                </ScrollArea.Autosize>
              </Paper>
            </Stack>
          </Box>

          <Box style={{ flex: 1, minWidth: 0 }}>
            {selected ? (
              <GroupDetail key={selected.id} group={selected} />
            ) : (
              <Paper withBorder p="xl" radius="sm">
                <Text c="dimmed" size="sm" ta="center">
                  Select a group to manage its members.
                </Text>
              </Paper>
            )}
          </Box>
        </Flex>
      )}

      {showCreateModal && (
        <GroupFormModal onClose={() => setShowCreateModal(false)} />
      )}
    </Stack>
  );
}

function GroupDetail({ group }: { group: GroupSummary }) {
  const deleteGroup = useDeleteGroup();
  const members = useGroupMembers(group.id);
  const users = useUsers();
  const addMember = useAddGroupMember();
  const removeMember = useRemoveGroupMember();
  const catalog = usePermissionCatalog();
  const groupPerms = useGroupPermissions(group.id);
  const grantPerm = useGrantGroupPermission();
  const revokePerm = useRevokeGroupPermission();
  const [toAdd, setToAdd] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  const allUsers = users.data?.users ?? [];
  const memberList = members.data?.members ?? [];
  const memberIds = new Set(memberList.map((m) => m.userId));
  const available = allUsers.filter((u) => !memberIds.has(u.id));

  const catalogPerms = catalog.data?.permissions ?? [];
  const grantedPerms = new Set(
    groupPerms.data?.permissions.map((p) => p.permission) ?? []
  );

  const permMutating = (permission: string) =>
    (grantPerm.isPending && grantPerm.variables.permission === permission) ||
    (revokePerm.isPending && revokePerm.variables.permission === permission);

  function togglePermission(permission: string, next: boolean) {
    if (next) {
      grantPerm.mutate({ groupId: group.id, permission });
    } else {
      revokePerm.mutate({ groupId: group.id, permission });
    }
  }

  function confirmDelete() {
    modals.openConfirmModal({
      title: `Delete ${group.name}?`,
      children: (
        <Text size="sm">
          This will remove all associated members and permissions.
        </Text>
      ),
      labels: { confirm: "Delete", cancel: "Cancel" },
      confirmProps: { color: "red" },
      onConfirm: () => deleteGroup.mutate({ groupId: group.id }),
    });
  }

  function handleAdd(userId: string | null) {
    if (userId) {
      addMember.mutate({ groupId: group.id, userId });
      setToAdd(null);
    }
  }

  return (
    <Paper withBorder radius="sm" p="md">
      <Stack gap="md">
        <Group justify="space-between" wrap="nowrap" align="flex-start">
          <Box style={{ minWidth: 0 }}>
            <Title
              order={3}
              style={{
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
              }}
            >
              {group.name}
            </Title>
            {group.description && (
              <Text size="sm" c="dimmed">
                {group.description}
              </Text>
            )}
          </Box>
          <Group gap="xs" wrap="nowrap">
            <Tooltip label="Edit group" withArrow>
              <ActionIcon
                variant="subtle"
                color="gray"
                size="lg"
                onClick={() => setEditing(true)}
              >
                <IconPencil size={18} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete group" withArrow>
              <ActionIcon
                variant="subtle"
                color="red"
                size="lg"
                onClick={confirmDelete}
              >
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>

        <Select
          placeholder="Add member…"
          searchable
          value={toAdd}
          data={available.map((u) => ({
            value: u.id,
            label: u.email || u.id,
          }))}
          onChange={handleAdd}
          disabled={available.length === 0}
          leftSection={<IconUserPlus size={16} />}
          nothingFoundMessage="No users available"
          comboboxProps={{ shadow: "md" }}
        />

        <Box>
          <Group gap={6} mb="xs">
            <IconUsers size={15} />
            <Text fw={600} size="sm">
              Members
            </Text>
            <Badge variant="light" color="gray" size="sm">
              {memberList.length}
            </Badge>
          </Group>

          {members.isLoading ? (
            <Text c="dimmed" size="sm">
              Loading members…
            </Text>
          ) : memberList.length === 0 ? (
            <Text c="dimmed" size="sm">
              No members yet — add one above.
            </Text>
          ) : (
            <Stack gap={2}>
              {memberList.map((member) => (
                <Group
                  key={member.id}
                  gap="sm"
                  wrap="nowrap"
                  px="xs"
                  py={6}
                  style={{ borderRadius: "var(--mantine-radius-sm)" }}
                >
                  <Avatar
                    name={member.userName || member.userEmail || "?"}
                    color="initials"
                    radius="xl"
                    size="sm"
                  />
                  <Box style={{ flex: 1, minWidth: 0 }}>
                    <Text size="sm" truncate>
                      {member.userEmail}
                    </Text>
                    {member.userName ? (
                      <Text size="xs" c="dimmed" truncate>
                        {member.userName}
                      </Text>
                    ) : null}
                  </Box>
                  <Tooltip label="Remove member" withArrow>
                    <ActionIcon
                      color="red"
                      variant="subtle"
                      size="sm"
                      loading={
                        removeMember.isPending &&
                        removeMember.variables.userId === member.userId
                      }
                      onClick={() =>
                        removeMember.mutate({
                          groupId: group.id,
                          userId: member.userId,
                        })
                      }
                    >
                      <IconTrash size={15} />
                    </ActionIcon>
                  </Tooltip>
                </Group>
              ))}
            </Stack>
          )}
        </Box>

        <Box>
          <Group gap={6} mb="xs">
            <IconShieldLock size={15} />
            <Text fw={600} size="sm">
              Permissions
            </Text>
          </Group>

          {catalog.isLoading || groupPerms.isLoading ? (
            <Text c="dimmed" size="sm">
              Loading permissions…
            </Text>
          ) : catalogPerms.length === 0 ? (
            <Text c="dimmed" size="sm">
              No grantable permissions.
            </Text>
          ) : (
            <Stack gap={2}>
              {catalogPerms.map((permission) => {
                const label = permissionLabel(permission);
                return (
                  <Group
                    key={permission}
                    justify="space-between"
                    wrap="nowrap"
                    px="xs"
                    py={6}
                  >
                    <Box style={{ minWidth: 0 }}>
                      <Text size="sm">{label.full}</Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {permission}
                      </Text>
                    </Box>
                    <Switch
                      checked={grantedPerms.has(permission)}
                      disabled={permMutating(permission)}
                      aria-label={label.full}
                      onChange={(e) =>
                        togglePermission(permission, e.currentTarget.checked)
                      }
                    />
                  </Group>
                );
              })}
            </Stack>
          )}
        </Box>
      </Stack>

      {editing && (
        <GroupFormModal group={group} onClose={() => setEditing(false)} />
      )}
    </Paper>
  );
}

function GroupFormModal({
  group,
  onClose,
}: {
  group?: GroupSummary;
  onClose: () => void;
}) {
  const create = useCreateGroup();
  const update = useUpdateGroup();
  const [name, setName] = useState(group?.name ?? "");
  const [description, setDescription] = useState(group?.description ?? "");

  const isEdit = group !== undefined;
  const pending = create.isPending || update.isPending;

  function submit() {
    if (!name.trim()) return;
    if (group) {
      update.mutate(
        { groupId: group.id, name, description },
        { onSuccess: onClose }
      );
    } else {
      create.mutate({ name, description }, { onSuccess: onClose });
    }
  }

  return (
    <Modal opened onClose={onClose} title={isEdit ? "Edit group" : "Create group"}>
      <Stack gap="md">
        <TextInput
          label="Name"
          placeholder="Group name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          data-autofocus
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
          <Button loading={pending} onClick={submit} disabled={!name.trim()}>
            {isEdit ? "Save" : "Create"}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
