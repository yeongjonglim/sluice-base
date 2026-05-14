import { Badge, Button, Group, Select, Stack, Table, Text, TextInput, Title } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { IconPlus } from "@tabler/icons-react";
import { Link, createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import type { UpdateRequestFilters } from "@/api/hooks";
import { meQueryOptions, useCatalogServer, useUpdateRequests } from "@/api/hooks";

type UpdateListSearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
};

export const Route = createFileRoute("/_authed/update/")({
  validateSearch: (search: Record<string, unknown>): UpdateListSearch => ({
    from: typeof search.from === "string" ? search.from : undefined,
    to: typeof search.to === "string" ? search.to : undefined,
    databaseId: typeof search.databaseId === "string" ? search.databaseId : undefined,
    status: typeof search.status === "string" ? search.status : undefined,
  }),
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

const STATUS_OPTIONS = [
  { value: "", label: "All statuses" },
  { value: "Pending", label: "Pending" },
  { value: "Approved", label: "Approved" },
  { value: "Rejected", label: "Rejected" },
  { value: "Cancelled", label: "Cancelled" },
  { value: "Executed", label: "Executed" },
];

function dateToParam(d: string | null): string | undefined {
  return d || undefined;
}

function paramToDate(s: string | undefined): string | null {
  return s ?? null;
}

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
  const search = Route.useSearch();
  const navigate = useNavigate();
  const [submitterSearch, setSubmitterSearch] = useState("");

  const servers = useCatalogServer();
  const filters: UpdateRequestFilters = {
    from: search.from,
    to: search.to,
    databaseId: search.databaseId,
    status: search.status,
  };
  const requests = useUpdateRequests(filters);

  const me = Route.useRouteContext().queryClient.getQueryData(meQueryOptions.queryKey);
  const canSubmit = me?.permissions.includes("update:submit") ?? false;

  function setFilter(key: keyof UpdateListSearch, value: string | undefined) {
    void navigate({
      to: "/update",
      search: (prev: UpdateListSearch) => ({ ...prev, [key]: value || undefined }),
    });
  }

  const databaseOptions = [
    { value: "", label: "All databases" },
    ...(servers.data?.servers ?? []).flatMap((s) =>
      s.databases.map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
    ),
  ];

  const allRequests = requests.data?.requests ?? [];
  const displayedRequests = submitterSearch
    ? allRequests.filter((r) =>
        (r.submitterName ?? "").toLowerCase().includes(submitterSearch.toLowerCase()),
      )
    : allRequests;

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

      <Group gap="sm" wrap="wrap">
        <DateInput
          label="From"
          size="sm"
          clearable
          valueFormat="YYYY-MM-DD"
          value={paramToDate(search.from)}
          onChange={(d) => setFilter("from", dateToParam(d))}
          style={{ width: 160 }}
        />
        <DateInput
          label="To"
          size="sm"
          clearable
          valueFormat="YYYY-MM-DD"
          value={paramToDate(search.to)}
          onChange={(d) => setFilter("to", dateToParam(d))}
          style={{ width: 160 }}
        />
        <Select
          label="Database"
          size="sm"
          data={databaseOptions}
          value={search.databaseId ?? ""}
          onChange={(v) => setFilter("databaseId", v ?? undefined)}
          style={{ width: 240 }}
        />
        <Select
          label="Status"
          size="sm"
          data={STATUS_OPTIONS}
          value={search.status ?? ""}
          onChange={(v) => setFilter("status", v ?? undefined)}
          style={{ width: 160 }}
        />
        <TextInput
          label="Submitter"
          placeholder="Filter by name…"
          size="sm"
          value={submitterSearch}
          onChange={(e) => setSubmitterSearch(e.currentTarget.value)}
          style={{ width: 200 }}
        />
      </Group>

      {requests.isPending && <Text c="dimmed">Loading…</Text>}
      {requests.isError && <Text c="red">Failed to load requests.</Text>}
      {requests.data && displayedRequests.length === 0 && (
        <Text c="dimmed">No update requests match the current filters.</Text>
      )}

      {requests.data && displayedRequests.length > 0 && (
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
            {displayedRequests.map((r) => (
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
