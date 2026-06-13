import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  Kbd,
  NavLink,
  Popover,
  ScrollArea,
  Skeleton,
  Stack,
  Table,
  Text,
  Tooltip,
} from "@mantine/core";
import { useOs } from "@mantine/hooks";
import { Panel, Group as PanelGroup, Separator as PanelResizeHandle } from "react-resizable-panels";
import {
  IconChevronDown,
  IconChevronRight,
  IconDatabase,
  IconDownload,
  IconLock,
  IconPlayerPlay,
  IconPlaylistAdd,
  IconQuestionMark,
  IconShieldLock,
  IconTable,
} from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import React, { useCallback, useMemo, useRef } from "react";
import { keymap } from "@codemirror/view";
import { Prec } from "@codemirror/state";
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror";
import type { ExecuteQueryResponse } from "@/api/hooks";
import { ApiError } from "@/api/client";
import { exportToCsv } from "@/utils/csv.ts";
import { SqlEditor } from "@/components/SqlEditor";
import { useSessionState } from "@/utils/useSessionState";
import { meQueryOptions, useExecuteQuery, useSchema } from "@/api/hooks";
import { DatabaseSelect } from "@/components/DatabaseSelect";

const noIndentKeymap = Prec.highest(
  keymap.of([
    {
      key: "Enter",
      run: (view) => {
        const { from, to } = view.state.selection.main;

        view.dispatch({
          changes: { from, to, insert: "\n" },
          selection: { anchor: from + 1 },
        });

        return true;
      },
    },
  ]),
);

export const Route = createFileRoute("/_authed/query/")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryPage,
});

function resizeHandleStyle(orientation: "horizontal" | "vertical"): React.CSSProperties {
  return {
    position: "relative",
    background: "transparent",
    ...(orientation === "vertical"
      ? {
          width: 4,
          cursor: "col-resize",
          borderLeft: "1px solid var(--mantine-color-default-border)",
        }
      : {
          height: 4,
          cursor: "row-resize",
          borderTop: "1px solid var(--mantine-color-default-border)",
        }),
  };
}

