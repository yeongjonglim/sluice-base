import {
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  Kbd,
  NavLink,
  ScrollArea,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  Tooltip,
  useComputedColorScheme,
} from "@mantine/core";
import { Group as PanelGroup, Panel, Separator as PanelResizeHandle } from "react-resizable-panels";
import {
  IconChevronDown,
  IconChevronRight,
  IconDatabase,
  IconDownload,
  IconPlayerPlay,
  IconPlaylistAdd,
  IconTable,
} from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import React, { useCallback, useEffect, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { keymap } from "@codemirror/view";
import { Prec } from "@codemirror/state";
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror";
import type { ExecuteQueryResponse } from "@/api/hooks";
import { meQueryOptions, useExecuteQuery, useSchema, useServers } from "@/api/hooks";

export function buildCsv(
  columns: string[],
  rows: (string | null | undefined)[][],
): string {
  const escape = (val: string | null | undefined): string => {
    const s = val == null ? "" : String(val);
    if (s.includes(",") || s.includes('"') || s.includes("\n")) {
      return `"${s.replace(/"/g, '""')}"`;
    }
    return s;
  };
  const lines = [
    columns.map(escape).join(","),
    ...rows.map((row) => row.map(escape).join(",")),
  ];
  return lines.join("\n");
}

export function exportToCsv(
  columns: string[],
  rows: (string | null | undefined)[][],
  filename: string,
): void {
  const csv = buildCsv(columns, rows);
  const blob = new Blob([csv], { type: "text/csv" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

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
      ? { width: 4, cursor: "col-resize", borderLeft: "1px solid var(--mantine-color-default-border)" }
      : { height: 4, cursor: "row-resize", borderTop: "1px solid var(--mantine-color-default-border)" }),
  };
}

function QueryPage() {
  const servers = useServers();
  const [selectedDatabaseId, setSelectedDatabaseId] = useState<string | null>(null);
  const schema = useSchema(selectedDatabaseId);
  const [editorContent, setEditorContent] = useState("");
  const editorRef = useRef<ReactCodeMirrorRef>(null);
  const executeQuery = useExecuteQuery();
  const computedColorScheme = useComputedColorScheme();

  const databaseOptions = (servers.data?.servers ?? [])
    .filter((s) => !s.isDisabled)
    .flatMap((s) =>
      s.databases
        .filter((d) => !d.isDisabled)
        .map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
    );

  const handleTableClick = useCallback(
    (schemaName: string, tableName: string, columns: Array<{ name: string }>) => {
      const colList = columns.map((c) => c.name).join(", ");
      const snippet = `SELECT ${colList}\nFROM ${schemaName}.${tableName}\nLIMIT 1000;\n`;
      setEditorContent((prev) =>
        prev.trimEnd() === "" ? snippet : `${prev.trimEnd()}\n\n${snippet}`,
      );
    },
    [],
  );

  const handleRun = useCallback(() => {
    if (selectedDatabaseId && editorContent.trim()) {
      executeQuery.mutate({ databaseId: selectedDatabaseId, sql: editorContent });
    }
  }, [selectedDatabaseId, editorContent, executeQuery]);

  const runKeymap = Prec.highest(
    keymap.of([
      {
        key: "Ctrl-Enter",
        mac: "Cmd-Enter",
        run: () => { handleRun(); return true; },
      },
      {
        key: "F5",
        run: () => { handleRun(); return true; },
      },
    ]),
  );

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "F5") {
        e.preventDefault();
        handleRun();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [handleRun]);

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
          <Select
            placeholder="Select a database"
            data={databaseOptions}
            value={selectedDatabaseId}
            onChange={setSelectedDatabaseId}
            mb="xs"
            size="sm"
          />
          <SchemaSidebar schema={schema} onTableClick={handleTableClick} />
        </Stack>
      </Panel>

      <PanelResizeHandle style={resizeHandleStyle("vertical")} />

      <Panel minSize="30%" style={{ display: "flex", flexDirection: "column", minWidth: 0 }}>
        <PanelGroup orientation="vertical">
          <Panel defaultSize="35%" minSize="15%" style={{ display: "flex", flexDirection: "column" }}>
            <Box
              p="xs"
              style={{
                borderBottom: "1px solid var(--mantine-color-default-border)",
                display: "flex",
                flexDirection: "column",
                height: "100%",
              }}
            >
              <Box
                style={{
                  border: "1px solid var(--mantine-color-default-border)",
                  borderRadius: "var(--mantine-radius-sm)",
                  overflow: "hidden",
                  flex: 1,
                  minHeight: 0,
                }}
              >
                <CodeMirror
                  ref={editorRef}
                  value={editorContent}
                  onChange={setEditorContent}
                  extensions={[sql(), runKeymap]}
                  theme={computedColorScheme === "dark" ? githubDark : githubLight}
                  height="100%"
                  style={{ height: "100%" }}
                  basicSetup={{ lineNumbers: true, foldGutter: false }}
                />
              </Box>

              <Group mt="xs" gap="xs" style={{ flexShrink: 0 }}>
                <Button
                  leftSection={<IconPlayerPlay size={14} />}
                  rightSection={<Kbd size="xs">F5</Kbd>}
                  size="sm"
                  onClick={handleRun}
                  loading={executeQuery.isPending}
                  disabled={!selectedDatabaseId || !editorContent.trim()}
                >
                  Run
                </Button>
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
}: {
  result: ExecuteQueryResponse | null;
  isPending: boolean;
  isError: boolean;
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
          onClick={() =>
            exportToCsv(columns, rows, `query-results-${Date.now()}.csv`)
          }
        >
          CSV
        </Button>
      </Group>
      <ScrollArea style={{ flex: 1, minHeight: 0 }} type="auto">
        <Table stickyHeader striped withTableBorder withColumnBorders fz="xs" style={{ whiteSpace: "nowrap" }}>
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
                  <Table.Td key={j}>{cell}</Table.Td>
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
  columns: Array<{ name: string }>,
) => void;

function SchemaSidebar({
  schema,
  onTableClick,
}: {
  schema: ReturnType<typeof useSchema>;
  onTableClick: TableClickHandler;
}) {
  const [expandedSchemas, setExpandedSchemas] = useState<Set<string>>(new Set());
  const [expandedTables, setExpandedTables] = useState<Set<string>>(new Set());

  if (schema.isFetching) {
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
    setExpandedSchemas((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }

  function toggleTable(key: string) {
    setExpandedTables((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  return (
    <Stack gap={0}>
      {schema.data.schemas.map((s) => {
        const schemaExpanded = expandedSchemas.has(s.name);
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
                const tableExpanded = expandedTables.has(tableKey);
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
                        <Button onClick={() => onTableClick(s.name, t.name, t.columns)} size="xs" variant="subtle">
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
                          <Group key={c.name} gap="xs" px="xs" py={2} wrap="nowrap">
                            <Text size="xs" style={{ minWidth: 0 }}>
                              {c.name}
                            </Text>
                            <Code fz="xs">{c.dataType}</Code>
                            {c.isNullable && (
                              <Text size="xs" c="dimmed">
                                null
                              </Text>
                            )}
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
