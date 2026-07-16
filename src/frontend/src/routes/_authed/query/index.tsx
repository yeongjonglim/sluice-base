import {
  ActionIcon,
  Box,
  Button,
  Group,
  Kbd,
  Popover,
  Splitter,
  Stack,
  Text,
} from "@mantine/core";
import { useOs } from "@mantine/hooks";
import {
  IconPlayerPlay,
  IconPlayerTrackNext,
  IconQuestionMark,
} from "@tabler/icons-react";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useCallback, useMemo, useRef, useState } from "react";
import { keymap } from "@codemirror/view";
import { Prec } from "@codemirror/state";
import type { EditorView } from "@codemirror/view";
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror";
import { SqlEditor } from "@/components/SqlEditor";
import { useSessionState } from "@/utils/useSessionState";
import { meQueryOptions, useSchema } from "@/api/hooks";
import { DatabaseSelect } from "@/components/DatabaseSelect";
import { SchemaSidebar } from "@/components/schema/SchemaSidebar";
import { useQueryRuns } from "@/api/useQueryRuns";
import { splitSqlStatements } from "@/utils/splitSqlStatements";
import { selectStatements } from "@/utils/selectStatements";
import { highlightStatementInEditor } from "@/utils/editorHighlight";
import { ResultTabs } from "@/components/query/ResultTabs";

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
  const { runs, run, isRunning } = useQueryRuns();
  // Which button launched the in-flight run, so only that button shows the
  // spinner. No reset needed: each button's loading is gated on `isRunning`,
  // which flips back to false when the run settles.
  const [activeRun, setActiveRun] = useState<"single" | "all">("single");

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

  const statements = useMemo(() => splitSqlStatements(editorContent), [editorContent]);

  const handleRun = useCallback(
    (runAll: boolean, view: EditorView | null | undefined) => {
      if (!selectedDatabaseId || statements.length === 0) return;
      const sel = view
        ? {
            from: view.state.selection.main.from,
            to: view.state.selection.main.to,
            empty: view.state.selection.main.empty,
          }
        : { from: 0, to: 0, empty: true };
      const targets = selectStatements(statements, sel, runAll);
      if (targets.length > 0) {
        setActiveRun(runAll ? "all" : "single");
        run(selectedDatabaseId, targets);
      }
    },
    [selectedDatabaseId, statements, run],
  );

  const handleHighlight = useCallback((entry: { fromPos: number; toPos: number }) => {
    const view = editorRef.current?.view;
    if (view) highlightStatementInEditor(view, entry.fromPos, entry.toPos);
  }, []);

  const runKeymap = Prec.highest(
    keymap.of([
      {
        key: "Ctrl-Enter",
        mac: "Cmd-Enter",
        run: (view) => {
          handleRun(false, view);
          return true;
        },
      },
      {
        key: "Ctrl-Shift-Enter",
        mac: "Cmd-Shift-Enter",
        run: (view) => {
          handleRun(true, view);
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
                  onClick={() => handleRun(false, editorRef.current?.view)}
                  loading={isRunning && activeRun === "single"}
                  disabled={!selectedDatabaseId || statements.length === 0 || isRunning}
                >
                  Run
                </Button>
                <Button
                  variant="default"
                  leftSection={<IconPlayerTrackNext size={14} />}
                  rightSection={<Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+Shift+Enter</Kbd>}
                  size="sm"
                  onClick={() => handleRun(true, editorRef.current?.view)}
                  loading={isRunning && activeRun === "all"}
                  disabled={!selectedDatabaseId || statements.length === 0 || isRunning}
                >
                  Run all{statements.length > 1 ? ` (${statements.length})` : ""}
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
                      <Group gap="xs" justify="space-between"><Text size="xs">Run all statements</Text><Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+Shift+Enter</Kbd></Group>
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
            <ResultTabs runs={runs} onHighlight={handleHighlight} />
          </Splitter.Pane>
        </Splitter>
      </Splitter.Pane>
    </Splitter>
  );
}

