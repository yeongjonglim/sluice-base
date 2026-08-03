import { Alert, Box, Tabs, Text } from "@mantine/core";
import type { UpdatePreviewResponse } from "@/api/hooks";
import { ResultTable } from "@/components/query/ResultTable";

export function PreviewResults({ result }: { result: UpdatePreviewResponse }) {
  if (result.error) {
    return (
      <Alert color="red" title="Preview error" m="xs">
        {result.error}
      </Alert>
    );
  }

  const sets = result.resultSets;

  if (sets.length === 0) {
    return (
      <Text size="sm" c="dimmed" p="xs">
        {result.affectedRows} rows affected · {result.durationMs} ms · rolled back (no rows returned)
      </Text>
    );
  }

  return (
    <Tabs defaultValue="0" keepMounted={false}>
      <Tabs.List>
        {sets.map((_, i) => (
          <Tabs.Tab key={i} value={String(i)}>
            Result {i + 1}
          </Tabs.Tab>
        ))}
      </Tabs.List>
      {sets.map((set, i) => (
        <Tabs.Panel key={i} value={String(i)}>
          <Box mt="xs">
            <ResultTable
              columns={set.columns}
              rows={set.rows}
              rowCount={set.rows.length}
              durationMs={result.durationMs}
              resultIndex={i}
            />
          </Box>
        </Tabs.Panel>
      ))}
    </Tabs>
  );
}
