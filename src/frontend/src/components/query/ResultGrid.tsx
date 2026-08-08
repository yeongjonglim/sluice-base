import { Alert, Box, Code, Skeleton, Stack, Text } from "@mantine/core";
import type { RunEntry } from "@/api/useQueryRuns";
import { ApiError } from "@/api/client";
import { PlanSummaryBadges } from "@/components/query/PlanSummaryBadges";
import { ResultTable } from "@/components/query/ResultTable";

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
      reason?: string;
    } | null;
    const columns = body?.columns ?? [];
    return (
      <Alert color="orange" title="Query blocked — restricted columns" m="xs">
        {columns.length > 0 ? (
          <>
            <Text size="sm" mb="xs">
              Your query references columns you are not authorised to access:
            </Text>
            {columns.map((c, i) => (
              <Code key={i} display="block" fz="xs">
                {c.schema}.{c.table}.{c.column}
              </Code>
            ))}
          </>
        ) : (
          <Text size="sm">{body?.reason ?? "This query is blocked by the sensitive-column policy."}</Text>
        )}
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
  return (
    <Stack gap={0} h="100%">
      {entry.response?.estimate && (
        <Box px="xs" pt="xs">
          <PlanSummaryBadges summary={entry.response.estimate} label="Planner estimate" />
        </Box>
      )}
      <Box style={{ flex: 1, minHeight: 0 }}>
        <ResultTable
          columns={entry.response?.columns ?? []}
          rows={entry.response?.rows ?? []}
          rowCount={Number(entry.response?.rowCount ?? 0)}
          durationMs={Number(entry.response?.durationMs ?? 0)}
          resultIndex={entry.index}
        />
      </Box>
    </Stack>
  );
}
