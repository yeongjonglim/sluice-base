import {
  ActionIcon,
  Alert,
  Badge,
  Group,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { IconCopy } from "@tabler/icons-react";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import type { QueryHistoryFilters, QueryHistoryItem } from "@/api/hooks";
import { meQueryOptions, useCatalogServer, useQueryHistory } from "@/api/hooks";
import { useHasPermission } from "@/auth/permission";

type HistorySearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
};

export const Route = createFileRoute("/_authed/query/history")({
  validateSearch: (search: Record<string, unknown>): HistorySearch => ({
    from: typeof search.from === "string" ? search.from : undefined,
    to: typeof search.to === "string" ? search.to : undefined,
    databaseId: typeof search.databaseId === "string" ? search.databaseId : undefined,
    status: typeof search.status === "string" ? search.status : undefined,
  }),
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryHistoryPage,
});

const STATUS_COLOR: Record<string, string> = {
  Success: "teal",
  Error: "red",
  Timeout: "orange",
  Unknown: "gray",
};

const STATUS_OPTIONS = [
  { value: "", label: "All statuses" },
  { value: "Success", label: "Success" },
  { value: "Error", label: "Error" },
  { value: "Timeout", label: "Timeout" },
];

function QueryHistoryPage() {
  const search = Route.useSearch();
  const navigate = useNavigate();
  const canAudit = useHasPermission("query:audit");
  const [userSearch, setUserSearch] = useState("");

  const servers = useCatalogServer();
  const filters: QueryHistoryFilters = {
    from: search.from,
    to: search.to,
    databaseId: search.databaseId,
    status: search.status,
  };
  const history = useQueryHistory(filters);

  const databaseOptions = [
    { value: "", label: "All databases" },
    ...(servers.data?.servers ?? []).flatMap((s) =>
      s.databases.map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
    ),
  ];

  function setFilter(key: keyof HistorySearch, value: string | undefined) {
    void navigate({
      to: "/query/history",
      search: (prev: HistorySearch) => ({ ...prev, [key]: value || undefined }),
    });
  }

  const allItems = history.data?.items ?? [];
  const displayedItems = canAudit && userSearch
    ? allItems.filter((i) =>
        (i.userName ?? "").toLowerCase().includes(userSearch.toLowerCase()),
      )
    : allItems;

  return (
    <Stack gap="md">
      <Title order={2}>Query History</Title>

      <Group gap="sm" wrap="wrap">
        <TextInput
          type="date"
          label="From"
          size="sm"
          value={search.from ?? ""}
          onChange={(e) => setFilter("from", e.currentTarget.value)}
          style={{ width: 160 }}
        />
        <TextInput
          type="date"
          label="To"
          size="sm"
          value={search.to ?? ""}
          onChange={(e) => setFilter("to", e.currentTarget.value)}
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
        {canAudit && (
          <TextInput
            label="User"
            placeholder="Filter by name…"
            size="sm"
            value={userSearch}
            onChange={(e) => setUserSearch(e.currentTarget.value)}
            style={{ width: 200 }}
          />
        )}
      </Group>

      {history.isPending && (
        <Text c="dimmed" size="sm">Loading…</Text>
      )}

      {history.isError && (
        <Alert color="red" title="Failed to load history">
          Could not reach the server. Check your connection and try again.
        </Alert>
      )}

      {history.data && displayedItems.length === 0 && (
        <Text c="dimmed" size="sm">No entries match the current filters.</Text>
      )}

      {history.data && displayedItems.length > 0 && (
        <ScrollArea type="auto">
          <Table striped withTableBorder highlightOnHover fz="sm" style={{ tableLayout: "fixed", width: "100%" }}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th style={{ width: 80 }}>Status</Table.Th>
                <Table.Th style={{ width: 170 }}>Database</Table.Th>
                {canAudit && <Table.Th style={{ width: 120 }}>User</Table.Th>}
                <Table.Th>SQL</Table.Th>
                <Table.Th style={{ width: 155 }}>Executed At</Table.Th>
                <Table.Th style={{ width: 90 }}>Duration</Table.Th>
                <Table.Th style={{ width: 60 }}>Rows</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {displayedItems.map((item) => (
                <HistoryRow key={item.id} item={item} canAudit={canAudit} />
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Stack>
  );
}

function HistoryRow({ item, canAudit }: { item: QueryHistoryItem; canAudit: boolean }) {
  const colorScheme = useComputedColorScheme();

  function copySql() {
    void navigator.clipboard.writeText(item.queryText).then(() => {
      notifications.show({ message: "SQL copied to clipboard", color: "teal" });
    }).catch(() => {
      // Clipboard API unavailable — silent no-op per spec
    });
  }

  return (
    <Table.Tr>
      <Table.Td>
        <Badge color={STATUS_COLOR[item.status] ?? "gray"} size="sm">
          {item.status}
        </Badge>
      </Table.Td>
      <Table.Td>{item.databaseDisplayName ?? "—"}</Table.Td>
      {canAudit && <Table.Td>{item.userName ?? "—"}</Table.Td>}
      <Table.Td>
        <Group gap="xs" align="flex-start" wrap="nowrap">
          <div style={{ flex: 1, minWidth: 0, overflow: "hidden" }}>
            <CodeMirror
              value={item.queryText}
              readOnly
              editable={false}
              extensions={[sql()]}
              theme={colorScheme === "dark" ? githubDark : githubLight}
              height="auto"
              maxHeight="120px"
              basicSetup={{ lineNumbers: false, foldGutter: false }}
            />
          </div>
          <ActionIcon size="sm" variant="subtle" onClick={copySql} aria-label="Copy SQL">
            <IconCopy size={14} />
          </ActionIcon>
        </Group>
      </Table.Td>
      <Table.Td>
        <Text size="xs">
          {new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" })
            .format(new Date(item.executedAt))}
        </Text>
      </Table.Td>
      <Table.Td>
        {item.durationMs != null ? (
          <Text size="xs">{item.durationMs} ms</Text>
        ) : "—"}
      </Table.Td>
      <Table.Td>
        {item.rowCount != null ? (
          <Text size="xs">{item.rowCount}</Text>
        ) : "—"}
      </Table.Td>
    </Table.Tr>
  );
}
