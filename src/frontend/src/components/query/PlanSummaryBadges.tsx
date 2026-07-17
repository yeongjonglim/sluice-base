import { Badge, Group, Text } from "@mantine/core";
import type { QueryPlanSummary } from "@/api/hooks";

function fmt(n: number | string): string {
  return new Intl.NumberFormat().format(Math.round(Number(n)));
}

export function PlanSummaryBadges({
  summary,
  label,
}: {
  summary: QueryPlanSummary;
  label?: string;
}) {
  return (
    <Group gap="xs" wrap="wrap" align="center">
      {label && (
        <Text size="xs" c="dimmed" fw={500}>
          {label}
        </Text>
      )}
      <Badge variant="light" color="gray">~{fmt(summary.estimatedRows)} rows</Badge>
      <Badge variant="light" color="gray">cost {fmt(summary.totalCost)}</Badge>
      <Badge variant="light" color="blue">{summary.rootNode}</Badge>
      {summary.hasSeqScan && (
        <Badge variant="light" color="orange">Full Table Scan</Badge>
      )}
      {summary.actualTotalMs != null && (
        <Badge variant="light" color="teal">{summary.actualTotalMs} ms actual</Badge>
      )}
    </Group>
  );
}
