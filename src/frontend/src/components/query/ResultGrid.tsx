import {
  Alert, Button, Code, Flex, Group, ScrollArea, Skeleton, Stack, Table, Text,
} from "@mantine/core";
import { IconDownload } from "@tabler/icons-react";
import { ApiError } from "@/api/client";
import { exportToCsv } from "@/utils/csv.ts";
import type { RunEntry } from "@/api/useQueryRuns";

export function ResultGrid({ entry }: { entry: RunEntry }) {
  if (entry.status === "pending") {
    return (
      <Stack p="xs" gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (entry.status === "blocked") {
    const apiErr = entry.error instanceof ApiError ? entry.error : null;
    const body = apiErr?.body as {
      columns?: Array<{ schema: string; table: string; column: string }>;
    } | null;
    return (
      <Alert color="orange" title="Query blocked — restricted columns" m="xs">
        <Text size="sm" mb="xs">
          Your query references columns you are not authorised to access:
        </Text>
        {(body?.columns ?? []).map((c, i) => (
          <Code key={i} display="block" fz="xs">
            {c.schema}.{c.table}.{c.column}
          </Code>
        ))}
      </Alert>
    );
  }

  if (entry.status === "error") {
    if (entry.response?.error) {
      return (
        <Stack p="xs" gap="xs">
          <Text size="xs" c="dimmed">
            Error · {entry.response.durationMs} ms
          </Text>
          <Alert color="red" title="Query error">
            {entry.response.error}
          </Alert>
        </Stack>
      );
    }
    return (
      <Alert color="red" title="Request failed" m="xs">
        Could not reach the server. Check your connection and try again.
      </Alert>
    );
  }

  // success
  const columns = entry.response?.columns ?? [];
  const rows = entry.response?.rows ?? [];
  const rowCount = entry.response?.rowCount ?? 0;

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
          {rowCount} {rowCount === 1 ? "row" : "rows"} · {entry.response?.durationMs} ms
        </Text>
        <Button
          size="xs"
          variant="subtle"
          leftSection={<IconDownload size={12} />}
          onClick={() => exportToCsv(columns, rows, `query-results-${entry.index + 1}.csv`)}
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
