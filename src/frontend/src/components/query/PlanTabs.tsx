import { useState } from "react";
import { Box, Group, Tabs, Text, Tooltip } from "@mantine/core";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { PlanView } from "@/components/query/PlanView";

function snippet(text: string, max = 36): string {
  const oneLine = text.replace(/\s+/g, " ").trim();
  return oneLine.length > max ? `${oneLine.slice(0, max - 1)}…` : oneLine;
}

export function PlanTabs({
  runs,
  onHighlight,
}: {
  runs: Array<ExplainEntry>;
  onHighlight: (entry: ExplainEntry) => void;
}) {
  const [active, setActive] = useState<string | null>(runs[0]?.id ?? null);

  if (runs.length === 0) {
    return <Text p="xs" size="sm" c="dimmed">Explain a query to see its plan.</Text>;
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
            <Tooltip label={entry.text} multiline maw={480} withArrow openDelay={400}>
              <Group gap={6} wrap="nowrap">
                <Text size="xs" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
                  {snippet(entry.text)}
                </Text>
              </Group>
            </Tooltip>
          </Tabs.Tab>
        ))}
      </Tabs.List>
      <Box style={{ flex: 1, minHeight: 0, overflow: "auto" }}>
        <PlanView entry={activeEntry} />
      </Box>
    </Tabs>
  );
}
