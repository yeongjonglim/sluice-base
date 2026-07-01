import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  Kbd,
  Popover,
  ScrollArea,
  Skeleton,
  Splitter,
  Stack,
  Table,
  Text,
} from "@mantine/core";
import { useOs } from "@mantine/hooks";
import {
  IconDownload,
  IconPlayerPlay,
  IconQuestionMark,
} from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useCallback, useMemo, useRef } from "react";
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
import { SchemaSidebar } from "@/components/schema/SchemaSidebar";

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
    <Splitter
      orientation="horizontal"
      h="calc(100vh - 44px)"
      withHandle={false}
      handleColor="var(--mantine-color-default-border)"
      lineSize={4}
      style={{
        margin: "calc(-1 * var(--mantine-spacing-sm))",
      }}
    >
      <Splitter.Pane
        defaultSize={20}
        min={12}
        style={{ display: "flex", flexDirection: "column", overflow: "hidden" }}
      >
        {/* Database picker stays fixed at the panel width; it never scrolls with long names. */}
        <Box p="xs" style={{ flexShrink: 0 }}>
          <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
        </Box>
        {/* Schema tree scrolls on its own — long table/column names extend horizontally here.
            scrollbarGutter keeps the (hover-revealed) scrollbar from overlapping the sticky
            right-edge controls; the gutter is always reserved so nothing hides under it. */}
        <Box data-schema-scroll style={{ flex: 1, minHeight: 0, overflow: "auto", scrollbarGutter: "stable" }}>
          <Box miw="max-content" px="xs" pb="xs">
            <SchemaSidebar schema={schema} onTableClick={handleTableClick} />
          </Box>
        </Box>
      </Splitter.Pane>

      <Splitter.Pane defaultSize={80} min={30} style={{ display: "flex", flexDirection: "column", minWidth: 0 }}>
        <Splitter
          orientation="vertical"
          h="100%"
          withHandle={false}
          handleColor="var(--mantine-color-default-border)"
          lineSize={4}
          style={{ flex: 1 }}
        >
          <Splitter.Pane
            defaultSize={35}
            min={15}
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
          </Splitter.Pane>

          <Splitter.Pane defaultSize={65} min={15} style={{ overflow: "hidden" }}>
            <QueryResults
              result={executeQuery.data ?? null}
              isPending={executeQuery.isPending}
              isError={executeQuery.isError}
              error={executeQuery.error}
            />
          </Splitter.Pane>
        </Splitter>
      </Splitter.Pane>
    </Splitter>
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

