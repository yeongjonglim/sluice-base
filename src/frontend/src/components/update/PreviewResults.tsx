import { Alert, Tabs, Text } from "@mantine/core";
import type { UpdatePreviewResponse } from "@/api/hooks";
import { ResultTable } from "@/components/query/ResultTable";

export function PreviewResults({ result }: { result: UpdatePreviewResponse }) {
  if (result.error) {
    return (
      <Alert color="red" title="Preview error" m="sm">
        {result.error}
      </Alert>
    );
  }

  const sets = result.resultSets;

  // Conditional phrasing — a preview is a dry run, so nothing is ever committed.
  const summary = `${result.affectedRows} rows would change · ${result.durationMs} ms · rolled back — nothing was committed`;

  if (sets.length === 0) {
    return (
      <Text size="sm" c="dimmed" p="md">
        {summary}
      </Text>
    );
  }

  // Mirrors the query playground's fill pattern: a flex column that fills the
  // pane, so each result grid scrolls inside its own viewport rather than
  // growing the page.
  return (
    <Tabs
      defaultValue="0"
      keepMounted={false}
      style={{ display: "flex", flexDirection: "column", height: "100%" }}
    >
      <Tabs.List style={{ flexShrink: 0, flexWrap: "nowrap", overflowX: "auto" }}>
        {sets.map((_, i) => (
          <Tabs.Tab key={i} value={String(i)}>
            Result {i + 1}
          </Tabs.Tab>
        ))}
      </Tabs.List>
      <Text size="xs" c="dimmed" px="sm" py={4} style={{ flexShrink: 0 }}>
        {summary}
      </Text>
      {sets.map((set, i) => (
        <Tabs.Panel key={i} value={String(i)} style={{ flex: 1, minHeight: 0 }}>
          <ResultTable
            columns={set.columns}
            rows={set.rows}
            rowCount={set.rows.length}
            durationMs={Number(result.durationMs)}
            resultIndex={i}
          />
        </Tabs.Panel>
      ))}
    </Tabs>
  );
}
