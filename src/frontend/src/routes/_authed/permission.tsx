import { Badge, Box, Card, Group, Stack, Table, Text, TextInput, Title, Tooltip } from "@mantine/core";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import type { paths } from "@/api/schema.ts";
import {
  meQueryOptions,
  useGrantPermission,
  useMe,
  usePermissionCatalog,
  useRevokePermission,
  useUsers,
} from "@/api/hooks";
import { EffectiveCell } from "@/components/EffectiveCell";

const PERMISSION_LABELS: Record<string, { short: string; scope: string; full: string }> = {
  "query:execute": { short: "Query", scope: "read", full: "Run read queries" },
  "query:audit": { short: "Audit", scope: "read log", full: "Audit read queries" },
  "update:submit": { short: "Submit", scope: "updates", full: "Submit update requests" },
  "update:approve": { short: "Approve", scope: "updates", full: "Approve update requests" },
  "update:execute": { short: "Execute", scope: "updates", full: "Execute approved updates" },
  "server:manage": { short: "Servers", scope: "admin", full: "Manage servers" },
  "permission:manage": { short: "Permissions", scope: "admin", full: "Manage permissions" },
};

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

// The same control the Access matrix uses, so one fact always has one rendering.
// Inert: it is a key, not a second set of toggles.
function ProvenanceLegend() {
  const noop = () => {};
  return (
    <Box inert style={{ display: "flex", gap: 24, flexWrap: "wrap", alignItems: "center" }}>
      <Group gap={8}>
        <EffectiveCell fromDirect fromGroups={[]} onToggle={noop} ariaLabel="Direct grant" />
        <Text size="xs" c="dimmed">Direct grant</Text>
      </Group>
      <Group gap={8}>
        <EffectiveCell
          fromDirect={false}
          fromGroups={[{ groupId: "legend", name: "group" }]}
          onToggle={noop}
          ariaLabel="Inherited grant"
        />
        <Text size="xs" c="dimmed">Inherited via group</Text>
      </Group>
      <Group gap={8}>
        <EffectiveCell
          fromDirect
          fromGroups={[{ groupId: "legend", name: "group" }]}
          onToggle={noop}
          ariaLabel="Direct and inherited"
        />
        <Text size="xs" c="dimmed">Both</Text>
      </Group>
    </Box>
  );
}

export function PermissionsAdminPage() {
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

  // Per-cell pending: only the grant being saved spins, so the rest of the row
  // stays readable and usable.
  function pendingPermission(userId: string): string | undefined {
    // `variables` is typed as always present; it is undefined until the first call.
    const granted: { userId: string; permission: string } | undefined = grant.variables;
    const revoked: { userId: string; permission: string } | undefined = revoke.variables;
    if (grant.isPending && granted && granted.userId === userId) return granted.permission;
    if (revoke.isPending && revoked && revoked.userId === userId) return revoked.permission;
    return undefined;
  }

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
      <Stack gap="sm">
        <TextInput
          placeholder="Filter by email or name…"
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          style={{ maxWidth: 320 }}
        />
        {permissions.length > 0 && <ProvenanceLegend />}
      </Stack>
      <Text size="xs" c="dimmed" hiddenFrom="md">
        Scroll sideways for the permission columns.
      </Text>
      <Table.ScrollContainer minWidth={860}>
        <Table stickyHeader striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              <Table.Th style={{ whiteSpace: "nowrap" }}>Last login</Table.Th>
              {permissions.map((p) => (
                <Table.Th key={p} style={{ textAlign: "center" }}>
                  <Tooltip label={PERMISSION_LABELS[p].full} withArrow>
                    <Stack gap={0} align="center">
                      <Text size="xs" fw={600}>{PERMISSION_LABELS[p].short}</Text>
                      <Text size="xs" c="dimmed" style={{ fontSize: 10, lineHeight: 1.2 }}>
                        {PERMISSION_LABELS[p].scope}
                      </Text>
                    </Stack>
                  </Tooltip>
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {filtered.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={permissions.length + 2}>
                  <Text size="sm" c="dimmed">
                    No users match “{search}”.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
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
                  <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>
                    {user.lastLoginAt
                      ? new Intl.DateTimeFormat("en", {
                          dateStyle: "medium",
                          timeStyle: "short",
                        }).format(new Date(user.lastLoginAt))
                      : "Never"}
                  </Text>
                </Table.Td>
                {permissions.map((permission) => {
                  const ep = user.permissions.find((p) => p.permission === permission);
                  const fromDirect = ep?.fromDirect ?? false;
                  const fromGroups = (ep?.fromGroups ?? []).map((g) => ({
                    groupId: String(g.groupId),
                    name: g.name,
                  }));

                  return (
                    <Table.Td key={permission} style={{ textAlign: "center" }}>
                      <Stack gap={4} align="center">
                        <EffectiveCell
                          fromDirect={fromDirect}
                          fromGroups={fromGroups}
                          onToggle={(next) => void handleToggle(user, permission, next)}
                          ariaLabel={`${PERMISSION_LABELS[permission].full} for ${user.email ?? user.id}`}
                          pending={pendingPermission(user.id) === permission}
                        />
                        {fromGroups.length > 0 && (
                          <Text
                            size="xs"
                            c="cyan"
                            style={{ fontSize: 10, lineHeight: 1.2 }}
                            title={`Inherited via ${fromGroups.map((g) => g.name).join(", ")}`}
                          >
                            {fromGroups.map((g) => g.name).join(", ")}
                          </Text>
                        )}
                      </Stack>
                    </Table.Td>
                  );
                })}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Stack>
  );
}
