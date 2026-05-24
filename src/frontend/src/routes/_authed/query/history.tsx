import {
  ActionIcon,
  Alert,
  Badge,
  Group,
  MultiSelect,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
  useComputedColorScheme,
} from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { notifications } from "@mantine/notifications";
import { IconCopy, IconShieldLock } from "@tabler/icons-react";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import type { QueryHistoryFilters, QueryHistoryItem } from "@/api/hooks";
import { meQueryOptions, useCatalogServer, useQueryHistory, useSensitiveColumns } from "@/api/hooks";
import { useHasPermission } from "@/auth/permission";

type HistorySearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
  sensitiveColumn?: Array<string>;
};

export const Route = createFileRoute("/_authed/query/history")({
  validateSearch: (search: Record<string, unknown>): HistorySearch => ({
    from: typeof search.from === "string" ? search.from : undefined,
    to: typeof search.to === "string" ? search.to : undefined,
    databaseId: typeof search.databaseId === "string" ? search.databaseId : undefined,
    status: typeof search.status === "string" ? search.status : undefined,
    sensitiveColumn: Array.isArray(search.sensitiveColumn)
      ? search.sensitiveColumn.filter((v): v is string => typeof v === "string")
      : typeof search.sensitiveColumn === "string"
        ? [search.sensitiveColumn]
        : undefined,
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
  Blocked: "yellow",
  Unknown: "gray",
};

const STATUS_OPTIONS = [
  { value: "", label: "All statuses" },
  { value: "Success", label: "Success" },
  { value: "Error", label: "Error" },
  { value: "Timeout", label: "Timeout" },
  { value: "Blocked", label: "Blocked" },
];

function dateToParam(d: string | null): string | undefined {
  if (!d) return undefined;
  return d;
}

function paramToDate(s: string | undefined): string | null {
  return s ?? null;
}

function QueryHistoryPage() {
  const search = Route.useSearch();
  const navigate = useNavigate();
  const canAudit = useHasPermission("query:audit");
  const [userSearch, setUserSearch] = useState("");

  const servers = useCatalogServer();
  const sensitiveColumns = useSensitiveColumns(search.databaseId ?? null);
  const filters: QueryHistoryFilters = {
    from: search.from,
    to: search.to,
    databaseId: search.databaseId,
    status: search.status,
    sensitiveColumn: search.sensitiveColumn,
  };
  const history = useQueryHistory(filters);

  const databaseOptions = [
    { value: "", label: "All databases" },
    ...(servers.data?.servers ?? []).flatMap((s) =>
      s.databases.map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
    ),
  ];

  const sensitiveColumnOptions = [
    { value: "any", label: "Any sensitive column" },
    ...(sensitiveColumns.data?.columns ?? []).map((c) => ({
      value: `${c.schemaName}.${c.tableName}.${c.columnName}`,
      label: `${c.schemaName}.${c.tableName}.${c.columnName}`,
    })),
  ];

  function setFilter(key: keyof HistorySearch, value: string | Array<string> | undefined) {
    void navigate({
      to: "/query/history",
      search: (prev: HistorySearch) => ({
        ...prev,
        [key]: Array.isArray(value) ? (value.length > 0 ? value : undefined) : (value || undefined),
      }),
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
        <MultiSelect
          label="Sensitive columns"
          size="sm"
          placeholder="All queries"
          data={sensitiveColumnOptions}
          value={search.sensitiveColumn ?? []}
          onChange={(v) => setFilter("sensitiveColumn", v)}
          clearable
          style={{ minWidth: 200 }}
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
        <Group gap={4} wrap="nowrap">
          <Badge color={STATUS_COLOR[item.status] ?? "gray"} size="sm">
            {item.status}
          </Badge>
          {item.sensitiveColumns.length > 0 && (
            <Tooltip label={item.sensitiveColumns.join(", ")} multiline>
              <IconShieldLock size={14} color="var(--mantine-color-yellow-6)" />
            </Tooltip>
          )}
        </Group>
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
