import { Badge, Button, Group, Stack, Table, Text, Title } from "@mantine/core";
import { IconPlus } from "@tabler/icons-react";
import { Link, createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { meQueryOptions, useUpdateRequests } from "@/api/hooks";

export const Route = createFileRoute("/_authed/update/")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    const hasAny =
      me?.permissions.includes("update:submit") ||
      me?.permissions.includes("update:approve") ||
      me?.permissions.includes("update:execute");
    if (!hasAny) {
      throw redirect({ to: "/" });
    }
  },
  component: UpdateListPage,
});

const STATUS_COLOR: Record<string, string> = {
  Pending: "blue",
  Approved: "green",
  Rejected: "red",
  Cancelled: "gray",
  Executed: "teal",
};

function statusBadge(
  status: "Pending" | "Approved" | "Rejected" | "Cancelled" | "Executed",
  execSuccess?: boolean | null,
) {
  if (status === "Executed" && execSuccess === false) {
    return <Badge color="red">Failed</Badge>;
  }
  return <Badge color={STATUS_COLOR[status] ?? "gray"}>{status}</Badge>;
}

function UpdateListPage() {
  const requests = useUpdateRequests();
  const navigate = useNavigate();
  const me = (
    Route.useRouteContext()
  ).queryClient.getQueryData(meQueryOptions.queryKey);
  const canSubmit = me?.permissions.includes("update:submit") ?? false;

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={2}>Update Requests</Title>
        {canSubmit && (
          <Button leftSection={<IconPlus size={14} />} component={Link} to="/update/new">
            New Request
          </Button>
        )}
      </Group>

      {requests.isPending && <Text c="dimmed">Loading…</Text>}
      {requests.isError && <Text c="red">Failed to load requests.</Text>}
      {requests.data && requests.data.requests.length === 0 && (
        <Text c="dimmed">No update requests yet.</Text>
      )}

      {requests.data && requests.data.requests.length > 0 && (
        <Table striped withTableBorder highlightOnHover style={{ cursor: "pointer" }}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Status</Table.Th>
              <Table.Th>Server</Table.Th>
              <Table.Th>Submitted by</Table.Th>
              <Table.Th>Reason</Table.Th>
              <Table.Th>Submitted</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {requests.data.requests.map((r) => (
              <Table.Tr
                key={r.id}
                onClick={() => void navigate({ to: "/update/$id", params: { id: r.id } })}
              >
                <Table.Td>{statusBadge(r.status, r.execSuccess)}</Table.Td>
                <Table.Td>{r.databaseDisplayName ?? "—"}</Table.Td>
                <Table.Td>{r.submitterName ?? "—"}</Table.Td>
                <Table.Td
                  style={{
                    maxWidth: 300,
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                  }}
                >
                  {r.reason}
                </Table.Td>
                <Table.Td>{new Date(r.submittedAt).toLocaleString()}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Stack>
  );
}