function QueryPage() {
  const isMac = useOs({ getValueInEffect: false }) === "macos";
  const [selectedDatabaseId, setSelectedDatabaseId] = useSessionState<string | null>(
    "sluice:query:db",
    null,
  );
  const schema = useSchema(selectedDatabaseId);
  const [editorContent, setEditorContent] = useSessionState("sluice:query:editor", "");
  const editorRef = useRef<ReactCodeMirrorRef>(null);
  const executeQuery = useExecuteQuery();

  const handleTableClick = useCallback(
    (schemaName: string, tableName: string, columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>) => {
      const safeCols = columns.filter((c) => !c.isSensitive);
      if (safeCols.length === 0) return;
      const colList = safeCols.map((c) => c.name).join(", ");
      const snippet = `SELECT ${colList}\nFROM ${schemaName}.${tableName}\nLIMIT 1000;\n`;
      setEditorContent((prev) =>
        prev.trimEnd() === "" ? snippet : `${prev.trimEnd()}\n\n${snippet}`,
      );
    },
    [setEditorContent],
  );

  const handleRun = useCallback(() => {
    if (selectedDatabaseId && editorContent.trim()) {
      executeQuery.mutate({ databaseId: selectedDatabaseId, sql: editorContent.trim() });
    }
  }, [selectedDatabaseId, editorContent, executeQuery]);

  const runKeymap = Prec.highest(
    keymap.of([
      {
        key: "Ctrl-Enter",
        mac: "Cmd-Enter",
        run: () => {
          handleRun();
          return true;
        },
      },
    ]),
  );

  const editorExtensions = useMemo(
    () => [runKeymap, noIndentKeymap],
    [runKeymap],
  );

  return (
    <PanelGroup
      orientation="horizontal"
      style={{
        margin: "calc(-1 * var(--mantine-spacing-sm))",
        height: "calc(100vh - 44px)",
      }}
    >
      <Panel defaultSize="20%" minSize="12%" style={{ overflow: "auto" }}>
        <Stack gap={0} p="xs">
          <Box mb="xs">
            <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
          </Box>
          <SchemaSidebar schema={schema} onTableClick={handleTableClick} />
        </Stack>
      </Panel>

      <PanelResizeHandle style={resizeHandleStyle("vertical")} />

      <Panel minSize="30%" style={{ display: "flex", flexDirection: "column", minWidth: 0 }}>
        <PanelGroup orientation="vertical">
          <Panel
            defaultSize="35%"
            minSize="15%"
            style={{ display: "flex", flexDirection: "column" }}
          >
            <Box
              p="xs"
              style={{
                borderBottom: "1px solid var(--mantine-color-default-border)",
                display: "flex",
                flexDirection: "column",
                height: "100%",
              }}
            >
              <SqlEditor
                ref={editorRef}
                value={editorContent}
                onChange={setEditorContent}
                databaseId={selectedDatabaseId}
                extensions={editorExtensions}
                minLines={20}
                height="100%"
                style={{ flex: 1, minHeight: 0 }}
              />

              <Group mt="xs" gap="xs" style={{ flexShrink: 0 }}>
                <Button
                  leftSection={<IconPlayerPlay size={14} />}
                  rightSection={<Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+Enter</Kbd>}
                  size="sm"
                  onClick={handleRun}
                  loading={executeQuery.isPending}
                  disabled={!selectedDatabaseId || !editorContent.trim()}
                >
                  Run
                </Button>
                <Popover position="bottom-start" withArrow shadow="md">
                  <Popover.Target>
                    <ActionIcon variant="subtle" size="sm" color="gray" aria-label="Keyboard shortcuts">
                      <IconQuestionMark size={14} />
                    </ActionIcon>
                  </Popover.Target>
                  <Popover.Dropdown>
                    <Stack gap={4}>
                      <Text size="sm" fw={600} mb={2}>Keyboard shortcuts</Text>
                      <Group gap="xs" justify="space-between"><Text size="xs">Run query</Text><Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+Enter</Kbd></Group>
                      <Group gap="xs" justify="space-between"><Text size="xs">Toggle comment</Text><Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+/</Kbd></Group>
                      <Group gap="xs" justify="space-between"><Text size="xs">Move line up/down</Text><Kbd size="xs">Alt+↑/↓</Kbd></Group>
                      <Group gap="xs" justify="space-between"><Text size="xs">Copy line up/down</Text><Kbd size="xs">Shift+Alt+↑/↓</Kbd></Group>
                      <Group gap="xs" justify="space-between"><Text size="xs">Select line</Text><Kbd size="xs">{isMac ? "Ctrl" : "Alt"}+L</Kbd></Group>
                      <Group gap="xs" justify="space-between"><Text size="xs">Indent / Outdent</Text><Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+] / [</Kbd></Group>
                      <Group gap="xs" justify="space-between"><Text size="xs">Delete line</Text><Kbd size="xs">Shift+{isMac ? "⌘" : "Ctrl"}+K</Kbd></Group>
                    </Stack>
                  </Popover.Dropdown>
                </Popover>
                {!selectedDatabaseId && (
                  <Text size="xs" c="dimmed">
                    Select a database to run queries
                  </Text>
                )}
              </Group>
            </Box>
          </Panel>

          <PanelResizeHandle style={resizeHandleStyle("horizontal")} />

          <Panel minSize="15%" style={{ overflow: "hidden" }}>
            <QueryResults
              result={executeQuery.data ?? null}
              isPending={executeQuery.isPending}
              isError={executeQuery.isError}
              error={executeQuery.error}
            />
          </Panel>
        </PanelGroup>
      </Panel>
    </PanelGroup>
  );
}

