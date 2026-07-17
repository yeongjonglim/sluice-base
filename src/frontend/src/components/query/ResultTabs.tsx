import { useState } from "react";
import { Box, Group, Tabs, Text, Tooltip } from "@mantine/core";
import type { RunEntry } from "@/api/useQueryRuns";
import { ResultGrid } from "@/components/query/ResultGrid";

function snippet(text: string, max = 36): string {
  const oneLine = text.replace(/\s+/g, " ").trim();
  return oneLine.length > max ? `${oneLine.slice(0, max - 1)}…` : oneLine;
}

function TabLabel({ entry }: { entry: RunEntry }) {
  const failed = entry.status === "error" || entry.status === "blocked";
  return (
    <Tooltip label={entry.text} multiline maw={480} withArrow openDelay={400}>
      <Group gap={6} wrap="nowrap">
        {failed && (
          <Box
            w={7}
            h={7}
            style={{ borderRadius: "50%", background: "var(--mantine-color-red-6)", flexShrink: 0 }}
          />
        )}
        <Text size="xs" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
          {snippet(entry.text)}
        </Text>
        {entry.status === "success" && (
          <Text size="xs" c="dimmed">
            {entry.response?.rowCount ?? 0}
          </Text>
        )}
      </Group>
    </Tooltip>
  );
}

export function ResultTabs({
  runs,
  onHighlight,
}: {
  runs: Array<RunEntry>;
  onHighlight: (entry: RunEntry) => void;
}) {
  // `active` is the user's explicitly-clicked tab id; the rendered tab is
  // derived below with a fallback, so a new run batch (whose ids won't match a
  // stale `active`) transparently falls back to the first tab — no effect needed.
  const [active, setActive] = useState<string | null>(runs[0]?.id ?? null);

  if (runs.length === 0) {
    return (
      <Text p="xs" size="sm" c="dimmed">
        Run a query to see results.
      </Text>
    );
  }

  const activeEntry = runs.find((r) => r.id === active) ?? runs[0];

  return (
    <Tabs
      value={activeEntry.id}
      onChange={(value) => {
        if (!value) return;
        setActive(value);
        const entry = runs.find((r) => r.id === value);
        if (entry) onHighlight(entry);
      }}
      keepMounted={false}
      style={{ display: "flex", flexDirection: "column", height: "100%" }}
    >
      <Tabs.List style={{ flexShrink: 0, flexWrap: "nowrap", overflowX: "auto" }}>
        {runs.map((entry) => (
          <Tabs.Tab key={entry.id} value={entry.id}>
            <TabLabel entry={entry} />
          </Tabs.Tab>
        ))}
      </Tabs.List>
      <Box style={{ flex: 1, minHeight: 0 }}>
        <ResultGrid entry={activeEntry} />
      </Box>
    </Tabs>
  );
}
