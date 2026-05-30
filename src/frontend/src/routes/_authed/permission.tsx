import { Badge, Card, Group, Stack, Switch, Table, Text, TextInput, Title } from "@mantine/core";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import type { paths } from "@/api/schema.ts";
import { permissionLabel } from "@/auth/permission.ts";
import {
  meQueryOptions,
  useGrantPermission,
  useMe,
  usePermissionCatalog,
  useRevokePermission,
  useUsers,
} from "@/api/hooks";

type UserSummary = paths["/api/admin/user"]["get"]["responses"][200]["content"]["application/json"]["users"][0];

export const Route = createFileRoute("/_authed/permission")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: PermissionsAdminPage,
});

function PermissionsAdminPage() {
  const me = useMe();
  const users = useUsers();
  const catalog = usePermissionCatalog();
  const grant = useGrantPermission();
  const revoke = useRevokePermission();
  const [search, setSearch] = useState("");

  const permissions = catalog.data?.permissions ?? [];
  const allUsers = users.data?.users ?? [];
  const filtered = allUsers.filter(
    (u) =>
      (u.email ?? "").toLowerCase().includes(search.toLowerCase()) ||
      (u.name ?? "").toLowerCase().includes(search.toLowerCase()),
  );

  const isMutating = (userId: string) =>
    (grant.isPending && grant.variables.userId === userId) ||
    (revoke.isPending && revoke.variables.userId === userId);

  function confirmSelfRevoke(): Promise<boolean> {
    return new Promise((resolve) => {
      modals.openConfirmModal({
        title: "Revoke your own admin permission?",
        children: (
          <Text size="sm">
            You will lose access to this page. The bootstrap config will re-grant permission:manage
            on your next login if your email is listed there.
          </Text>
        ),
        labels: { confirm: "Revoke", cancel: "Cancel" },
        confirmProps: { color: "red" },
        onConfirm: () => resolve(true),
        onCancel: () => resolve(false),
        onClose: () => resolve(false),
      });
    });
  }

  async function handleToggle(user: UserSummary, permission: string, nextValue: boolean) {
    if (!nextValue && permission === "permission:manage" && user.id === me.data?.id) {
      const confirmed = await confirmSelfRevoke();
      if (!confirmed) return;
    }

    if (nextValue) {
      grant.mutate({ userId: user.id, permission });
    } else {
      revoke.mutate({ userId: user.id, permission });
    }
  }

  if (allUsers.length === 0 && !users.isLoading) {
    return (
      <Stack gap="md">
        <Title order={2}>Permission management</Title>
        <Card withBorder>
          <Text c="dimmed" size="sm">
            No users yet. Sign in as a bootstrap admin to populate the user table.
          </Text>
        </Card>
      </Stack>
    );
  }

  return (
    <Stack gap="md">
      <Title order={2}>Permission management</Title>
      <TextInput
        placeholder="Filter by email or name…"
        value={search}
        onChange={(e) => setSearch(e.currentTarget.value)}
        style={{ maxWidth: 320 }}
      />
      <Table.ScrollContainer minWidth={600}>
        <Table stickyHeader striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              <Table.Th>Last login</Table.Th>
              {permissions.map((p) => (
                <Table.Th key={p}>{permissionLabel(p).short}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {filtered.map((user) => (
              <Table.Tr key={user.id}>
                <Table.Td>
                  <Stack gap={2}>
                    <Group gap={6}>
                      <Text size="sm" fw={500}>
                        {user.email}
                      </Text>
                      {user.id === me.data?.id && (
                        <Badge size="xs" variant="outline">
                          you
                        </Badge>
                      )}
                    </Group>
                    {user.name && (
                      <Text size="xs" c="dimmed">
                        {user.name}
                      </Text>
                    )}
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Text size="xs" c="dimmed">
                    {user.lastLoginAt
                      ? new Intl.DateTimeFormat("en", {
                          dateStyle: "medium",
                          timeStyle: "short",
                        }).format(new Date(user.lastLoginAt))
                      : "Never"}
                  </Text>
                </Table.Td>
                {permissions.map((permission) => (
                  <Table.Td key={permission}>
                    <Switch
                      checked={user.permissions.includes(permission)}
                      disabled={isMutating(user.id)}
                      aria-label={permissionLabel(permission).full}
                      onChange={(e) => void handleToggle(user, permission, e.currentTarget.checked)}
                    />
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Stack>
  );
}