function QueryResults({
  result,
  isPending,
  isError,
  error,
}: {
  result: ExecuteQueryResponse | null;
  isPending: boolean;
  isError: boolean;
  error: unknown;
}) {
  if (isPending) {
    return (
      <Stack p="xs" gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (isError) {
    const apiErr = error instanceof ApiError ? error : null;
    if (apiErr?.status === 403) {
      const body = apiErr.body as {
        type?: string;
        columns?: Array<{ schema: string; table: string; column: string }>;
      } | null;
      if (body?.type === "sensitive_columns") {
        return (
          <Alert color="orange" title="Query blocked — restricted columns" m="xs">
            <Text size="sm" mb="xs">
              Your query references columns you are not authorised to access:
            </Text>
            {(body.columns ?? []).map((c, i) => (
              <Code key={i} display="block" fz="xs">
                {c.schema}.{c.table}.{c.column}
              </Code>
            ))}
          </Alert>
        );
      }
    }
    return (
      <Alert color="red" title="Request failed" m="xs">
        Could not reach the server. Check your connection and try again.
      </Alert>
    );
  }

  if (!result) {
    return (
      <Text p="xs" size="sm" c="dimmed">
        Run a query to see results.
      </Text>
    );
  }

  if (result.error) {
    return (
      <Stack p="xs" gap="xs">
        <Text size="xs" c="dimmed">
          Error · {result.durationMs} ms
        </Text>
        <Alert color="red" title="Query error">
          {result.error}
        </Alert>
      </Stack>
    );
  }

  const columns = result.columns ?? [];
  const rows = result.rows ?? [];

  return (
    <Flex direction="column" style={{ height: "100%" }}>
      <Group
        justify="space-between"
        align="center"
        px="xs"
        style={{
          flexShrink: 0,
          height: 32,
          borderBottom: "1px solid var(--mantine-color-default-border)",
        }}
      >
        <Text size="xs" c="dimmed">
          {result.rowCount} {result.rowCount === 1 ? "row" : "rows"} · {result.durationMs} ms
        </Text>
        <Button
          size="xs"
          variant="subtle"
          leftSection={<IconDownload size={12} />}
          onClick={() => exportToCsv(columns, rows, `query-results-${Date.now()}.csv`)}
        >
          CSV
        </Button>
      </Group>
      <ScrollArea style={{ flex: 1, minHeight: 0 }} type="auto">
        <Table
          stickyHeader
          striped
          withTableBorder
          withColumnBorders
          fz="xs"
          style={{ whiteSpace: "nowrap" }}
        >
          <Table.Thead>
            <Table.Tr>
              {columns.map((col) => (
                <Table.Th key={col}>{col}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row, i) => (
              <Table.Tr key={i}>
                {row.map((cell, j) => (
                  <Table.Td key={j}>
                    {cell === null ? (
                      <Text size="xs" c="dimmed" fs="italic">NULL</Text>
                    ) : cell}
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>
    </Flex>
  );
}

type TableClickHandler = (
  schemaName: string,
  tableName: string,
  columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>,
) => void;

function SchemaSidebar({
  schema,
  onTableClick,
}: {
  schema: ReturnType<typeof useSchema>;
  onTableClick: TableClickHandler;
}) {
  const [expandedSchemas, setExpandedSchemas] = useSessionState<Array<string>>(
    "sluice:query:expandedSchemas",
    [],
  );
  const [expandedTables, setExpandedTables] = useSessionState<Array<string>>(
    "sluice:query:expandedTables",
    [],
  );

  if (schema.isLoading) {
    return (
      <Stack gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (schema.isError) {
    return (
      <Alert color="red" title="Schema load failed" mt="xs">
        Could not connect to the server.
      </Alert>
    );
  }

  if (!schema.data) {
    return (
      <Text size="sm" c="dimmed" p="xs">
        Select a database to browse its schema.
      </Text>
    );
  }

  function toggleSchema(name: string) {
    setExpandedSchemas((prev) =>
      prev.includes(name) ? prev.filter((s) => s !== name) : [...prev, name],
    );
  }

  function toggleTable(key: string) {
    setExpandedTables((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key],
    );
  }

  return (
    <Stack gap={0}>
      {schema.data.schemas.map((s) => {
        const schemaExpanded = expandedSchemas.includes(s.name);
        return (
          <div key={s.name}>
            <NavLink
              label={s.name}
              leftSection={<IconDatabase size={14} />}
              rightSection={
                schemaExpanded ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />
              }
              onClick={() => toggleSchema(s.name)}
              active={false}
            />
            {schemaExpanded &&
              s.tables.map((t) => {
                const tableKey = `${s.name}.${t.name}`;
                const tableExpanded = expandedTables.includes(tableKey);
                return (
                  <div key={tableKey}>
                    <Group gap="xs" justify="space-between" wrap="nowrap">
                      <NavLink
                        label={t.name}
                        leftSection={<IconTable size={14} />}
                        rightSection={
                          tableExpanded ? (
                            <IconChevronDown size={12} />
                          ) : (
                            <IconChevronRight size={12} />
                          )
                        }
                        onClick={() => toggleTable(tableKey)}
                        pl="lg"
                        active={false}
                      />
                      <Tooltip label="Append SELECT query" position="right" withArrow>
                        <Button
                          onClick={() => onTableClick(s.name, t.name, t.columns)}
                          size="xs"
                          variant="subtle"
                          disabled={t.columns.every((c) => c.isSensitive)}
                        >
                          <IconPlaylistAdd />
                        </Button>
                      </Tooltip>
                    </Group>
                    {tableExpanded && (
                      <Stack
                        gap={0}
                        pl="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-xs))"
                      >
                        {t.columns.map((c) => (
                          <Group
                            key={c.name}
                            gap="xs"
                            px="xs"
                            py={2}
                            wrap="nowrap"
                            style={c.isRestricted ? { opacity: 0.45 } : undefined}
                          >
                            <Text size="xs" style={{ minWidth: 0 }}>
                              {c.name}
                            </Text>
                            <Code fz="xs">{c.dataType}</Code>
                            {c.isNullable && (
                              <Text size="xs" c="dimmed">
                                null
                              </Text>
                            )}
                            {c.isRestricted ? (
                              <Tooltip label="Restricted — you cannot access this column" withArrow>
                                <IconLock size={10} color="var(--mantine-color-red-6)" />
                              </Tooltip>
                            ) : c.isSensitive ? (
                              <Tooltip label="Sensitive — excluded from generated queries" withArrow>
                                <IconShieldLock size={10} color="var(--mantine-color-yellow-6)" />
                              </Tooltip>
                            ) : null}
                          </Group>
                        ))}
                      </Stack>
                    )}
                  </div>
                );
              })}
          </div>
        );
      })}
    </Stack>
  );
}
