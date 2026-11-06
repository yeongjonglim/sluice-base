import { Badge, Button, Group, Loader, Modal, Stack, Table, Text, Tooltip } from "@mantine/core";
import type { McpGrant } from "@/api/hooks";
import { useMyMcpGrants, useRevokeMyMcpGrant } from "@/api/hooks";
import { timeAgo } from "@/utils/timeAgo";

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

function GrantRow({ grant }: { grant: McpGrant }) {
  const revoke = useRevokeMyMcpGrant();
  return (
    <Table.Tr>
      <Table.Td style={{ maxWidth: 260 }}>
        <Group gap={6} wrap="nowrap">
          <Tooltip label={grant.clientId} withArrow>
            <Text size="sm" truncate="end">
              {grant.clientId}
            </Text>
          </Tooltip>
          {grant.isLoopback && (
            <Badge size="xs" variant="light" color="gray">
              local
            </Badge>
          )}
        </Group>
      </Table.Td>
      <Table.Td>
        <Text size="sm">{timeAgo(grant.lastUsedAt ?? grant.signedInAt)}</Text>
      </Table.Td>
      <Table.Td>
        <Text size="sm">{dateFormat.format(new Date(grant.expiresAt))}</Text>
      </Table.Td>
      <Table.Td style={{ textAlign: "right" }}>
        <Button
          size="xs"
          variant="light"
          color="red"
          loading={revoke.isPending}
          onClick={() => revoke.mutate(grant.id)}
        >
          Revoke
        </Button>
      </Table.Td>
    </Table.Tr>
  );
}

export function McpConnectionsModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const grants = useMyMcpGrants(opened);
  const rows = grants.data?.grants ?? [];

  return (
    <Modal opened={opened} onClose={onClose} title="MCP connections" size="lg">
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          AI tools you have signed in to with your account. Revoking one ends its access right away; it has to
          sign in again to reconnect.
        </Text>
        {grants.isLoading ? (
          <Loader size="sm" />
        ) : rows.length === 0 ? (
          <Text size="sm">No AI tools are connected.</Text>
        ) : (
          <Table>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Client</Table.Th>
                <Table.Th>Last used</Table.Th>
                <Table.Th>Expires</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((grant) => (
                <GrantRow key={grant.id} grant={grant} />
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>
    </Modal>
  );
}
