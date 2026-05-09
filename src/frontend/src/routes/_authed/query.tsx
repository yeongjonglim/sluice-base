import {
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  NavLink,
  ScrollArea,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  useMantineColorScheme,
} from "@mantine/core";
import {
  IconChevronDown,
  IconChevronRight,
  IconDatabase,
  IconPlayerPlay,
  IconTable,
} from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useCallback, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { keymap } from "@codemirror/view";
import { Prec } from "@codemirror/state";
import type {ReactCodeMirrorRef} from "@uiw/react-codemirror";
import type {ExecuteQueryResponse} from "@/api/hooks";
import {
  meQueryOptions,
  useExecuteQuery,
  useSchema,
  useServers
} from "@/api/hooks";

export const Route = createFileRoute("/_authed/query")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: QueryPage,
});

function QueryPage() {
  const servers = useServers();
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const schema = useSchema(selectedServerId);
  const [editorContent, setEditorContent] = useState("");
  const editorRef = useRef<ReactCodeMirrorRef>(null);
  const executeQuery = useExecuteQuery();
  const { colorScheme } = useMantineColorScheme();

  const serverOptions = (servers.data?.servers ?? []).map((s) => ({
    value: s.id,
    label: s.name,
  }));

  const handleTableClick = useCallback(
    (schemaName: string, tableName: string, columns: Array<{ name: string }>) => {
      const colList = columns.map((c) => c.name).join(", ");
      const snippet = `SELECT ${colList}\nFROM ${schemaName}.${tableName}\nLIMIT 100;\n`;
      setEditorContent((prev) =>
        prev.trimEnd() === "" ? snippet : `${prev.trimEnd()}\n\n${snippet}`,
      );
    },
    [],
  );

  const runKeymap = Prec.highest(
    keymap.of([
      {
        key: "Ctrl-Enter",
        mac: "Cmd-Enter",
        run: () => {
          if (selectedServerId && editorContent.trim()) {
            executeQuery.mutate({ serverId: selectedServerId, sql: editorContent });
          }
          return true;
        },
      },
    ]),
  );

  const handleRun = () => {
    if (selectedServerId && editorContent.trim()) {
      executeQuery.mutate({ serverId: selectedServerId, sql: editorContent });
    }
  };

  return (
    <Flex h="calc(100vh - 90px)" style={{ overflow: "hidden" }}>
      <Box
        w={280}
        style={{
          borderRight: "1px solid var(--mantine-color-default-border)",
          overflow: "auto",
          flexShrink: 0,
        }}
      >
        <Stack gap={0} p="xs">
          <Select
            placeholder="Select a server"
            data={serverOptions}
            value={selectedServerId}
            onChange={setSelectedServerId}
            mb="xs"
            size="sm"
          />
          <SchemaSidebar schema={schema} onTableClick={handleTableClick} />
        </Stack>
      </Box>

      <Box flex={1} style={{ overflowY: "auto" }}>
        <Stack gap={0} p="md">
          <Box
            style={{
              border: "1px solid var(--mantine-color-default-border)",
              borderRadius: "var(--mantine-radius-sm)",
              overflow: "hidden",
            }}
          >
            <CodeMirror
              ref={editorRef}
              value={editorContent}
              onChange={setEditorContent}
              extensions={[sql(), runKeymap]}
              theme={colorScheme === "dark" ? githubDark : githubLight}
              height="300px"
              basicSetup={{ lineNumbers: true, foldGutter: false }}
            />
          </Box>

          <Group mt="xs" gap="xs">
            <Button
              leftSection={<IconPlayerPlay size={14} />}
              size="sm"
              onClick={handleRun}
              loading={executeQuery.isPending}
              disabled={!selectedServerId || !editorContent.trim()}
            >
              Run
            </Button>
            {!selectedServerId && (
              <Text size="xs" c="dimmed">
                Select a server to run queries
              </Text>
            )}
          </Group>

          <QueryResults
            result={executeQuery.data ?? null}
            isPending={executeQuery.isPending}
            isError={executeQuery.isError}
          />
        </Stack>
      </Box>
    </Flex>
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
      <Stack mt="md" gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={28} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (isError) {
    return (
      <Alert color="red" title="Request failed" mt="md">
        Could not reach the server. Check your connection and try again.
      </Alert>
    );
  }

  if (!result) {
    return (
      <Text mt="md" size="sm" c="dimmed">
        Run a query to see results.
      </Text>
    );
  }

  if (result.error) {
    return (
      <Stack mt="md" gap="xs">
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
    <Stack mt="md" gap="xs">
      <Text size="xs" c="dimmed">
        {result.rowCount} {result.rowCount === 1 ? "row" : "rows"} · {result.durationMs} ms
      </Text>
      <ScrollArea type="auto">
        <Table striped withTableBorder withColumnBorders fz="xs" style={{ whiteSpace: "nowrap" }}>
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
                    {cell}
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>
    </Stack>
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
        Select a server to browse its schema.
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
                      onClick={() => {
                        toggleTable(tableKey);
                        onTableClick(s.name, t.name, t.columns);
                      }}
                      pl="lg"
                      active={false}
                    />
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
