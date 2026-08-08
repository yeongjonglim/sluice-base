import { useMemo } from "react";
import { Alert, Code, Collapse, Stack, Text, UnstyledButton } from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconChevronRight } from "@tabler/icons-react";
import type { ExplainEntry } from "@/api/useExplainRuns";
import { ApiError } from "@/api/client";
import { PlanSummaryBadges } from "@/components/query/PlanSummaryBadges";
import { PlanTree } from "@/components/query/PlanTree";
import { parsePlan } from "@/utils/queryPlan";

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

export function PlanView({ entry }: { entry: ExplainEntry }) {
  const [open, { toggle }] = useDisclosure(false);
  const parsed = useMemo(
    () => (entry.plan ? parsePlan(entry.plan.planJson) : null),
    [entry.plan],
  );

  if (entry.status === "pending") {
    return <Text p="xs" size="sm" c="dimmed">Analyzing…</Text>;
  }

  if (entry.status === "blocked") {
    const body = entry.error instanceof ApiError
      ? (entry.error.body as {
          columns?: Array<{ schema: string; table: string; column: string }>;
          reason?: string;
        } | null)
      : null;
    const columns = body?.columns ?? [];
    return (
      <Alert color="orange" title="Blocked — restricted columns" m="xs">
        {columns.length > 0 ? (
          columns.map((c, i) => (
            <Code key={i} display="block" fz="xs">{c.schema}.{c.table}.{c.column}</Code>
          ))
        ) : (
          <Text size="sm">{body?.reason ?? "This query is blocked by the sensitive-column policy."}</Text>
        )}
      </Alert>
    );
  }

  if (entry.status === "error" || !entry.plan) {
    const message = entry.error instanceof ApiError
      ? String(entry.error.body ?? entry.error.message)
      : "Could not analyze this statement.";
    return (
      <Alert color="red" title="Explain failed" m="xs">
        {message}
      </Alert>
    );
  }

  return (
    <Stack p="xs" gap="xs">
      <PlanSummaryBadges summary={entry.plan.summary} />
      {parsed && <PlanTree root={parsed} />}
      <UnstyledButton onClick={toggle}>
        <Text size="xs" c="dimmed" style={{ display: "flex", alignItems: "center", gap: 4 }}>
          <IconChevronRight
            size={12}
            style={{ transform: open ? "rotate(90deg)" : "none", transition: "transform 120ms" }}
          />
          Raw plan
        </Text>
      </UnstyledButton>
      <Collapse expanded={open || !parsed} keepMounted={false}>
        <Code block fz="xs" style={{ maxHeight: 320, overflow: "auto" }}>
          {prettyJson(entry.plan.planJson)}
        </Code>
      </Collapse>
    </Stack>
  );
}
