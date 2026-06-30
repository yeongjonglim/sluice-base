import {
  ActionIcon,
  Alert,
  Group,
  MultiSelect,
  Paper,
  Select,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { notifications } from "@mantine/notifications";
import { IconAlertTriangle, IconCopy, IconDeviceDesktop, IconPlugConnected, IconShieldLock } from "@tabler/icons-react";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useMemo, useState } from "react";
import type { QueryHistoryFilters, QueryHistoryItem } from "@/api/hooks";
import { meQueryOptions, useCatalogServer, useQueryHistory } from "@/api/hooks";
import { SqlEditor } from "@/components/SqlEditor";
import { useHasPermission } from "@/auth/permission";

type HistorySearch = {
  from?: string;
  to?: string;
  databaseId?: string;
  status?: string;
  source?: string;
  sensitiveColumn?: Array<string>;
};

export const Route = createFileRoute("/_authed/query/history")({
  validateSearch: (search: Record<string, unknown>): HistorySearch => ({
    from: typeof search.from === "string" ? search.from : undefined,
    to: typeof search.to === "string" ? search.to : undefined,
    databaseId: typeof search.databaseId === "string" ? search.databaseId : undefined,
    status: typeof search.status === "string" ? search.status : undefined,
    source: typeof search.source === "string" ? search.source : undefined,
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

const SOURCE_OPTIONS = [
  { value: "", label: "All sources" },
  { value: "Ui", label: "UI" },
  { value: "Mcp", label: "MCP" },
];

function dateToParam(d: string | null): string | undefined {
  if (!d) return undefined;
  return d;
}

function paramToDate(s: string | undefined): string | null {
  return s ?? null;
}

export function QueryHistoryPage() {
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
    source: search.source,
    sensitiveColumn: search.sensitiveColumn,
  };
  const history = useQueryHistory(filters);

  const databaseOptions = [
    { value: "", label: "All databases" },
    ...(servers.data?.servers ?? []).flatMap((s) =>
      s.databases.map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
    ),
  ];

  const sensitiveColumnOptions = useMemo(() => {
    const cols = new Set<string>();
    for (const item of history.data?.items ?? []) {
      for (const sc of item.sensitiveColumns) {
        cols.add(sc);
      }
    }
    return [
      { value: "any", label: "Any sensitive column" },
      ...[...cols].sort().map((c) => ({ value: c, label: c })),
    ];
  }, [history.data]);

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
        <Select
          label="Source"
          size="sm"
          data={SOURCE_OPTIONS}
          value={search.source ?? ""}
          onChange={(v) => setFilter("source", v ?? undefined)}
          style={{ width: 140 }}
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
        <Stack gap="sm" data-testid="history-entries">
          {displayedItems.map((item) => (
            <HistoryEntry key={item.id} item={item} canAudit={canAudit} />
          ))}
        </Stack>
      )}
    </Stack>
  );
}

function HistoryEntry({ item, canAudit }: { item: QueryHistoryItem; canAudit: boolean }) {
  function copySql() {
    void navigator.clipboard.writeText(item.queryText).then(() => {
      notifications.show({ message: "SQL copied to clipboard", color: "teal" });
    }).catch(() => {
      // Clipboard API unavailable — silent no-op per spec
    });
  }

  const statusColor = STATUS_COLOR[item.status] ?? "gray";
  const executedAt = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" })
    .format(new Date(item.executedAt));
  const metrics = [
    item.durationMs != null ? `${item.durationMs} ms` : null,
    item.rowCount != null ? `${item.rowCount} ${item.rowCount === 1 ? "row" : "rows"}` : null,
  ].filter(Boolean).join(" · ");

  return (
    <Paper
      withBorder
      radius="md"
      p="sm"
      style={{ borderLeft: `3px solid var(--mantine-color-${statusColor}-6)` }}
    >
      <Group justify="space-between" gap="sm" mb="xs">
        <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
          <Text ff="monospace" fw={700} size="xs" tt="uppercase" c={statusColor}>
            {item.status}
          </Text>
          <Text size="sm" fw={500} truncate>
            {item.databaseDisplayName ?? "—"}
            {item.serverName && (
              <Text span c="dimmed" fw={400} ml={6}>
                {item.serverName}
              </Text>
            )}
          </Text>
          {item.source === "Mcp" ? (
            <Tooltip label="From MCP">
              <IconPlugConnected size={14} color="var(--mantine-color-dimmed)" />
            </Tooltip>
          ) : (
            <Tooltip label="From UI">
              <IconDeviceDesktop size={14} color="var(--mantine-color-dimmed)" />
            </Tooltip>
          )}
          {item.sensitiveColumns.length > 0 && (
            <Tooltip label={item.sensitiveColumns.join(", ")} multiline maw={360}>
              <IconShieldLock size={14} color="var(--mantine-color-yellow-6)" />
            </Tooltip>
          )}
        </Group>
        <Group gap="md" wrap="nowrap" style={{ flexShrink: 0 }}>
          {canAudit && (
            <Text size="xs" c="dimmed">{item.userName ?? "—"}</Text>
          )}
          <Text size="xs" c="dimmed">{executedAt}</Text>
          {metrics && (
            <Text size="xs" c="dimmed" ff="monospace">{metrics}</Text>
          )}
          <ActionIcon size="sm" variant="subtle" color="gray" onClick={copySql} aria-label="Copy SQL">
            <IconCopy size={14} />
          </ActionIcon>
        </Group>
      </Group>
      {item.error && (
        <Alert
          variant="light"
          color={statusColor}
          icon={<IconAlertTriangle size={16} />}
          p="xs"
          mb="xs"
        >
          <Text size="xs" ff="monospace" style={{ whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
            {item.error}
          </Text>
        </Alert>
      )}
      <SqlEditor
        value={item.queryText}
        readOnly
        editable={false}
        height="auto"
        maxHeight="240px"
      />
    </Paper>
  );
}
